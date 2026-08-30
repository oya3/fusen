using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Fusen.Helpers
{
    /// <summary>
    /// bash (GNU Readline / Emacs スタイル) のキーバインドを RichTextBox に与えるハンドラ。
    ///
    /// fusen.ini の [Bash] Enabled = true のときだけ NoteWindow から接続される。
    /// 無効時は一切関与しないため、既定の編集操作はそのまま残る。
    ///
    /// マーク（Ctrl+@ / Ctrl+Space）を置くと、以降のカーソル移動がマークからの範囲選択になる。
    /// WPF の選択は「アンカー位置」と「移動位置」で表現されるため、こちらでも移動位置を
    /// _point として持ち、移動のたびに Select(_mark, _point) で選択し直す。
    /// キャレットは選択設定後に移動位置側へ移るとは限らないので、_point を正とする。
    /// </summary>
    public class BashKeyHandler
    {
        private readonly RichTextBox _rtb;

        /// <summary>Ctrl+@ / Ctrl+Space で置いたマーク。null なら範囲選択中でない。</summary>
        private TextPointer? _mark;

        /// <summary>マークがあるときの移動位置（キャレット相当）。</summary>
        private TextPointer? _point;

        /// <summary>直近にキルしたテキスト。Ctrl+Y で貼り付ける。</summary>
        private string _killBuffer = string.Empty;

        /// <summary>Ctrl+S / Ctrl+R のインクリメンタル検索。</summary>
        private readonly IncrementalSearch _search;

        /// <summary>検索の状態が変わったときに呼ばれる（検索バーの表示更新用）。</summary>
        public event Action<SearchDisplayState>? SearchStateChanged;

        public bool IsSearching => _search.IsActive;

        public BashKeyHandler(RichTextBox richTextBox)
        {
            _rtb = richTextBox ?? throw new ArgumentNullException(nameof(richTextBox));
            _search = new IncrementalSearch(_rtb);
        }

        /// <summary>検索中に入力された文字を検索文字列へ追加する。NoteWindow の PreviewTextInput から呼ぶ。</summary>
        public bool HandleTextInput(string text)
        {
            if (!_search.IsActive || string.IsNullOrEmpty(text)) return false;

            // 制御文字は検索文字列に入れない
            if (text.Length == 1 && char.IsControl(text[0])) return true;

            _search.AppendChar(text);
            NotifySearchState();
            return true;
        }

        /// <summary>検索を打ち切る（フォーカスを失ったときや折りたたみ時に呼ぶ）。</summary>
        public void EndSearch()
        {
            if (!_search.IsActive) return;

            _search.Accept();
            NotifySearchState();
        }

        private void NotifySearchState() => SearchStateChanged?.Invoke(_search.State);

        /// <summary>
        /// 検索中のキー操作。検索は入力を横取りするモードなので、通常のキーバインドより先に通す。
        /// </summary>
        private bool HandleKeyWhileSearching(Key key, bool ctrl, bool alt)
        {
            if (key == Key.Escape || (ctrl && key == Key.G))
            {
                _search.Abort();
                NotifySearchState();
                return true;
            }

            if (key == Key.Return || key == Key.Enter)
            {
                _search.Accept();
                NotifySearchState();
                return true;
            }

            if (key == Key.Back)
            {
                _search.Backspace();
                NotifySearchState();
                return true;
            }

            if (ctrl && key == Key.S)
            {
                _search.Advance(forward: true);
                NotifySearchState();
                return true;
            }

            if (ctrl && key == Key.R)
            {
                _search.Advance(forward: false);
                NotifySearchState();
                return true;
            }

            if (ctrl && key == Key.W)
            {
                // 検索中の Ctrl+W は削除ではなく「カーソル位置の単語を検索文字列に取り込む」
                _search.YankWord();
                NotifySearchState();
                return true;
            }

            if (ctrl && key == Key.Y)
            {
                _search.AppendText(_killBuffer);
                NotifySearchState();
                return true;
            }

            // 修飾キー単独は無視して検索を続ける
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.System)
            {
                return false;
            }

            // それ以外の操作キーは検索を確定してから、通常の処理へ渡す
            if (ctrl || alt || key is Key.Left or Key.Right or Key.Up or Key.Down
                             or Key.Home or Key.End or Key.PageUp or Key.PageDown)
            {
                _search.Accept();
                NotifySearchState();
                return false;
            }

            // 通常の文字入力は PreviewTextInput 側で検索文字列に足すため、ここでは何もしない
            return false;
        }

        /// <summary>
        /// キー入力を処理する。処理した場合は true を返し、呼び出し側で e.Handled = true とする。
        /// </summary>
        public bool HandleKey(KeyEventArgs e)
        {
            // Alt 併用時、WPF は e.Key に Key.System を入れ、実際のキーを e.SystemKey に入れる
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;

            bool ctrl = (mods & ModifierKeys.Control) == ModifierKeys.Control;
            bool alt = (mods & ModifierKeys.Alt) == ModifierKeys.Alt;
            bool shift = (mods & ModifierKeys.Shift) == ModifierKeys.Shift;

            // インクリメンタル検索中は入力を横取りする
            if (_search.IsActive)
            {
                return HandleKeyWhileSearching(key, ctrl, alt);
            }

            // ESC: マーク・選択のキャンセル
            if (key == Key.Escape && !ctrl && !alt)
            {
                ClearMark();
                return true;
            }

            if (alt && !ctrl)
            {
                switch (key)
                {
                    case Key.F:
                        Move(EditingCommands.MoveRightByWord);
                        return true;
                    case Key.B:
                        Move(EditingCommands.MoveLeftByWord);
                        return true;
                    case Key.D:
                        KillNextWord();
                        return true;
                    case Key.W:
                        // 選択範囲のコピー。非選択時は何もしない
                        CopyRegion();
                        return true;
                }
                return false;
            }

            if (!ctrl || alt)
            {
                return false;
            }

            switch (key)
            {
                // ---- カーソル移動 ----
                case Key.A:
                    Move(EditingCommands.MoveToLineStart);
                    return true;
                case Key.E:
                    Move(EditingCommands.MoveToLineEnd);
                    return true;
                case Key.F:
                    Move(EditingCommands.MoveRightByCharacter);
                    return true;
                case Key.B:
                    Move(EditingCommands.MoveLeftByCharacter);
                    return true;
                case Key.P:
                    Move(EditingCommands.MoveUpByLine);
                    return true;
                case Key.N:
                    Move(EditingCommands.MoveDownByLine);
                    return true;

                // ---- マーク ----
                case Key.Space:
                    SetMark();
                    return true;
                // Ctrl+@ : JIS配列では @ 単独キー、US配列では Shift+2
                case Key.Oem3:
                    SetMark();
                    return true;
                case Key.D2 when shift:
                    SetMark();
                    return true;

                // ---- 削除・キル ----
                case Key.W:
                    CutRegionOrPreviousWord();
                    return true;
                case Key.K:
                    KillToLineEnd();
                    return true;
                case Key.U:
                    KillToLineStart();
                    return true;
                case Key.D:
                    DeleteForward();
                    return true;
                case Key.H:
                    DeleteBackward();
                    return true;

                // ---- ヤンク ----
                case Key.Y:
                    Yank();
                    return true;

                // ---- インクリメンタル検索 ----
                case Key.S:
                    ClearMark();
                    _search.Start(forward: true);
                    NotifySearchState();
                    return true;
                case Key.R:
                    ClearMark();
                    _search.Start(forward: false);
                    NotifySearchState();
                    return true;

                // ---- その他 ----
                case Key.T:
                    TransposeChars();
                    return true;
                case Key.G:
                    ClearMark();
                    return true;
                case Key.Z:
                    Undo();
                    return true;
                // Ctrl+_ : US配列では Shift+'-'、JIS配列では Shift+'ろ'
                case Key.OemMinus:
                case Key.OemBackslash:
                    Undo();
                    return true;
            }

            return false;
        }

        // ==================== マークと移動 ====================

        private void SetMark()
        {
            _mark = _rtb.CaretPosition;
            _point = _rtb.CaretPosition;
            _rtb.Selection.Select(_mark, _point);
        }

        private void ClearMark()
        {
            _mark = null;
            _point = null;
            var caret = _rtb.CaretPosition;
            _rtb.Selection.Select(caret, caret);
        }

        /// <summary>
        /// カーソルを動かす。マークがある場合はマークとの間を選択し直す。
        /// </summary>
        private void Move(RoutedUICommand command)
        {
            if (_mark == null || _point == null)
            {
                command.Execute(null, _rtb);
                return;
            }

            // 一度選択を畳んでから移動しないと、移動コマンドが選択の端を基準にしてしまう
            _rtb.Selection.Select(_point, _point);
            _rtb.CaretPosition = _point;

            command.Execute(null, _rtb);

            _point = _rtb.CaretPosition;
            _rtb.Selection.Select(_mark, _point);
        }

        // ==================== キル・ヤンク ====================

        /// <summary>キルしたテキストを保持し、システムクリップボードにも反映する。</summary>
        private void SetKillBuffer(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            _killBuffer = text;
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BashKeyHandler] Clipboard.SetText error: {ex.Message}");
            }
        }

        /// <summary>選択範囲をキルバッファへ移し、本文から削除する。</summary>
        private void CutSelection()
        {
            if (_rtb.Selection.IsEmpty) return;

            SetKillBuffer(_rtb.Selection.Text);
            _rtb.Selection.Text = string.Empty;
            ClearMark();
        }

        private void CopyRegion()
        {
            if (_rtb.Selection.IsEmpty) return;

            SetKillBuffer(_rtb.Selection.Text);
            ClearMark();
        }

        /// <summary>Ctrl+W: 選択範囲のカット。非選択時は直前の1単語を削除。</summary>
        private void CutRegionOrPreviousWord()
        {
            if (!_rtb.Selection.IsEmpty)
            {
                CutSelection();
                return;
            }

            EditingCommands.SelectLeftByWord.Execute(null, _rtb);
            CutSelection();
        }

        /// <summary>Alt+D: 直後の1単語を削除。</summary>
        private void KillNextWord()
        {
            if (!_rtb.Selection.IsEmpty)
            {
                CutSelection();
                return;
            }

            EditingCommands.SelectRightByWord.Execute(null, _rtb);
            CutSelection();
        }

        /// <summary>Ctrl+K: カーソルから行末まで削除。行末にいる場合は改行を1つ削除して次行を連結する。</summary>
        private void KillToLineEnd()
        {
            if (!_rtb.Selection.IsEmpty)
            {
                CutSelection();
                return;
            }

            EditingCommands.SelectToLineEnd.Execute(null, _rtb);

            if (_rtb.Selection.IsEmpty)
            {
                EditingCommands.Delete.Execute(null, _rtb);
                return;
            }

            CutSelection();
        }

        /// <summary>Ctrl+U: カーソルから行頭まで削除。</summary>
        private void KillToLineStart()
        {
            if (!_rtb.Selection.IsEmpty)
            {
                CutSelection();
                return;
            }

            EditingCommands.SelectToLineStart.Execute(null, _rtb);
            CutSelection();
        }

        /// <summary>Ctrl+Y: 直前にキルしたテキストを貼り付ける。キルバッファが空ならクリップボードを使う。</summary>
        private void Yank()
        {
            string text = _killBuffer;
            if (string.IsNullOrEmpty(text))
            {
                try
                {
                    text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[BashKeyHandler] Clipboard.GetText error: {ex.Message}");
                    text = string.Empty;
                }
            }

            if (string.IsNullOrEmpty(text)) return;

            _rtb.Selection.Text = text;
            _rtb.CaretPosition = _rtb.Selection.End;
            ClearMark();
        }

        // ==================== 1文字操作 ====================

        private void DeleteForward()
        {
            if (!_rtb.Selection.IsEmpty)
            {
                _rtb.Selection.Text = string.Empty;
                ClearMark();
                return;
            }

            EditingCommands.Delete.Execute(null, _rtb);
        }

        private void DeleteBackward()
        {
            if (!_rtb.Selection.IsEmpty)
            {
                _rtb.Selection.Text = string.Empty;
                ClearMark();
                return;
            }

            EditingCommands.Backspace.Execute(null, _rtb);
        }

        /// <summary>
        /// Ctrl+T: カーソル前後の1文字を入れ替える。
        /// readline と同様、行末では直前の2文字を入れ替える。
        /// </summary>
        private void TransposeChars()
        {
            ClearMark();

            var point = _rtb.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var prev = point.GetNextInsertionPosition(LogicalDirection.Backward);
            if (prev == null) return;

            var next = point.GetNextInsertionPosition(LogicalDirection.Forward);

            // 行末・文末では直前の2文字を対象にする
            if (next == null || !IsSingleCharOnSameLine(point, next))
            {
                next = point;
                point = prev;
                prev = point.GetNextInsertionPosition(LogicalDirection.Backward);
                if (prev == null) return;
            }

            if (!IsSingleCharOnSameLine(prev, point) || !IsSingleCharOnSameLine(point, next)) return;

            string left = new TextRange(prev, point).Text;
            string right = new TextRange(point, next).Text;

            var target = new TextRange(prev, next);
            target.Text = right + left;

            _rtb.CaretPosition = target.End;
        }

        /// <summary>2点の間がちょうど1文字（改行などを跨がない）かどうか。</summary>
        private static bool IsSingleCharOnSameLine(TextPointer start, TextPointer end)
        {
            var text = new TextRange(start, end).Text;
            return text.Length == 1 && text != "\r" && text != "\n";
        }

        private void Undo()
        {
            ClearMark();
            if (_rtb.CanUndo)
            {
                _rtb.Undo();
            }
        }
    }
}
