# マウスでの範囲選択中に UI が固まる問題（IME / TSF）

- **発生**: 2026-09-04 報告
- **対策**: 2026-09-06 `src/Fusen/Helpers/ImeCompat.cs` を追加して解消
- **一行でいうと**: 選択範囲が変わるたびに WPF が IME(TSF) へ通知し、その COM 呼び出しの応答待ちで UI スレッドが最大 914ms 止まっていた。TSF を使わせないことで解消した。

再発したときに同じ回り道をしないよう、外した仮説も含めて残す。

---

## 1. 症状

- 付箋の本文をマウスの左ドラッグで範囲選択すると、途中で選択の色反転が止まり、しばらくしてから一気に追いつく
- **毎回は起きない**
- **マウスのドラッグのときだけ**。`Ctrl + @`（マーク）からのキーボード操作による範囲選択は正常
- 数行の短い選択でも起きる。本文の量・画像の有無・付箋の枚数とは無関係

## 2. 環境

計測時のマシン。タッチ／ペンは無く、リモートセッションでもない。

| 項目 | 値 |
| :--- | :--- |
| `SM_DIGITIZER` | `0x0`（タッチ・ペンなし） |
| `SM_REMOTESESSION` | `0`（ローカル） |
| `Tablet.TabletDevices.Count` | `0` |
| `RenderCapability.Tier` | 2（ハードウェア描画） |
| 実データ | 付箋 12 枚 / 最大 3,852 文字 / 画像 0 |

## 3. 原因

ウォッチドッグが UI 停止中に採取したダンプの、UI スレッドのスタック（下から上へ読む）。

```
HwndMouseInputProvider.FilterMessage           ← WM_MOUSEMOVE（ドラッグ中）
 TextBoxBase.OnMouseMove
  TextEditorMouse.OnMouseMoveWithFocus         ← 選択範囲を広げる
   TextRange.ChangeBlock.Dispose → EndChange
    TextSelection.NotifyChanged
     ITextStoreACPSink.OnSelectionChange()     ← IME(TSF) へ「選択が変わった」と通知
      TextStore.RequestLock → GrantLockWorker
       ITextStoreACPSink.OnLockGranted
        TextStore.ITfTextEditSink.OnEndEdit
         TextServicesDisplayAttributePropertyRanges.OnEndEdit
          GetDisplayAttribute
           TF_CreateCategoryMgr(...)           ← COM オブジェクトを毎回生成
            InterfaceMarshaler.ConvertToManaged
             SynchronizationContext.InvokeWaitMethodHelper
              WaitForMultipleObjectsEx         ← ここで 914ms 待っていた
```

ロード済みモジュールに `msctf.dll` / `msctfui.dll` / `textinputframework.dll`（Windows 11 の新しい入力基盤 = `TextInputHost.exe`）。

**選択範囲が変わるたびに TSF へ通知が飛び、その中で WPF が毎回 COM オブジェクトを作りに行って、IME 側の応答を待っている。** 症状がすべて説明できる。

- マウスドラッグのときだけ → マウスが動くたびに選択が変わる＝通知が毎回飛ぶ
- キーボードの範囲選択は平気 → キー1打ごとにしか選択が変わらず、通知の回数が桁違いに少ない
- 毎回は起きない → IME 側がすぐ応答すれば一瞬で終わる。遅れたときだけ待たされる
- 本文の量と無関係 → 通知1回あたりのコストは本文サイズに依存しない

Fusen 側のコードは無関係。WPF の `RichTextBox` と Windows の IME の間の問題。

## 4. 対策

`src/Fusen/Helpers/ImeCompat.cs` を追加し、`App.Application_Startup` の**先頭**で呼ぶ。

WPF が「TSF を使うか」を判定した結果をキャッシュしている内部フィールド
`MS.Internal.TextServicesLoader.s_servicesInstalled`（WindowsBase）を、起動時にリフレクションで
`NotInstalled` に倒す。

```
                          対策前          対策後
TextEditor.TextStore      TextStore  ->   null （TSF に通知しない）
TextEditor.ImmComposition ImmComposition  ImmComposition （IMM32 で日本語入力）
```

`TextStore` が作られなければ `ITextStoreACPSink` も無く、上のスタックの連鎖は**構造的に起こり得ない**。
日本語入力は `ImmComposition`（従来の IMM32 経路）が引き継ぐ。

### なぜこの方法なのか（他は効かなかった）

| 試した案 | 結果 |
| :--- | :--- |
| `InputMethod.SetIsInputMethodEnabled(rtb, false)` | **効かない**。切り替えても `TextEditor.TextStore` は生きたまま |
| WPF 公式の `AppContext` スイッチ | **存在しない**。PresentationCore / PresentationFramework / WindowsBase の全スイッチ名を洗い出して確認済み（Stylus 用はあるが TSF 用は無い） |
| Windows の設定「以前のバージョンの Microsoft IME を使う」 | 効く見込みだが、**各PCで設定を変えてもらう必要がある**ため配布物の対策にならない |

### 注意点

- **起動時、ウィンドウを1枚も作る前に呼ぶこと。** 最初のテキストコントロールがフォーカスを得た時点で判定結果が使われるため、それより後では効かない
- WPF の非公開フィールドに依存する。将来の .NET で名前が変われば効かなくなるので、`try/catch` で握り潰し、**失敗したら従来どおり動く**ようにしてある
- 日本語入力が IMM32 経路になる。標準の Microsoft IME は問題ないが、**TSF 専用のテキストサービス（一部のサードパーティ IME、手書き入力、再変換）は影響を受ける可能性がある**。その場合は `App.Application_Startup` の呼び出しを外す
- `fusen.ini` に切り替え設定は設けていない。設定を触らない利用者も自動的に救うため

## 5. 効果

| | 左ボタン押下中の UI 停止（最大） | 内容 |
| :--- | ---: | :--- |
| 対策前（約17分の使用） | **914 ms** | IME(TSF) の応答待ち |
| 対策後（約2日の使用・12件） | 661 ms | ウィンドウの移動/リサイズ（後述・正常動作） |

対策後に採取したダンプのスタックには TSF が一切現れず、代わりに次の形になっていた。

```
Dispatcher.PushFrameImpl → DispatchMessage
  SubclassWndProc → DefWndProcWrapper → CallWindowProc   ← Win32 の DefWindowProc
    SubclassWndProc → DefWndProcWrapper → CallWindowProc  ← 入れ子（モーダルループ）
```

これは**ヘッダーを掴んでの移動（`DragMove`）や端を掴んでのリサイズ**で、Windows 自身がメッセージループを握っている状態。Win32 の仕様どおりの動作であり不具合ではない。ウォッチドッグは「UI スレッドが止まったか」しか見ないため、付箋を動かすたびに1件記録される。

## 6. 調査で外した仮説（実測値つき）

再発時に同じ道を辿らないための記録。すべて計測して否定した。

| 仮説 | 実測 |
| :--- | :--- |
| 透過（`AllowsTransparency`）＋ `DropShadowEffect` の描画コスト | 影を消して不透明にしても停止 1,342ms。**シロ** |
| 本文中の画像 | 2400×1600 の PNG 3枚ありと画像なしで差なし（826ms / 749ms）。**シロ** |
| 付箋の枚数 | 8枚同時でも変化なし。**シロ** |
| 直前の入力（自動保存400ms・画像同期600msのタイマー） | ドラッグ中に発火させても停止 0 回。**シロ** |
| `WM_NCHITTEST` フック（`PointFromScreen`） | ドラッグ中 31 回 / 合計 0.5ms。**シロ** |
| 高ポーリングレートのマウスによるメッセージ洪水 | Windows 側が座標を間引くため `WM_MOUSEMOVE` は激減する。**シロ** |
| WPF のスタイラス/タッチ（WISP）スタック | `Tablet.TabletDevices.Count = 0`。**シロ** |
| クリップボード（`ApplicationCommands.Paste` の `CanExecute`） | 別問題として実在（後述）。ただしドラッグ選択では踏まない。**シロ** |
| 長い本文＋オートスクロールでのレイアウト | 素の WPF `RichTextBox` でも同等に発生する WPF の性質。実データ（最大3,852文字）では規模が合わない。**シロ** |
| 既存の選択範囲の内側から掴んだことによるドラッグ＆ドロップ | 「何も選択していない状態から」という条件に合わない。**シロ** |

**合成マウス入力（`SetCursorPos` / `mouse_event`）では再現しなかった。** 実機の IME が絡んで初めて出るため、自動操作での再現は諦めて、実使用中に証拠を採る方式（次章）に切り替えたことで特定できた。

## 7. 再発したときの手順

### 7.1 調査用ビルドの作り方

`src/Fusen` をリポジトリ外にコピーし、`AssemblyName` を `fusen-diag` に変えて下記 `Diag.cs` を足す。
`App.Application_Startup` の末尾で `Diag.Start();` を呼ぶ。

ビルドしたものを **`bin/publish/` に置く**と、`data/` と `fusen.ini` の探索が実行ファイル直下なので、
いつもの付箋のまま再現待ちができる。**通常の `fusen.exe` は必ず終了させてから起動すること**
（同じ `notes.json` を2プロセスで書くと壊れる）。

```csharp
// Diag.cs — UI スレッドの停止を検出し、止まっている最中にダンプを取る
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace Fusen
{
    internal static class Diag
    {
        const int StallLogMs = 150;   // これを超えたらログに残す
        const int DumpMs = 400;       // これを超えたらダンプを取る
        const int MaxDumps = 5;

        [DllImport("dbghelp.dll", SetLastError = true)]
        static extern bool MiniDumpWriteDump(IntPtr hProcess, uint pid, IntPtr hFile, int type,
                                             IntPtr ex, IntPtr user, IntPtr callback);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        struct POINT { public int X, Y; }

        static readonly object LogLock = new object();
        static readonly string Dir = AppDomain.CurrentDomain.BaseDirectory;
        static string _logPath = "";
        static int _dumps;

        public static void Start()
        {
            _logPath = Path.Combine(Dir, "fusen-stall.log");
            Log($"===== 調査ビルド起動 {DateTime.Now:yyyy-MM-dd HH:mm:ss} pid={GetCurrentProcessId()} =====");

            var dispatcher = Application.Current.Dispatcher;
            var th = new Thread(() => Watch(dispatcher))
            { IsBackground = true, Name = "fusen-diag-watchdog", Priority = ThreadPriority.AboveNormal };
            th.Start();
        }

        static void Watch(Dispatcher dispatcher)
        {
            var sw = Stopwatch.StartNew();
            long seq = 0, ack = 0;

            while (!dispatcher.HasShutdownStarted)
            {
                long n = ++seq;
                double t0 = sw.Elapsed.TotalMilliseconds;
                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Input,
                        new Action(() => Interlocked.Exchange(ref ack, n)));
                }
                catch { return; }

                bool dumped = false;
                double waited;
                while (Interlocked.Read(ref ack) != n)
                {
                    Thread.Sleep(15);
                    waited = sw.Elapsed.TotalMilliseconds - t0;
                    if (!dumped && waited >= DumpMs && _dumps < MaxDumps) { dumped = true; CaptureDump(waited); }
                    if (waited > 30000) break;
                }

                waited = sw.Elapsed.TotalMilliseconds - t0;
                if (waited >= StallLogMs)
                {
                    GetCursorPos(out var pt);
                    bool lbutton = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                    Log($"[{DateTime.Now:HH:mm:ss.fff}] UI 停止 {waited:F0} ms  " +
                        $"左ボタン={(lbutton ? "押下中" : "離れている")}  カーソル=({pt.X},{pt.Y})" +
                        (dumped ? "  -> ダンプ取得" : ""));
                }
                Thread.Sleep(50);
            }
        }

        static void CaptureDump(double waited)
        {
            try
            {
                _dumps++;
                var path = Path.Combine(Dir, $"fusen-stall-{DateTime.Now:yyyyMMdd_HHmmss_fff}.dmp");
                using var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
                const int type = 0x1 | 0x4 | 0x400 | 0x800 | 0x1000; // DataSegs/Handle/PrivateRW/FullMemInfo/ThreadInfo
                bool ok = MiniDumpWriteDump(GetCurrentProcess(), GetCurrentProcessId(),
                                            fs.SafeFileHandle.DangerousGetHandle(), type,
                                            IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Log($"    ダンプ {(ok ? "作成" : "失敗")}: {Path.GetFileName(path)} ({waited:F0} ms 経過時点)");
            }
            catch (Exception ex) { Log("    ダンプ失敗: " + ex.Message); }
        }

        static void Log(string line)
        {
            try { lock (LogLock) File.AppendAllText(_logPath, line + Environment.NewLine, new UTF8Encoding(true)); }
            catch { }
        }
    }
}
```

**要点は「止まった後」ではなく「止まっている最中」にダンプを取ること。** 別スレッドから
`Dispatcher.BeginInvoke(DispatcherPriority.Input, ...)` を投げ、返ってこない間に採取する。
これで UI スレッドが何を実行中に止まったのかがそのまま残る。

### 7.2 ダンプの読み方

```
dotnet tool install -g dotnet-dump
dotnet-dump analyze fusen-stall-YYYYMMDD_HHmmss_fff.dmp
> clrstack        # UI スレッド（OS Thread 0）のスタック
> modules         # ロード済み DLL（msctf.dll などの有無を見る）
```

`.pdb` を dmp と同じ場所に置いておくと行番号まで出る。

### 7.3 判断の分かれ道

- **ログに `左ボタン=押下中` の行が出る** → UI スレッドが詰まっている。ダンプから原因の関数を特定できる
- **固まったのにログが空** → UI スレッドは動いていたことになる。犯人は描画側（GPU / ドライバ / DWM）で、探す場所がまるごと変わる

## 8. 副産物：右クリックメニューで約1秒固まる件（未対応）

調査中に見つけた別問題。**今回の症状とは別で、まだ直していない。**

`RichTextBox` に直付けしたコンテキストメニューの `ApplicationCommands.Paste` は、`CanExecute` の評価で
OLE クリップボードを開きにいく。他プロセスがクリップボードを掴んでいると、WPF 内部のリトライ
（100ms × 10回）で丸ごとブロックする。

```
[A0] Paste.CanExecute(target=RichTextBox) = False  1038.1 ms
[C ] Clipboard.ContainsText()                      1007.1 ms   ← COMException
```

メニューが開いている間だけの問題（閉じると `CommandTarget` が null に戻り、requery は 0.8ms）。
リモートデスクトップやクリップボード常駐ツールを使う環境では踏みやすい。

## 9. 参考：入力の重さ（未対応）

`NoteRichTextBox_TextChanged` は1文字ごとに `NormalizeParagraphSpacing` + `GetPlainText` +
`SaveToXaml`（本文全体の XAML 直列化）を回している。約4,000文字＋画像1枚の付箋で
**1打鍵あたり 25.9ms**。本文サイズに比例するため、極端に長い付箋では入力が重くなる。
