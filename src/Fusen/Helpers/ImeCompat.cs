using System;
using System.Linq;
using System.Reflection;
using System.Windows;

namespace Fusen.Helpers
{
    /// <summary>
    /// WPF に TSF (Text Services Framework) を使わせず、従来の IMM32 経路で日本語入力させる。
    ///
    /// TSF が有効だと、<c>RichTextBox</c> は選択範囲が変わるたびに TSF へ通知する
    /// (<c>ITextStoreACPSink.OnSelectionChange</c>)。WPF はその処理の中で毎回
    /// <c>TF_CreateCategoryMgr</c> で COM オブジェクトを作りに行き、IME 側の応答を待つ。
    /// この待ちで UI スレッドが数百ms〜1秒止まることがある。
    ///
    /// マウスのドラッグで範囲選択すると、マウスが動くたびに通知が飛ぶため踏みやすい。
    /// 症状は「ドラッグしても選択の色反転が止まり、しばらくしてから一気に追いつく」。
    /// キーボードでの範囲選択 (<c>Ctrl + @</c> 等) が平気なのは、通知の回数が桁違いに少ないため。
    ///
    /// WPF には TSF を切る公開スイッチが無いため、内部の判定結果
    /// (<c>MS.Internal.TextServicesLoader.s_servicesInstalled</c>) を「未インストール」に倒す。
    /// これで <c>TextEditor.TextStore</c> が作られなくなり、通知経路そのものが消える。
    /// 日本語入力は <c>ImmComposition</c> (IMM32) が引き継ぐ。
    ///
    /// 内部実装に依存するため、将来の .NET で通用しなくなる可能性がある。
    /// その場合は何もせず従来どおり動くよう、失敗は握り潰す。
    /// 詳しい調査経緯は docs/memos/2026-09-06-mouse-selection-freeze.md を参照。
    /// </summary>
    public static class ImeCompat
    {
        /// <summary>
        /// TSF を無効化する。ウィンドウを1つも作る前（アプリ起動直後）に呼ぶこと。
        /// 最初のテキストコントロールがフォーカスを得た時点で判定結果が使われるため、
        /// それより後に呼んでも効かない。
        /// </summary>
        /// <returns>結果の説明。ログや調査用で、失敗しても呼び出し側は続行してよい。</returns>
        public static string DisableTextServicesFramework()
        {
            try
            {
                // TextServicesLoader は WindowsBase 側にある
                var loader = typeof(DependencyObject).Assembly.GetType("MS.Internal.TextServicesLoader");
                if (loader == null) return "TextServicesLoader が見つからないため何もしない";

                var field = loader.GetFields(BindingFlags.NonPublic | BindingFlags.Static)
                                  .FirstOrDefault(f => f.FieldType.IsEnum
                                      && f.Name.IndexOf("servicesInstalled", StringComparison.OrdinalIgnoreCase) >= 0);
                if (field == null) return "s_servicesInstalled が見つからないため何もしない";

                var notInstalled = Enum.GetValues(field.FieldType).Cast<object>()
                    .FirstOrDefault(v => v.ToString()!.IndexOf("NotInstalled", StringComparison.OrdinalIgnoreCase) >= 0);
                if (notInstalled == null) return "NotInstalled 値が無いため何もしない";

                var before = field.GetValue(null);
                field.SetValue(null, notInstalled);
                return $"TSF を無効化した（{before} -> {field.GetValue(null)}）";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ImeCompat] DisableTextServicesFramework error: {ex.Message}");
                return "TSF の無効化に失敗（従来どおり動作する）: " + ex.Message;
            }
        }
    }
}
