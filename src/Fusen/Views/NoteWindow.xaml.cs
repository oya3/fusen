using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Fusen.Helpers;
using Fusen.Models;
using Fusen.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using RichTextBox = System.Windows.Controls.RichTextBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;

namespace Fusen.Views
{
    public partial class NoteWindow : Window
    {
        public NoteItem Note { get; }
        private bool _isInitializing = true;
        private double _expandedHeight = 300;

        // Windows API: ウィンドウドラッグリサイズ用定数
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_SIZE = 0xF000;
        private const int WMSZ_BOTTOMRIGHT = 8;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        public NoteWindow(NoteItem note)
        {
            InitializeComponent();
            Note = note;
            DataContext = Note;

            // 初期位置とサイズの適用
            Left = Note.X;
            Top = Note.Y;
            Width = Math.Max(200, Note.Width);
            _expandedHeight = Math.Max(120, Note.Height);

            Topmost = Note.IsPinned;
            UpdatePinState();
            ApplyColorTheme(Note.ColorTheme);

            // クリップボード貼り付けハンドラの設定
            DataObject.AddPastingHandler(NoteRichTextBox, OnPasteCommand);
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // XAMLからFlowDocumentを復元
            if (!string.IsNullOrWhiteSpace(Note.ContentXaml))
            {
                FlowDocumentHelper.LoadFromXaml(NoteRichTextBox.Document, Note.ContentXaml);
            }
            else if (!string.IsNullOrWhiteSpace(Note.PlainText))
            {
                NoteRichTextBox.Document.Blocks.Clear();
                var p = new Paragraph(new Run(Note.PlainText));
                NoteRichTextBox.Document.Blocks.Add(p);
            }

            UpdateTitleDisplay();

            // 折りたたみ状態の適用
            if (Note.IsFolded)
            {
                ApplyFoldState(true);
            }
            else
            {
                Height = _expandedHeight;
                ApplyFoldState(false);
            }

            _isInitializing = false;
        }

        public void ApplyColorTheme(string themeName)
        {
            Note.ColorTheme = themeName;
            var theme = NoteColorTheme.GetTheme(themeName);

            MainBorder.Background = theme.BackgroundBrush;
            MainBorder.BorderBrush = theme.BorderBrush;
            HeaderBorder.Background = theme.HeaderBrush;
            TitleTextBlock.Foreground = theme.HeaderForegroundBrush;
            NoteRichTextBox.Foreground = theme.ForegroundBrush;
            NoteRichTextBox.CaretBrush = theme.ForegroundBrush;

            if (theme.Name == "Dark")
            {
                FoldIcon.Foreground = theme.HeaderForegroundBrush;
            }
        }

        public void ToggleFold(bool? forceFold = null)
        {
            bool nextFoldState = forceFold ?? !Note.IsFolded;
            Note.IsFolded = nextFoldState;

            ApplyFoldState(nextFoldState);
            NoteManager.Instance.RequestAutoSave();
        }

        private void ApplyFoldState(bool isFolded)
        {
            if (isFolded)
            {
                if (ContentArea.Visibility == Visibility.Visible)
                {
                    _expandedHeight = Height;
                    Note.Height = _expandedHeight;
                }

                ContentArea.Visibility = Visibility.Collapsed;
                // ヘッダー + マージン(8+8) = 52
                MinHeight = 52;
                MaxHeight = 52;
                Height = 52;
                FoldIcon.Text = "▼";
                BtnFold.ToolTip = "展開する";
                WindowResizeGrip.Visibility = Visibility.Collapsed;
                MainBorder.CornerRadius = new CornerRadius(8);
                HeaderBorder.CornerRadius = new CornerRadius(7);
            }
            else
            {
                ContentArea.Visibility = Visibility.Visible;
                MinHeight = 120;
                MaxHeight = double.PositiveInfinity;
                Height = Math.Max(120, _expandedHeight);
                FoldIcon.Text = "▲";
                BtnFold.ToolTip = "折りたたむ";
                WindowResizeGrip.Visibility = Visibility.Visible;
                MainBorder.CornerRadius = new CornerRadius(8);
                HeaderBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            }
        }

        public void SyncModelFromWindow()
        {
            if (!_isInitializing)
            {
                Note.X = Left;
                Note.Y = Top;
                Note.Width = Width;
                if (!Note.IsFolded)
                {
                    Note.Height = Height;
                }
                else
                {
                    Note.Height = _expandedHeight;
                }
            }
        }

        private void UpdateTitleDisplay()
        {
            TitleTextBlock.Text = string.IsNullOrWhiteSpace(Note.Title) ? "新規メモ" : Note.Title;
        }

        private void UpdatePinState()
        {
            PinIcon.Opacity = Note.IsPinned ? 1.0 : 0.45;
            BtnPin.ToolTip = Note.IsPinned ? "ピン留め解除 (通常表示)" : "最前面にピン留め";
        }

        // ==================== イベントハンドラ ====================

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // ヘッダーのダブルクリックで折りたたみ/展開切替
                ToggleFold();
                e.Handled = true;
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SyncModelFromWindow();
                NoteManager.Instance.RequestAutoSave();
            }
        }

        private void BtnNew_Click(object sender, RoutedEventArgs e)
        {
            NoteManager.Instance.CreateNewNote(Left + 30, Top + 30);
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            Note.IsPinned = !Note.IsPinned;
            Topmost = Note.IsPinned;
            UpdatePinState();
            NoteManager.Instance.RequestAutoSave();
        }

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            ColorPickerPopup.PlacementTarget = (UIElement)sender;
            ColorPickerPopup.IsOpen = true;
        }

        private void ColorOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string colorName)
            {
                ApplyColorTheme(colorName);
                ColorPickerPopup.IsOpen = false;
                NoteManager.Instance.RequestAutoSave();
            }
        }

        private void BtnList_Click(object sender, RoutedEventArgs e)
        {
            NoteManager.Instance.ShowNoteList();
        }

        private void BtnFold_Click(object sender, RoutedEventArgs e)
        {
            ToggleFold();
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            NoteManager.Instance.DeleteNote(Note);
        }

        private void NoteRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;

            Note.PlainText = FlowDocumentHelper.GetPlainText(NoteRichTextBox.Document);
            Note.ContentXaml = FlowDocumentHelper.SaveToXaml(NoteRichTextBox.Document);
            Note.UpdatedAt = DateTime.Now;

            UpdateTitleDisplay();
            NoteManager.Instance.RequestAutoSave();
        }

        private void NoteRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // ショートカットキー操作
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (e.Key == Key.N)
                {
                    BtnNew_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.L)
                {
                    BtnList_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
                else if (e.Key == Key.W)
                {
                    BtnDelete_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                }
            }
        }

        private void OnPasteCommand(object sender, DataObjectPastingEventArgs e)
        {
            try
            {
                // クリップボードに画像があるかチェック
                if (Clipboard.ContainsImage())
                {
                    var imageSource = Clipboard.GetImage();
                    if (imageSource != null)
                    {
                        var fileName = StorageService.Instance.SaveImage(imageSource, Note.Id);
                        if (!string.IsNullOrEmpty(fileName))
                        {
                            FlowDocumentHelper.InsertImage(NoteRichTextBox, imageSource, fileName);
                            e.CancelCommand();
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteWindow] OnPasteCommand error: {ex.Message}");
            }
        }

        private void ResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !Note.IsFolded)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                SendMessage(hwnd, WM_SYSCOMMAND, (IntPtr)(SC_SIZE + WMSZ_BOTTOMRIGHT), IntPtr.Zero);
                SyncModelFromWindow();
                NoteManager.Instance.RequestAutoSave();
                e.Handled = true;
            }
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (!_isInitializing)
            {
                SyncModelFromWindow();
                NoteManager.Instance.RequestAutoSave();
            }
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isInitializing)
            {
                SyncModelFromWindow();
                NoteManager.Instance.RequestAutoSave();
            }
        }
    }
}
