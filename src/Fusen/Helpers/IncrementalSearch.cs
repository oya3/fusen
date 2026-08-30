using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Documents;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Fusen.Helpers
{
    /// <summary>検索バーの表示内容。</summary>
    public readonly struct SearchDisplayState
    {
        public bool IsActive { get; init; }
        public bool Forward { get; init; }
        public string Query { get; init; }
        public bool Found { get; init; }
        public bool Wrapped { get; init; }
    }

    /// <summary>
    /// bash / Emacs のインクリメンタル検索 (isearch) を RichTextBox に対して行う。
    ///
    /// 検索中は「開始位置」を覚えておき、Ctrl+G / ESC で中断したときにそこへ戻す。
    /// 大文字小文字は Emacs と同じスマートケース（検索文字列が全て小文字なら区別しない）。
    ///
    /// 文書の走査は、TextPointer を辿って本文を1本の文字列へ平坦化し、
    /// 文字位置から TextPointer を引ける対応表を同時に作る方式で行う。
    /// 付箋1枚分の文字数であればキー入力のたびに作り直しても十分速い。
    /// </summary>
    public class IncrementalSearch
    {
        private readonly RichTextBox _rtb;

        private bool _active;
        private bool _forward = true;
        private bool _found = true;
        private bool _wrapped;
        private string _query = string.Empty;

        /// <summary>検索を開始した位置。中断時にここへ戻す。</summary>
        private TextPointer? _origin;

        /// <summary>現在の一致箇所の開始位置（平坦化した文字列上の添字）。未一致なら -1。</summary>
        private int _matchIndex = -1;

        public bool IsActive => _active;

        public IncrementalSearch(RichTextBox richTextBox)
        {
            _rtb = richTextBox ?? throw new ArgumentNullException(nameof(richTextBox));
        }

        public SearchDisplayState State => new()
        {
            IsActive = _active,
            Forward = _forward,
            Query = _query,
            Found = _found,
            Wrapped = _wrapped,
        };

        /// <summary>検索を開始する。既に検索中なら次の候補へ進む。</summary>
        public void Start(bool forward)
        {
            if (_active)
            {
                Advance(forward);
                return;
            }

            _active = true;
            _forward = forward;
            _found = true;
            _wrapped = false;
            _query = string.Empty;
            _matchIndex = -1;
            _origin = _rtb.CaretPosition;
        }

        /// <summary>確定して検索を終了する。カーソルは一致箇所に残る。</summary>
        public void Accept()
        {
            if (!_active) return;

            _active = false;
            _query = string.Empty;
            _matchIndex = -1;
            _origin = null;

            // 一致箇所の選択は解除し、末尾にキャレットを置く
            var end = _rtb.Selection.End;
            _rtb.Selection.Select(end, end);
            _rtb.CaretPosition = end;
        }

        /// <summary>中断して検索を終了する。カーソルは開始位置へ戻す。</summary>
        public void Abort()
        {
            if (!_active) return;

            var origin = _origin;

            _active = false;
            _query = string.Empty;
            _matchIndex = -1;
            _origin = null;

            if (origin != null)
            {
                _rtb.Selection.Select(origin, origin);
                _rtb.CaretPosition = origin;
            }
        }

        /// <summary>検索文字列に1文字追加する。</summary>
        public void AppendChar(string text)
        {
            if (!_active || string.IsNullOrEmpty(text)) return;

            _query += text;
            Search(fromIndex: _matchIndex >= 0 ? _matchIndex : CurrentCaretIndex(), _forward);
        }

        /// <summary>検索文字列の末尾を1文字削る。空になったら一致表示を消す。</summary>
        public void Backspace()
        {
            if (!_active || _query.Length == 0) return;

            _query = _query.Substring(0, _query.Length - 1);

            if (_query.Length == 0)
            {
                _found = true;
                _wrapped = false;
                _matchIndex = -1;
                if (_origin != null)
                {
                    _rtb.Selection.Select(_origin, _origin);
                }
                return;
            }

            Search(fromIndex: _origin != null ? IndexOfPointer(_origin) : 0, _forward);
        }

        /// <summary>次（前）の候補へ進む。</summary>
        public void Advance(bool forward)
        {
            if (!_active) return;

            _forward = forward;

            if (_query.Length == 0) return;

            int from = _matchIndex >= 0
                ? (forward ? _matchIndex + 1 : _matchIndex - 1)
                : CurrentCaretIndex();

            Search(from, forward);
        }

        /// <summary>
        /// Ctrl+W: 一致箇所（無ければキャレット）から続く1単語を検索文字列に取り込む。
        /// Emacs の isearch-yank-word-or-char に相当する。
        /// </summary>
        public void YankWord()
        {
            if (!_active) return;

            var (text, _) = BuildFlatText();
            int start = _matchIndex >= 0 ? _matchIndex + _query.Length : CurrentCaretIndex();
            if (start < 0 || start >= text.Length) return;

            int i = start;

            // 単語の手前にある区切り文字は1文字だけ取り込む（Emacs の word-or-char 相当）
            if (!IsWordChar(text[i]))
            {
                _query += text[i];
                Search(_matchIndex >= 0 ? _matchIndex : CurrentCaretIndex(), _forward);
                return;
            }

            var sb = new StringBuilder();
            while (i < text.Length && IsWordChar(text[i]))
            {
                sb.Append(text[i]);
                i++;
            }

            if (sb.Length == 0) return;

            _query += sb.ToString();
            Search(_matchIndex >= 0 ? _matchIndex : CurrentCaretIndex(), _forward);
        }

        /// <summary>任意の文字列を検索文字列に足す（Ctrl+Y でキルバッファを取り込む用）。</summary>
        public void AppendText(string text)
        {
            if (!_active || string.IsNullOrEmpty(text)) return;

            _query += text;
            Search(_matchIndex >= 0 ? _matchIndex : CurrentCaretIndex(), _forward);
        }

        // ==================== 検索本体 ====================

        private void Search(int fromIndex, bool forward)
        {
            var (text, map) = BuildFlatText();

            if (_query.Length == 0 || text.Length == 0)
            {
                _found = _query.Length == 0;
                _matchIndex = -1;
                return;
            }

            // スマートケース: 検索文字列が全て小文字なら大文字小文字を区別しない
            var comparison = HasUpper(_query)
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (fromIndex < 0) fromIndex = 0;
            if (fromIndex > text.Length) fromIndex = text.Length;

            int index = forward
                ? IndexOfForward(text, fromIndex, comparison)
                : IndexOfBackward(text, fromIndex, comparison);

            bool wrapped = false;

            // 見つからなければ端から折り返して再検索する
            if (index < 0)
            {
                index = forward
                    ? IndexOfForward(text, 0, comparison)
                    : IndexOfBackward(text, text.Length, comparison);
                wrapped = index >= 0;
            }

            if (index < 0)
            {
                _found = false;
                _wrapped = false;
                _matchIndex = -1;
                return;
            }

            _found = true;
            _wrapped = wrapped;
            _matchIndex = index;

            // 終端は「次の文字の位置」ではなく「最後の文字の直後」を使う。
            // 次の文字は別の段落に属することがあり、その位置まで選ぶと改行まで選択に入ってしまう。
            var start = map[index];
            var lastChar = map[index + _query.Length - 1];
            var end = lastChar.GetPositionAtOffset(1, LogicalDirection.Forward) ?? lastChar;

            _rtb.Selection.Select(start, end);
        }

        private int IndexOfForward(string text, int from, StringComparison comparison)
        {
            if (from > text.Length - _query.Length) return -1;
            return text.IndexOf(_query, from, comparison);
        }

        private int IndexOfBackward(string text, int from, StringComparison comparison)
        {
            int start = Math.Min(from - 1, text.Length - _query.Length);
            for (int i = start; i >= 0; i--)
            {
                if (string.Compare(text, i, _query, 0, _query.Length, comparison) == 0)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool HasUpper(string s)
        {
            foreach (var c in s)
            {
                if (char.IsUpper(c)) return true;
            }
            return false;
        }

        private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

        // ==================== 文書の平坦化 ====================

        /// <summary>
        /// 本文を1本の文字列に平坦化し、文字位置 -> TextPointer の対応表を作る。
        /// 対応表は末尾に文書終端を1つ余分に持つため、要素数は文字数 + 1 になる。
        ///
        /// 段落の終わりには改行を1文字挿入する。これが無いと前の行の末尾と次の行の先頭が
        /// 地続きに見えてしまい、行をまたいだ誤一致や Ctrl+W での単語の取り込みすぎが起きる。
        /// </summary>
        private (string text, List<TextPointer> map) BuildFlatText()
        {
            var sb = new StringBuilder();
            var map = new List<TextPointer>();

            var doc = _rtb.Document;
            var position = doc.ContentStart;

            while (position != null)
            {
                var context = position.GetPointerContext(LogicalDirection.Forward);

                if (context == TextPointerContext.Text)
                {
                    string run = position.GetTextInRun(LogicalDirection.Forward);
                    for (int i = 0; i < run.Length; i++)
                    {
                        sb.Append(run[i]);
                        map.Add(position.GetPositionAtOffset(i));
                    }
                    position = position.GetPositionAtOffset(run.Length);
                }
                else
                {
                    if (context == TextPointerContext.ElementEnd && position.Parent is Paragraph)
                    {
                        sb.Append('\n');
                        map.Add(position);
                    }
                    position = position.GetNextContextPosition(LogicalDirection.Forward);
                }
            }

            map.Add(doc.ContentEnd);
            return (sb.ToString(), map);
        }

        /// <summary>キャレット位置が平坦化文字列の何文字目にあたるかを返す。</summary>
        private int CurrentCaretIndex() => IndexOfPointer(_rtb.CaretPosition);

        private int IndexOfPointer(TextPointer target)
        {
            var (_, map) = BuildFlatText();
            for (int i = 0; i < map.Count; i++)
            {
                if (map[i].CompareTo(target) >= 0)
                {
                    return i;
                }
            }
            return map.Count - 1;
        }
    }
}
