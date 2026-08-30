using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfBlock = System.Windows.Documents.Block;
using MdTable = Markdig.Extensions.Tables.Table;
using MdTableRow = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;

namespace Fusen.Helpers
{
    /// <summary>
    /// Markdown を WPF の FlowDocument へ描画する。
    ///
    /// 描画結果は読み取り専用のプレビュー用であり、編集中の本文（NoteWindow の RichTextBox）とは
    /// 別の FlowDocument を作る。編集側の文書には一切触れないため、プレビューを開いても
    /// contentXaml が整形済みの内容で上書きされることはない。
    ///
    /// 画像はプレビューに表示しない。Markdown の画像記法は代替テキストとして描画する。
    /// （貼り付け画像は FlowDocument 側に保持されており plainText には含まれないため）
    /// </summary>
    public static class MarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseAutoLinks()
            .Build();

        private static readonly FontFamily MonospaceFont =
            new("Consolas, Cascadia Mono, MS Gothic, monospace");

        public static FlowDocument Render(string? markdown, double baseFontSize, Brush foreground)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontSize = baseFontSize,
                Foreground = foreground,
                // 行間は編集画面と揃える
                LineHeight = double.NaN,
            };

            if (string.IsNullOrWhiteSpace(markdown))
            {
                return doc;
            }

            try
            {
                var parsed = Markdown.Parse(markdown, Pipeline);
                foreach (var block in ConvertBlocks(parsed, baseFontSize, foreground))
                {
                    doc.Blocks.Add(block);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownRenderer] Render error: {ex.Message}");
                doc.Blocks.Clear();
                doc.Blocks.Add(new Paragraph(new Run(markdown)));
            }

            return doc;
        }

        // ==================== ブロック ====================

        private static IEnumerable<WpfBlock> ConvertBlocks(IEnumerable<MarkdownObject> blocks, double baseSize, Brush fg)
        {
            foreach (var block in blocks)
            {
                var converted = ConvertBlock(block, baseSize, fg);
                if (converted != null)
                {
                    yield return converted;
                }
            }
        }

        private static WpfBlock? ConvertBlock(MarkdownObject block, double baseSize, Brush fg)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    return ConvertHeading(heading, baseSize, fg);

                case ParagraphBlock paragraph:
                    {
                        var p = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
                        AppendInlines(p.Inlines, paragraph.Inline, baseSize, fg);
                        return p;
                    }

                case ListBlock list:
                    return ConvertList(list, baseSize, fg);

                case QuoteBlock quote:
                    return ConvertQuote(quote, baseSize, fg);

                case FencedCodeBlock fenced:
                    return ConvertCode(GetLines(fenced), baseSize, fg);

                case CodeBlock code:
                    return ConvertCode(GetLines(code), baseSize, fg);

                case ThematicBreakBlock:
                    return new Paragraph
                    {
                        Margin = new Thickness(0, 6, 0, 8),
                        BorderBrush = MakeFaded(fg, 0.35),
                        BorderThickness = new Thickness(0, 0, 0, 1),
                    };

                case MdTable table:
                    return ConvertTable(table, baseSize, fg);

                case HtmlBlock html:
                    // HTML はそのまま文字として見せる（付箋で HTML を描画する必要はないため）
                    return ConvertCode(GetLines(html), baseSize, fg);

                case ContainerBlock container:
                    {
                        // 未知のコンテナは中身だけを取り出して描画する
                        var section = new Section { Margin = new Thickness(0) };
                        foreach (var child in ConvertBlocks(container, baseSize, fg))
                        {
                            section.Blocks.Add(child);
                        }
                        return section.Blocks.Count > 0 ? section : null;
                    }

                case LeafBlock leaf:
                    {
                        // 未知のブロックは元のテキストを落とさずそのまま出す
                        string text = GetLines(leaf);
                        return string.IsNullOrEmpty(text)
                            ? null
                            : new Paragraph(new Run(text)) { Margin = new Thickness(0, 0, 0, 6) };
                    }

                default:
                    return null;
            }
        }

        private static WpfBlock ConvertHeading(HeadingBlock heading, double baseSize, Brush fg)
        {
            // h1 を 1.6 倍とし、h4 以降は本文と同じ大きさにする
            double scale = heading.Level switch
            {
                1 => 1.60,
                2 => 1.40,
                3 => 1.20,
                4 => 1.10,
                _ => 1.00,
            };

            var paragraph = new Paragraph
            {
                FontSize = baseSize * scale,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, heading.Level <= 2 ? 10 : 8, 0, 4),
            };

            AppendInlines(paragraph.Inlines, heading.Inline, baseSize * scale, fg);

            // h1 / h2 は下線を引いて見出しであることを分かりやすくする
            if (heading.Level <= 2)
            {
                paragraph.BorderBrush = MakeFaded(fg, 0.3);
                paragraph.BorderThickness = new Thickness(0, 0, 0, 1);
                paragraph.Padding = new Thickness(0, 0, 0, 3);
            }

            return paragraph;
        }

        private static WpfBlock ConvertList(ListBlock list, double baseSize, Brush fg)
        {
            var wpfList = new List
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(0),
                MarkerOffset = 4,
                MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            };

            if (list.IsOrdered && int.TryParse(list.OrderedStart, out int start) && start > 0)
            {
                wpfList.StartIndex = start;
            }

            foreach (var item in list)
            {
                if (item is not ListItemBlock itemBlock) continue;

                var listItem = new ListItem();
                foreach (var child in ConvertBlocks(itemBlock, baseSize, fg))
                {
                    listItem.Blocks.Add(child);
                }

                // 箇条書きの各項目は詰めて表示する
                foreach (var b in listItem.Blocks)
                {
                    b.Margin = new Thickness(0, 0, 0, 2);
                }

                if (listItem.Blocks.Count > 0)
                {
                    wpfList.ListItems.Add(listItem);
                }
            }

            return wpfList;
        }

        private static WpfBlock ConvertQuote(QuoteBlock quote, double baseSize, Brush fg)
        {
            var section = new Section
            {
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(10, 2, 0, 2),
                BorderBrush = MakeFaded(fg, 0.35),
                BorderThickness = new Thickness(3, 0, 0, 0),
                Foreground = MakeFaded(fg, 0.75),
            };

            foreach (var child in ConvertBlocks(quote, baseSize, fg))
            {
                section.Blocks.Add(child);
            }

            return section;
        }

        private static WpfBlock ConvertCode(string text, double baseSize, Brush fg)
        {
            var paragraph = new Paragraph(new Run(text.TrimEnd('\r', '\n')))
            {
                FontFamily = MonospaceFont,
                FontSize = baseSize * 0.92,
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(8, 5, 8, 5),
                Background = MakeFaded(fg, 0.07),
            };
            return paragraph;
        }

        private static WpfBlock ConvertTable(MdTable table, double baseSize, Brush fg)
        {
            var wpfTable = new Table
            {
                Margin = new Thickness(0, 0, 0, 8),
                CellSpacing = 0,
            };

            int columnCount = table
                .OfType<MdTableRow>()
                .Select(r => r.Count)
                .DefaultIfEmpty(0)
                .Max();

            for (int i = 0; i < columnCount; i++)
            {
                wpfTable.Columns.Add(new TableColumn());
            }

            var group = new TableRowGroup();
            wpfTable.RowGroups.Add(group);

            var borderBrush = MakeFaded(fg, 0.3);

            foreach (var rowObj in table)
            {
                if (rowObj is not MdTableRow row) continue;

                var wpfRow = new TableRow();
                if (row.IsHeader)
                {
                    wpfRow.FontWeight = FontWeights.Bold;
                    wpfRow.Background = MakeFaded(fg, 0.07);
                }

                foreach (var cellObj in row)
                {
                    if (cellObj is not MdTableCell cell) continue;

                    var wpfCell = new TableCell
                    {
                        BorderBrush = borderBrush,
                        BorderThickness = new Thickness(0, 0, 1, 1),
                        Padding = new Thickness(6, 3, 6, 3),
                        ColumnSpan = Math.Max(1, cell.ColumnSpan),
                        RowSpan = Math.Max(1, cell.RowSpan),
                    };

                    foreach (var child in ConvertBlocks(cell, baseSize, fg))
                    {
                        child.Margin = new Thickness(0);
                        wpfCell.Blocks.Add(child);
                    }

                    wpfRow.Cells.Add(wpfCell);
                }

                if (wpfRow.Cells.Count > 0)
                {
                    group.Rows.Add(wpfRow);
                }
            }

            return wpfTable;
        }

        // ==================== インライン ====================

        private static void AppendInlines(InlineCollection target, ContainerInline? container, double baseSize, Brush fg)
        {
            if (container == null) return;

            foreach (var inline in container)
            {
                var converted = ConvertInline(inline, baseSize, fg);
                if (converted != null)
                {
                    target.Add(converted);
                }
            }
        }

        private static System.Windows.Documents.Inline? ConvertInline(Markdig.Syntax.Inlines.Inline inline, double baseSize, Brush fg)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    return new Run(literal.Content.ToString());

                case EmphasisInline emphasis:
                    {
                        Span span = emphasis.DelimiterChar switch
                        {
                            '~' => new Span { TextDecorations = TextDecorations.Strikethrough },
                            _ => emphasis.DelimiterCount >= 2 ? new Bold() : new Italic(),
                        };
                        AppendInlines(span.Inlines, emphasis, baseSize, fg);
                        return span;
                    }

                case CodeInline code:
                    return new Run(code.Content)
                    {
                        FontFamily = MonospaceFont,
                        FontSize = baseSize * 0.92,
                        Background = MakeFaded(fg, 0.09),
                    };

                case LinkInline link:
                    return ConvertLink(link, baseSize, fg);

                case AutolinkInline autolink:
                    return MakeHyperlink(autolink.Url, new Run(autolink.Url));

                case LineBreakInline lineBreak:
                    // ソフト改行は原文の折り返しなので空白として扱う
                    return lineBreak.IsHard ? new LineBreak() : new Run(" ");

                case HtmlInline html:
                    return new Run(html.Tag);

                case ContainerInline containerInline:
                    {
                        var span = new Span();
                        AppendInlines(span.Inlines, containerInline, baseSize, fg);
                        return span;
                    }

                default:
                    {
                        // 未知のインラインは元の文字列を落とさずそのまま出す
                        string text = inline.ToString() ?? string.Empty;
                        return string.IsNullOrEmpty(text) ? null : new Run(text);
                    }
            }
        }

        private static System.Windows.Documents.Inline ConvertLink(LinkInline link, double baseSize, Brush fg)
        {
            // 画像はプレビューに表示しない。代替テキストが分かる形で残す
            if (link.IsImage)
            {
                string alt = GetInlineText(link);
                string label = string.IsNullOrWhiteSpace(alt) ? link.Url ?? string.Empty : alt;
                return new Run($"[画像: {label}]")
                {
                    FontStyle = FontStyles.Italic,
                    Foreground = MakeFaded(fg, 0.6),
                };
            }

            var content = new Span();
            AppendInlines(content.Inlines, link, baseSize, fg);

            if (content.Inlines.Count == 0)
            {
                content.Inlines.Add(new Run(link.Url ?? string.Empty));
            }

            return MakeHyperlink(link.Url, content);
        }

        private static System.Windows.Documents.Inline MakeHyperlink(string? url, System.Windows.Documents.Inline content)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return content;
            }

            try
            {
                var hyperlink = new Hyperlink(content)
                {
                    NavigateUri = new Uri(url, UriKind.RelativeOrAbsolute),
                    ToolTip = url,
                };
                return hyperlink;
            }
            catch (Exception ex)
            {
                // 不正な URI はリンクにせず、そのまま文字として見せる
                Debug.WriteLine($"[MarkdownRenderer] Invalid link '{url}': {ex.Message}");
                return content;
            }
        }

        // ==================== 補助 ====================

        private static string GetLines(LeafBlock block) => block.Lines.ToString() ?? string.Empty;

        private static string GetInlineText(ContainerInline container)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        sb.Append(literal.Content.ToString());
                        break;
                    case ContainerInline nested:
                        sb.Append(GetInlineText(nested));
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>本文色を薄めた色を作る。テーマ（明色・ダーク）どちらでも馴染ませるため。</summary>
        private static Brush MakeFaded(Brush source, double opacity)
        {
            if (source is SolidColorBrush solid)
            {
                var color = solid.Color;
                color.A = (byte)Math.Clamp(color.A * opacity, 0, 255);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            var fallback = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), 128, 128, 128));
            fallback.Freeze();
            return fallback;
        }
    }
}
