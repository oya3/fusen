using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fusen.Services;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Fusen.Helpers
{
    public static class FlowDocumentHelper
    {
        public static string SaveToXaml(FlowDocument doc)
        {
            try
            {
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

                // ポータブル復元処理: 画像コンテナの Source を検証・再バインド
                ResolveImagesInDocument(doc);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlowDocumentHelper] LoadFromXaml error: {ex.Message}");
            }
        }

        private static void ResolveImagesInDocument(FlowDocument doc)
        {
            foreach (var block in doc.Blocks)
            {
                if (block is BlockUIContainer container && container.Child is Image img)
                {
                    AttachImageContextMenu(img, container);
                    
                    // Tag にファイル名がある場合は最新パスから読み直す
                    if (img.Tag is string fileName && !string.IsNullOrEmpty(fileName))
                    {
                        var fullPath = StorageService.Instance.GetImageFullPath(fileName);
                        if (File.Exists(fullPath) && (img.Source == null || img.Source is not BitmapSource))
                        {
                            try
                            {
                                var bitmap = new BitmapImage();
                                bitmap.BeginInit();
                                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                                bitmap.EndInit();
                                img.Source = bitmap;
                            }
                            catch { }
                        }
                    }
                }
            }
        }

        public static string GetPlainText(FlowDocument doc)
        {
            try
            {
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                return range.Text.TrimEnd('\r', '\n');
            }
            catch
            {
                return string.Empty;
            }
        }

        public static void InsertImage(RichTextBox richTextBox, BitmapSource bitmapSource, string imageFileName)
        {
            try
            {
                var image = new Image
                {
                    Source = bitmapSource,
                    Stretch = Stretch.Uniform,
                    MaxWidth = 240,
                    MaxHeight = 300,
                    Margin = new Thickness(0, 4, 0, 4),
                    Tag = imageFileName,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var container = new BlockUIContainer(image)
                {
                    Margin = new Thickness(0, 4, 0, 4)
                };

                AttachImageContextMenu(image, container);

                var caret = richTextBox.CaretPosition;
                if (caret.Paragraph != null)
                {
                    richTextBox.Document.Blocks.InsertAfter(caret.Paragraph, container);
                }
                else
                {
                    richTextBox.Document.Blocks.Add(container);
                }

                // 次の段落を用意してカーソルを移動
                var nextParagraph = new Paragraph();
                richTextBox.Document.Blocks.InsertAfter(container, nextParagraph);
                richTextBox.CaretPosition = nextParagraph.ContentStart;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FlowDocumentHelper] InsertImage error: {ex.Message}");
            }
        }

        private static void AttachImageContextMenu(Image image, BlockUIContainer container)
        {
            var menu = new ContextMenu();
            
            var copyItem = new MenuItem { Header = "📋 画像をコピー" };
            copyItem.Click += (s, e) =>
            {
                if (image.Source is BitmapSource bs)
                {
                    Clipboard.SetImage(bs);
                }
            };

            var deleteItem = new MenuItem { Header = "🗑️ 画像を削除" };
            deleteItem.Click += (s, e) =>
            {
                if (container.Parent is FlowDocument doc)
                {
                    doc.Blocks.Remove(container);
                }
            };

            menu.Items.Add(copyItem);
            menu.Items.Add(deleteItem);
            image.ContextMenu = menu;
        }
    }
}
