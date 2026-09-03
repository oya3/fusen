using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fusen.Services;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Fusen.Helpers
{
    /// <summary>
    /// 付箋本文（FlowDocument）の保存・復元と、貼り付け画像の扱いをまとめる。
    ///
    /// 本文の実体はあくまでテキストで、画像は Markdown の画像記法
    /// （例: <c>![](images/xxx.png)</c>）として本文中に書かれる。
    /// 編集画面に見えている画像は、その記法の行に付随する「描画専用ブロック」であり、
    /// 保存対象ではない。記法の行さえ残っていれば読み込み時に作り直せる。
    ///
    /// この作りにしているのは、TextRange.Save(DataFormats.Xaml) が
    /// BlockUIContainer / InlineUIContainer に入った UIElement を保存しないため。
    /// 以前は画像を直接本文へ埋めていたが、保存のたびに黙って捨てられており、
    /// アプリを再起動すると画像が消えていた。
    /// 記法をテキストで持つことで、保存を通り抜けるうえに、
    /// Markdown プレビューにも表示でき、ユーザーが手で編集することもできる。
    /// </summary>
    public static class FlowDocumentHelper
    {
        /// <summary>
        /// 単独行に書かれた Markdown の画像記法。行全体がこの形のときだけ編集画面に画像を描く。
        /// 文章の途中に書かれた画像は編集中はテキストのまま（プレビューでは描画される）。
        /// </summary>
        private static readonly Regex ImageMarkerPattern = new(
            @"^!\[[^\]]*\]\(\s*(?<path>[^)\s]+)\s*\)$",
            RegexOptions.Compiled);

        /// <summary>
        /// 描画専用ブロックの目印。保存される XAML には出ないため、本文の一部にはならない。
        /// </summary>
        private const string PreviewContainerTag = "fusen:image-preview";

        public static string SaveToXaml(FlowDocument doc)
        {
            try
            {
                // 画像の描画用ブロックはここで自動的に落ちる（DataFormats.Xaml は UIElement を保存しない）。
                // 本文には Markdown の画像記法だけが残り、読み込み時に SyncImagePreviews が描き直す。
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                using (var ms = new MemoryStream())
                {
                    range.Save(ms, DataFormats.Xaml);
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlowDocumentHelper] SaveToXaml error: {ex.Message}");
                return string.Empty;
            }
        }

        public static void LoadFromXaml(FlowDocument doc, string xaml)
        {
            if (string.IsNullOrWhiteSpace(xaml))
            {
                doc.Blocks.Clear();
                return;
            }

            try
            {
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(xaml)))
                {
                    range.Load(ms, DataFormats.Xaml);
                }

                // 段落マージンの正規化（保存済みXAMLに焼き付いた余白も除去）
                NormalizeParagraphSpacing(doc);

                // 前回の保存が残した空白段落を掃除してから、画像記法の行に描画を付け直す
                RemoveSerializationGhosts(doc);
                SyncImagePreviews(doc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlowDocumentHelper] LoadFromXaml error: {ex.Message}");
            }
        }

        /// <summary>
        /// Markdown ソースから本文を組み立てる。1行を1段落として扱う。
        /// contentXaml を持たない付箋を読み込むときに使う。
        /// </summary>
        public static void LoadFromText(FlowDocument doc, string text)
        {
            if (doc == null) return;

            doc.Blocks.Clear();
            if (string.IsNullOrEmpty(text)) return;

            foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
            {
                doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = ParagraphMargin });
            }

            SyncImagePreviews(doc);
        }

        /// <summary>
        /// 保存時に画像の描画ブロックが残していく、空白1文字だけの段落を取り除く。
        ///
        /// TextRange.Save は BlockUIContainer を保存しないが、その位置に
        /// <c>&lt;Paragraph&gt; &lt;/Paragraph&gt;</c> を書き出す。放っておくと
        /// 読み込みのたびに画像の下へ空行が1行ずつ増えていく。
        ///
        /// 消すのは画像記法の行の直後にある空白1文字だけの段落に限る。
        /// ユーザーが打った空行は空文字の段落として保存されるため、これには当たらない。
        /// （空白1文字だけを打った行は区別できず消えるが、失うのは空白のみ）
        /// </summary>
        private static void RemoveSerializationGhosts(FlowDocument doc)
        {
            var ghosts = new List<Block>();

            foreach (var paragraph in doc.Blocks.OfType<Paragraph>())
            {
                if (!TryGetMarkerPath(paragraph, out _)) continue;

                if (paragraph.NextBlock is Paragraph next
                    && new TextRange(next.ContentStart, next.ContentEnd).Text == " ")
                {
                    ghosts.Add(next);
                }
            }

            foreach (var ghost in ghosts)
            {
                doc.Blocks.Remove(ghost);
            }
        }

        /// <summary>
        /// 本文を Markdown ソースとして取り出す。
        ///
        /// 文書全体をまとめて TextRange.Text に渡さないのは、埋め込み要素の位置に
        /// 空白だけの行が入るため。画像記法の直後に毎回余計な空行ができてしまう。
        /// </summary>
        public static string GetPlainText(FlowDocument doc)
        {
            if (doc == null) return string.Empty;

            try
            {
                var lines = new List<string>();
                foreach (var block in doc.Blocks)
                {
                    if (IsPreviewContainer(block)) continue;
                    lines.Add(new TextRange(block.ContentStart, block.ContentEnd).Text);
                }

                return string.Join(Environment.NewLine, lines).TrimEnd('\r', '\n');
            }
            catch
            {
                return string.Empty;
            }
        }

        // ==================== 画像 ====================

        /// <summary>本文へ書き込む画像記法を組み立てる。</summary>
        public static string BuildImageMarker(string imageFileName)
            => $"![]({StorageService.Instance.BuildMarkdownImagePath(imageFileName)})";

        /// <summary>
        /// 画像を貼り付けた位置に Markdown の画像記法を挿入する。
        /// 画像そのものは差し込まない。記法を見て SyncImagePreviews が描画を付ける。
        /// </summary>
        public static void InsertImageMarker(RichTextBox richTextBox, string imageFileName)
        {
            try
            {
                var marker = new Paragraph(new Run(BuildImageMarker(imageFileName)))
                {
                    Margin = ParagraphMargin
                };

                var caret = richTextBox.CaretPosition;
                if (caret.Paragraph != null)
                {
                    richTextBox.Document.Blocks.InsertAfter(caret.Paragraph, marker);
                }
                else
                {
                    richTextBox.Document.Blocks.Add(marker);
                }

                // 記法の行に続けて入力できるよう、次の段落を用意してカーソルを移す
                var nextParagraph = new Paragraph { Margin = ParagraphMargin };
                richTextBox.Document.Blocks.InsertAfter(marker, nextParagraph);
                richTextBox.CaretPosition = nextParagraph.ContentStart;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlowDocumentHelper] InsertImageMarker error: {ex.Message}");
            }
        }

        /// <summary>
        /// Markdown の画像記法の行と、その直下に置く描画用ブロックを一致させる。
        ///
        /// 差分だけを反映し、既にある「記法＋描画」の組には触らない。
        /// そのため入力中に呼ばれてもキャレットや IME の変換を巻き込まない。
        /// </summary>
        /// <returns>文書を変更した場合は true。</returns>
        public static bool SyncImagePreviews(FlowDocument doc)
        {
            if (doc == null) return false;

            bool changed = false;

            // 対応する記法の行を失った描画を取り除く（記法が編集・削除された場合）
            var stale = doc.Blocks
                .OfType<BlockUIContainer>()
                .Where(container => IsPreviewContainer(container) && !FollowsMatchingMarker(container))
                .ToList();

            foreach (var container in stale)
            {
                doc.Blocks.Remove(container);
                changed = true;
            }

            // 描画が付いていない記法の行に画像を足す
            foreach (var marker in doc.Blocks.OfType<Paragraph>().ToList())
            {
                if (!TryGetMarkerPath(marker, out var path)) continue;

                if (marker.NextBlock is BlockUIContainer existing
                    && IsPreviewContainer(existing)
                    && string.Equals(GetPreviewPath(existing), path, StringComparison.Ordinal))
                {
                    continue;
                }

                // 読み込めない場合は記法の行だけを残す。パスの誤りが本文上で見えるようにするため。
                var created = CreatePreviewContainer(path);
                if (created == null) continue;

                doc.Blocks.InsertAfter(marker, created);
                changed = true;
            }

            return changed;
        }

        /// <summary>
        /// 指定した画像を本文から取り除く。
        ///
        /// 消すのは Markdown の記法の行のほう。描画用ブロックだけを外しても、
        /// 記法が残っている限り次の同期で作り直されてしまう。
        /// </summary>
        public static bool RemoveImage(Image image)
        {
            if (image == null) return false;
            if (LogicalTreeHelper.GetParent(image) is not BlockUIContainer container) return false;
            if (container.Parent is not FlowDocument doc) return false;

            var marker = container.PreviousBlock;

            doc.Blocks.Remove(container);

            if (marker is Paragraph paragraph && TryGetMarkerPath(paragraph, out _))
            {
                doc.Blocks.Remove(paragraph);
            }

            return true;
        }

        /// <summary>段落の全文が画像記法なら、そのパスを返す。</summary>
        private static bool TryGetMarkerPath(Paragraph paragraph, out string path)
        {
            path = string.Empty;

            var text = new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim();
            if (text.Length < 5 || text[0] != '!') return false;

            var match = ImageMarkerPattern.Match(text);
            if (!match.Success) return false;

            path = match.Groups["path"].Value;
            return true;
        }

        private static bool IsPreviewContainer(Block? block)
            => block is BlockUIContainer container
               && container.Tag as string == PreviewContainerTag;

        private static string? GetPreviewPath(BlockUIContainer container)
            => (container.Child as Image)?.Tag as string;

        private static bool FollowsMatchingMarker(BlockUIContainer container)
            => container.PreviousBlock is Paragraph paragraph
               && TryGetMarkerPath(paragraph, out var path)
               && string.Equals(GetPreviewPath(container), path, StringComparison.Ordinal);

        /// <summary>画像記法のパスからファイルを読み、描画用ブロックを作る。読めなければ null。</summary>
        private static BlockUIContainer? CreatePreviewContainer(string markdownPath)
        {
            var fullPath = StorageService.Instance.ResolveImagePath(markdownPath);
            if (string.IsNullOrEmpty(fullPath)) return null;

            try
            {
                var bitmap = LoadBitmap(fullPath);

                var image = new Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    // 同期のたびに読み直さないよう、記法に書かれたパスをそのまま持たせて突き合わせる
                    Tag = markdownPath,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                return new BlockUIContainer(image)
                {
                    Tag = PreviewContainerTag,
                    Margin = ImageMargin
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlowDocumentHelper] CreatePreviewContainer error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 画像ファイルを読み込む。OnLoad で読み切るのは、ファイルを掴んだままにしないため。
        /// 掴んだままだと、同じ画像を消したり差し替えたりできなくなる。
        /// </summary>
        public static BitmapImage LoadBitmap(string fullPath)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// 本文中の画像を、付箋の幅に収まる大きさへ調整する。
        ///
        /// 固定の最大幅を持たせると、付箋を広げても画像が小さいままになり、
        /// 狭めると本文からはみ出す。リサイズのたびに呼び直して追従させる。
        ///
        /// 元のサイズより大きくは引き伸ばさない（拡大するとぼやけるため）。
        /// 高さの上限は設けず、縦横比は幅に従って決まるようにする。
        /// </summary>
        public static void ApplyResponsiveImageSize(FlowDocument doc, double availableWidth)
        {
            if (doc == null || availableWidth <= 0 || double.IsNaN(availableWidth)) return;

            foreach (var image in EnumerateImages(doc.Blocks))
            {
                double naturalWidth = (image.Source as BitmapSource)?.Width ?? 0;

                image.Stretch = Stretch.Uniform;
                image.MaxWidth = naturalWidth > 0
                    ? Math.Min(naturalWidth, availableWidth)
                    : availableWidth;
                image.MaxHeight = double.PositiveInfinity;
            }
        }

        /// <summary>本文中の Image を、入れ子のブロックも含めて列挙する。</summary>
        private static IEnumerable<Image> EnumerateImages(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case BlockUIContainer container when container.Child is Image image:
                        yield return image;
                        break;

                    case Paragraph paragraph:
                        foreach (var nested in EnumerateImages(paragraph.Inlines))
                        {
                            yield return nested;
                        }
                        break;

                    case Section section:
                        foreach (var nested in EnumerateImages(section.Blocks))
                        {
                            yield return nested;
                        }
                        break;

                    case List list:
                        foreach (var listItem in list.ListItems)
                        {
                            foreach (var nested in EnumerateImages(listItem.Blocks))
                            {
                                yield return nested;
                            }
                        }
                        break;

                    case Table table:
                        foreach (var group in table.RowGroups)
                        {
                            foreach (var row in group.Rows)
                            {
                                foreach (var cell in row.Cells)
                                {
                                    foreach (var nested in EnumerateImages(cell.Blocks))
                                    {
                                        yield return nested;
                                    }
                                }
                            }
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// インライン中の Image を列挙する。Markdown プレビューでは画像がリンクや強調の
        /// 内側（Span）に入ることがあるため、入れ子をたどる。
        /// </summary>
        private static IEnumerable<Image> EnumerateImages(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case InlineUIContainer container when container.Child is Image image:
                        yield return image;
                        break;

                    case Span span:
                        foreach (var nested in EnumerateImages(span.Inlines))
                        {
                            yield return nested;
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// 段落（Paragraph）の既定マージンを除去し、行間の開きすぎを解消する。
        /// FlowDocument の既定では段落ごとに上下余白が入り、改行のたびに1行分の空きが生じるため、
        /// 読み込み時・編集時に正規化する。
        /// </summary>
        public static void NormalizeParagraphSpacing(FlowDocument doc)
        {
            if (doc == null) return;

            doc.PagePadding = new Thickness(0);
            NormalizeBlocks(doc.Blocks);
        }

        private static void NormalizeBlocks(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph paragraph)
                {
                    if (paragraph.Margin != ParagraphMargin)
                    {
                        paragraph.Margin = ParagraphMargin;
                    }
                }
                else if (block is List list)
                {
                    if (block.Margin != ListMargin)
                    {
                        block.Margin = ListMargin;
                    }
                    foreach (var listItem in list.ListItems)
                    {
                        NormalizeBlocks(listItem.Blocks);
                    }
                }
                else if (block is Section section)
                {
                    section.Margin = ParagraphMargin;
                    NormalizeBlocks(section.Blocks);
                }
                else if (block is BlockUIContainer container)
                {
                    if (container.Margin != ImageMargin)
                    {
                        container.Margin = ImageMargin;
                    }
                }
            }
        }

        private static readonly Thickness ParagraphMargin = new Thickness(0);
        private static readonly Thickness ListMargin = new Thickness(0, 0, 0, 0);
        private static readonly Thickness ImageMargin = new Thickness(0, 2, 0, 2);
    }
}
