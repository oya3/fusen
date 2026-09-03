using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fusen.Helpers;
using Fusen.Models;
using Fusen.Services;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using RichTextBox = System.Windows.Controls.RichTextBox;

namespace Fusen.Views
{
    public partial class NoteWindow : Window
    {
        public NoteItem Note { get; }
        private bool _isInitializing = true;
        private bool _updatingOpacitySlider = false;
        private bool _updatingFontSizeSlider = false;
        private double _expandedHeight = 300;

        /// <summary>fusen.ini で bash モードが有効なときだけ生成される。無効時は null。</summary>
        private readonly BashKeyHandler? _bashKeys;

        /// <summary>
        /// 画像記法と、その下に表示する画像の同期を、入力が止まってから走らせるためのタイマー。
        ///
        /// TextChanged で直接同期すると、日本語入力の変換中に本文のブロックを差し替えることになり、
        /// キャレットが飛んだり変換が中断したりする。入力が落ち着くまで待ってからまとめて反映する。
        /// </summary>
        private readonly System.Windows.Threading.DispatcherTimer _imageSyncTimer;

        /// <summary>同期が自分で起こした TextChanged を無視するためのフラグ。</summary>
        private bool _syncingImages;

        /// <summary>画像記法の同期を待つ時間。長すぎると反応が鈍く、短すぎると変換中に割り込む。</summary>
        private static readonly TimeSpan ImageSyncDelay = TimeSpan.FromMilliseconds(600);

        /// <summary>
        /// 折りたたみ時のウィンドウ高。
        /// NoteWindow.xaml の HeaderBorder の Height(28) と、外周 Grid のマージン(8+8) の合計。
        /// ヘッダーの高さを変えたらここも合わせること。
        /// </summary>
        private const double FoldedWindowHeight = 44;

        // Windows API: WM_NCHITTEST 用定数
        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

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

            // 不透明度の適用
            double targetOpacity = Note.Opacity > 0 ? Note.Opacity : AppConfig.Instance.DefaultOpacity;
            Opacity = Math.Clamp(targetOpacity, 0.1, 1.0);

            Topmost = Note.IsPinned;
            UpdatePinState();
            ApplyColorTheme(Note.ColorTheme);

            // フォント設定の適用（本文はまだ読み込まれていないため、コントロール側のみ）
            NoteRichTextBox.FontSize = ResolveFontSize();
            if (!string.IsNullOrWhiteSpace(AppConfig.Instance.FontFamily))
            {
                try
                {
                    NoteRichTextBox.FontFamily = new System.Windows.Media.FontFamily(AppConfig.Instance.FontFamily);
                }
                catch { }
            }

            // bash (Readline) キーバインドは設定で有効にした場合のみ接続する
            if (AppConfig.Instance.BashModeEnabled)
            {
                _bashKeys = new BashKeyHandler(NoteRichTextBox);
                _bashKeys.SearchStateChanged += UpdateSearchBar;
            }

            // クリップボード貼り付けハンドラの設定
            DataObject.AddPastingHandler(NoteRichTextBox, OnPasteCommand);

            _imageSyncTimer = new System.Windows.Threading.DispatcherTimer { Interval = ImageSyncDelay };
            _imageSyncTimer.Tick += (_, _) => SyncImagePreviews();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(hwnd);
            source?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_NCHITTEST)
            {
                // マウス座標を取得してウィンドウ内ローカル座標に変換
                int screenX = unchecked((short)(long)lParam);
                int screenY = unchecked((short)((long)lParam >> 16));

                var pt = PointFromScreen(new Point(screenX, screenY));

                const double borderMargin = 8.0;      // 外枠ドロップシャドウのマージン
                const double resizeThickness = 8.0;    // リサイズ判定の幅

                double w = ActualWidth;
                double h = ActualHeight;

                // 付箋の外枠ボーダー位置に対する判定
                bool isLeft = pt.X >= 0 && pt.X <= (borderMargin + resizeThickness);
                bool isRight = pt.X >= (w - borderMargin - resizeThickness) && pt.X <= w;
                bool isTop = pt.Y >= 0 && pt.Y <= (borderMargin + resizeThickness);
                bool isBottom = pt.Y >= (h - borderMargin - resizeThickness) && pt.Y <= h;

                if (Note.IsFolded)
                {
                    // 折りたたみ時は左右のリサイズのみ許可
                    if (isLeft)
                    {
                        handled = true;
                        return (IntPtr)HTLEFT;
                    }
                    if (isRight)
                    {
                        handled = true;
                        return (IntPtr)HTRIGHT;
                    }
                }
                else
                {
                    // 展開時は8方向すべてのリサイズに対応
                    if (isTop && isLeft)
                    {
                        handled = true;
                        return (IntPtr)HTTOPLEFT;
                    }
                    if (isTop && isRight)
                    {
                        handled = true;
                        return (IntPtr)HTTOPRIGHT;
                    }
                    if (isBottom && isLeft)
                    {
                        handled = true;
                        return (IntPtr)HTBOTTOMLEFT;
                    }
                    if (isBottom && isRight)
                    {
                        handled = true;
                        return (IntPtr)HTBOTTOMRIGHT;
                    }

                    if (isLeft)
                    {
                        handled = true;
                        return (IntPtr)HTLEFT;
                    }
                    if (isRight)
                    {
                        handled = true;
                        return (IntPtr)HTRIGHT;
                    }
                    if (isTop)
                    {
                        handled = true;
                        return (IntPtr)HTTOP;
                    }
                    if (isBottom)
                    {
                        handled = true;
                        return (IntPtr)HTBOTTOM;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // カラーパレットの動的生成
            PopulateColorPalette();

            // XAMLからFlowDocumentを復元
            if (!string.IsNullOrWhiteSpace(Note.ContentXaml))
            {
                FlowDocumentHelper.LoadFromXaml(NoteRichTextBox.Document, Note.ContentXaml);
            }
            else if (!string.IsNullOrWhiteSpace(Note.PlainText))
            {
                FlowDocumentHelper.LoadFromText(NoteRichTextBox.Document, Note.PlainText);
            }

            // 段落マージンを除去して行間を詰める
            FlowDocumentHelper.NormalizeParagraphSpacing(NoteRichTextBox.Document);

            // 本文を読み込んだ後に文字サイズを適用する（読み込んだ XAML 側の指定を上書きするため）
            ApplyFontSize(ResolveFontSize());

            // 本文が確定してからでないと Markdown を描画できないため、ここで復元する
            ApplyPreviewState(Note.IsPreview);

            // 復元した画像を現在の付箋幅に合わせる。
            // レイアウトが済むまで ViewportWidth が確定しないため、描画完了後に実行する。
            Dispatcher.BeginInvoke(new Action(UpdateImageSizes), System.Windows.Threading.DispatcherPriority.Loaded);

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

        private void PopulateColorPalette()
        {
            ColorPaletteStackPanel.Children.Clear();
            var style = (Style)FindResource("ColorCircleButtonStyle");

            foreach (var theme in NoteColorTheme.GetAllThemes())
            {
                var btn = new Button
                {
                    Style = style,
                    Background = theme.BackgroundBrush,
                    BorderBrush = theme.BorderBrush,
                    ToolTip = theme.DisplayName,
                    Tag = theme.Name
                };
                btn.Click += ColorOption_Click;
                ColorPaletteStackPanel.Children.Add(btn);
            }
        }

        /// <summary>
        /// この付箋に適用する文字サイズを決める。付箋ごとの値が未設定なら fusen.ini の既定値を使う。
        /// </summary>
        private static double ResolveFontSizeFor(NoteItem note)
        {
            double size = note.FontSize > 0 ? note.FontSize : AppConfig.Instance.FontSize;
            return size > 0 ? Math.Clamp(size, 9.0, 36.0) : 13.5;
        }

        private double ResolveFontSize() => ResolveFontSizeFor(Note);

        /// <summary>
        /// 文字サイズを本文へ適用する。
        ///
        /// RichTextBox の本文は XAML として保存され、その際に FontSize が各要素へ焼き込まれる。
        /// そのためコントロールの FontSize を変えるだけでは既存のテキストに反映されない。
        /// 文書全体に対して明示的に適用し、保存時の XAML にも新しい値が載るようにする。
        /// </summary>
        private void ApplyFontSize(double size)
        {
            NoteRichTextBox.FontSize = size;

            var doc = NoteRichTextBox.Document;
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.ApplyPropertyValue(TextElement.FontSizeProperty, size);
        }

        /// <summary>
        /// Markdown プレビューの表示・非表示を切り替える。
        ///
        /// プレビューは編集用の RichTextBox とは別の FlowDocument を作って表示する。
        /// 同じ文書に描画すると TextChanged が走り、整形結果で contentXaml が上書きされて
        /// 元の Markdown ソースが失われるため、編集側の文書には一切触れない。
        /// </summary>
        public void ApplyPreviewState(bool isPreview)
        {
            if (isPreview)
            {
                var theme = NoteColorTheme.GetTheme(Note.ColorTheme);
                PreviewViewer.Document = MarkdownRenderer.Render(
                    Note.PlainText, ResolveFontSize(), theme.ForegroundBrush);

                if (!string.IsNullOrWhiteSpace(AppConfig.Instance.FontFamily))
                {
                    try
                    {
                        PreviewViewer.Document.FontFamily =
                            new System.Windows.Media.FontFamily(AppConfig.Instance.FontFamily);
                    }
                    catch { }
                }

                // プレビュー中は検索できないので終了させる
                _bashKeys?.EndSearch();

                NoteRichTextBox.Visibility = Visibility.Collapsed;
                PreviewViewer.Visibility = Visibility.Visible;
                PreviewIcon.Text = "✏";
                BtnPreview.ToolTip = "編集に戻る";

                // 描画が済むまで表示幅が確定しないため、画像の追従はレイアウト後に行う
                Dispatcher.BeginInvoke(new Action(UpdatePreviewImageSizes),
                    System.Windows.Threading.DispatcherPriority.Loaded);
            }
            else
            {
                PreviewViewer.Visibility = Visibility.Collapsed;
                PreviewViewer.Document = null;
                NoteRichTextBox.Visibility = Visibility.Visible;
                PreviewIcon.Text = "👁";
                BtnPreview.ToolTip = "Markdownプレビュー";

                // プレビュー中に待ち時間が過ぎた分を、編集画面へ戻る時点で揃える
                if (!_isInitializing)
                {
                    SyncImagePreviews();
                }
            }
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

            FoldIcon.Foreground = theme.HeaderForegroundBrush;

            // プレビュー中は配色を文字色から作っているため、描画し直す
            if (!_isInitializing && Note.IsPreview)
            {
                ApplyPreviewState(true);
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

                // 本文が隠れる以上、検索も続けられないので確定して終了する
                _bashKeys?.EndSearch();

                ContentArea.Visibility = Visibility.Collapsed;
                // ヘッダー(28) + ドロップシャドウ用マージン(8+8) = 44
                MinHeight = FoldedWindowHeight;
                MaxHeight = FoldedWindowHeight;
                Height = FoldedWindowHeight;
                FoldIcon.Text = "▼";
                BtnFold.ToolTip = "展開する";
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
                MainBorder.CornerRadius = new CornerRadius(8);
                HeaderBorder.CornerRadius = new CornerRadius(7, 7, 0, 0);
            }
        }

        /// <summary>
        /// ヘッダーが画面外にある場合だけ、掴める位置まで引き戻す。
        /// 2画面目を取り外した後や解像度を変えた後は、保存された座標のまま表示しても
        /// 付箋がどこにも見えず、移動する手段が無くなるため。見えている場合は動かさない
        /// （一時的にモニタを外しただけの構成で、保存済みのレイアウトを壊さないため）。
        /// </summary>
        public void EnsureOnScreen()
        {
            if (ScreenHelper.IsHeaderVisible(Left, Top, Width, FoldedWindowHeight)) return;

            double height = Note.IsFolded ? FoldedWindowHeight : Math.Max(120, _expandedHeight);
            var (x, y) = ScreenHelper.ClampToVirtualScreen(Left, Top, Width, height);
            Left = x;
            Top = y;

            SyncModelFromWindow();
            NoteManager.Instance.RequestAutoSave();
        }

        public void SyncModelFromWindow()
        {
            if (!_isInitializing)
            {
                Note.X = Left;
                Note.Y = Top;
                Note.Width = Width;
                Note.Opacity = Opacity;
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

        /// <summary>
        /// 本文中の画像に使える横幅。スクロールバーや余白を除いた実際の表示幅を返す。
        /// </summary>
        private double GetContentWidthForImages()
        {
            double width = NoteRichTextBox.ViewportWidth;

            if (double.IsNaN(width) || width <= 0)
            {
                width = NoteRichTextBox.ActualWidth
                        - NoteRichTextBox.Padding.Left - NoteRichTextBox.Padding.Right
                        - NoteRichTextBox.BorderThickness.Left - NoteRichTextBox.BorderThickness.Right;
            }

            // 丸め誤差で横スクロールバーが出ないよう、わずかに余裕をとる
            return Math.Max(40, width - 4);
        }

        /// <summary>本文中の画像を現在の付箋幅に合わせる。</summary>
        private void UpdateImageSizes()
        {
            FlowDocumentHelper.ApplyResponsiveImageSize(NoteRichTextBox.Document, GetContentWidthForImages());
        }

        /// <summary>
        /// 本文の Markdown 画像記法と、編集画面に表示している画像を一致させる。
        ///
        /// 差分がなければ本文に触れないため、入力中に呼ばれても実害はない。
        /// 表示だけの変更なので、ここでは保存を要求しない。
        /// </summary>
        private void SyncImagePreviews()
        {
            _imageSyncTimer.Stop();
            if (_syncingImages) return;

            _syncingImages = true;
            try
            {
                var caret = NoteRichTextBox.CaretPosition;

                if (!FlowDocumentHelper.SyncImagePreviews(NoteRichTextBox.Document)) return;

                // ブロックの増減でキャレットが動くことがあるため、元の位置へ戻す
                if (caret != null && caret.IsInSameDocument(NoteRichTextBox.Document.ContentStart))
                {
                    NoteRichTextBox.CaretPosition = caret;
                }

                UpdateImageSizes();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteWindow] SyncImagePreviews error: {ex.Message}");
            }
            finally
            {
                _syncingImages = false;
            }
        }

        /// <summary>プレビューの画像に使える横幅。縦スクロールバーの分を見込む。</summary>
        private double GetContentWidthForPreview()
        {
            double width = PreviewViewer.ActualWidth
                           - PreviewViewer.Padding.Left - PreviewViewer.Padding.Right;

            return Math.Max(40, width - 24);
        }

        /// <summary>プレビュー中の画像を現在の付箋幅に合わせる。</summary>
        private void UpdatePreviewImageSizes()
        {
            if (PreviewViewer.Document == null) return;

            FlowDocumentHelper.ApplyResponsiveImageSize(PreviewViewer.Document, GetContentWidthForPreview());
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

            // 現在の不透明度をスライダーに同期
            double currentOpacity = Note.Opacity > 0 ? Note.Opacity : this.Opacity;
            if (currentOpacity <= 0 || currentOpacity > 1.0) currentOpacity = 1.0;

            _updatingOpacitySlider = true;
            OpacitySlider.Value = Math.Round(currentOpacity * 100);
            OpacityValueTextBlock.Text = $"{(int)OpacitySlider.Value}%";
            _updatingOpacitySlider = false;

            // 現在の文字サイズをスライダーに同期
            double currentFontSize = ResolveFontSize();

            _updatingFontSizeSlider = true;
            FontSizeSlider.Value = currentFontSize;
            FontSizeValueTextBlock.Text = FormatFontSize(currentFontSize);
            _updatingFontSizeSlider = false;

            ColorPickerPopup.IsOpen = true;
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingOpacitySlider || _isInitializing) return;

            int percent = (int)Math.Round(e.NewValue);
            if (OpacityValueTextBlock != null)
            {
                OpacityValueTextBlock.Text = $"{percent}%";
            }

            double newOpacity = percent / 100.0;
            this.Opacity = Math.Clamp(newOpacity, 0.1, 1.0);
            Note.Opacity = this.Opacity;

            NoteManager.Instance.RequestAutoSave();
        }

        private static string FormatFontSize(double size)
            => size.ToString("0.#", CultureInfo.InvariantCulture);

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_updatingFontSizeSlider || _isInitializing) return;

            double newSize = Math.Clamp(e.NewValue, 9.0, 36.0);
            if (FontSizeValueTextBlock != null)
            {
                FontSizeValueTextBlock.Text = FormatFontSize(newSize);
            }

            Note.FontSize = newSize;
            ApplyFontSize(newSize);

            if (Note.IsPreview)
            {
                ApplyPreviewState(true);
            }

            NoteManager.Instance.RequestAutoSave();
        }

        private void BtnPresetOpacity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tagStr &&
                double.TryParse(tagStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacityVal))
            {
                _updatingOpacitySlider = true;
                OpacitySlider.Value = Math.Round(opacityVal * 100);
                OpacityValueTextBlock.Text = $"{(int)OpacitySlider.Value}%";
                _updatingOpacitySlider = false;

                this.Opacity = Math.Clamp(opacityVal, 0.1, 1.0);
                Note.Opacity = this.Opacity;

                NoteManager.Instance.RequestAutoSave();
            }
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

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            Note.IsPreview = !Note.IsPreview;
            ApplyPreviewState(Note.IsPreview);
            NoteManager.Instance.RequestAutoSave();
        }

        /// <summary>
        /// ヘッダーの ✕ は付箋をデスクトップから剥がして保管する。内容は消さない。
        ///
        /// ここから完全削除できないのは、折りたたみ ▲ の隣という押し間違えやすい位置にあるため。
        /// 取り消せない操作は、確認を挟むメモ一覧マネージャー側に集約している。
        /// </summary>
        private void BtnDetach_Click(object sender, RoutedEventArgs e)
        {
            NoteManager.Instance.ToggleNoteVisibility(Note);
        }

        private void NoteRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // 同期が画像ブロックを出し入れしたときの通知。本文のテキストは変わっていない。
            if (_isInitializing || _syncingImages) return;

            // 改行や貼り付けで生成された段落にもマージン除去を適用
            FlowDocumentHelper.NormalizeParagraphSpacing(NoteRichTextBox.Document);

            Note.PlainText = FlowDocumentHelper.GetPlainText(NoteRichTextBox.Document);
            Note.ContentXaml = FlowDocumentHelper.SaveToXaml(NoteRichTextBox.Document);
            Note.UpdatedAt = DateTime.Now;

            UpdateTitleDisplay();
            NoteManager.Instance.RequestAutoSave();

            // 画像記法の増減は、入力が落ち着いてから本文へ反映する
            _imageSyncTimer.Stop();
            _imageSyncTimer.Start();
        }

        private void NoteRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // bash モードのキーバインドを先に処理する。
            // Ctrl+N は bash では「1行下」なので、有効時は新規付箋作成より編集操作を優先する。
            if (_bashKeys != null && _bashKeys.HandleKey(e))
            {
                e.Handled = true;
                return;
            }

            // Alt+N: 新しい付箋を作成。
            // bash モードでは Ctrl+N がカーソル移動に使われるため、その影響を受けない代替として常に用意する。
            // Alt 併用時、WPF は e.Key に Key.System を入れ、実際のキーを e.SystemKey に入れる。
            var altKey = e.Key == Key.System ? e.SystemKey : e.Key;
            if (Keyboard.Modifiers == ModifierKeys.Alt && altKey == Key.N)
            {
                BtnNew_Click(this, new RoutedEventArgs());
                e.Handled = true;
                return;
            }

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
            }
        }

        /// <summary>
        /// インクリメンタル検索中は、入力された文字を本文ではなく検索文字列へ送る。
        /// PreviewKeyDown ではなくここで受けるのは、記号・かなや IME の確定文字を正しく取るため。
        /// </summary>
        private void NoteRichTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (_bashKeys != null && _bashKeys.HandleTextInput(e.Text))
            {
                e.Handled = true;
            }
        }

        /// <summary>フォーカスが外れたら検索は確定して終了する。</summary>
        private void NoteRichTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            _bashKeys?.EndSearch();

            // 待ち時間の途中で編集を離れた分を取りこぼさない
            SyncImagePreviews();
        }

        /// <summary>右クリックされた画像。メニューの項目を押したときの対象となる。</summary>
        private Image? _contextMenuImage;

        /// <summary>
        /// 右クリックメニューを開く直前に、カーソル下の画像を調べて画像用の項目を出し入れする。
        /// 文書内の Image に直接メニューを付ける方法は、RichTextBox の IsDocumentEnabled が
        /// 既定の false だと埋め込み要素へマウス入力が渡らないため使えない。
        /// </summary>
        private void NoteRichTextBox_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // キーボード（メニューキー）から開かれた場合は座標が -1 になる。
            // マウス位置は無関係な場所を指しているため、画像用の項目は出さない。
            _contextMenuImage = (e.CursorLeft < 0 || e.CursorTop < 0)
                ? null
                : FindImageAt(Mouse.GetPosition(NoteRichTextBox));

            var visibility = _contextMenuImage != null ? Visibility.Visible : Visibility.Collapsed;
            ImageMenuSeparator.Visibility = visibility;
            CopyImageMenuItem.Visibility = visibility;
            DeleteImageMenuItem.Visibility = visibility;
        }

        /// <summary>本文中の指定位置にある画像を返す。無ければ null。</summary>
        private Image? FindImageAt(Point point)
        {
            var hit = VisualTreeHelper.HitTest(NoteRichTextBox, point)?.VisualHit;
            while (hit != null && hit != NoteRichTextBox)
            {
                if (hit is Image image) return image;
                hit = VisualTreeHelper.GetParent(hit);
            }
            return null;
        }

        private void MenuCopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (_contextMenuImage?.Source is not BitmapSource bitmap) return;

            try
            {
                Clipboard.SetImage(bitmap);
            }
            catch (Exception ex)
            {
                // 他のアプリがクリップボードを掴んでいると失敗する。付箋の内容は無事なので通知はしない。
                System.Diagnostics.Debug.WriteLine($"[NoteWindow] MenuCopyImage_Click error: {ex.Message}");
            }
        }

        private void MenuDeleteImage_Click(object sender, RoutedEventArgs e)
        {
            if (_contextMenuImage == null) return;

            // 記法を消す前に、どのファイルを指していたかを控えておく
            var markdownPath = _contextMenuImage.Tag as string;

            // 消すのは本文の Markdown 画像記法。表示だけを外しても、記法が残っていれば作り直される。
            FlowDocumentHelper.RemoveImage(_contextMenuImage);

            _contextMenuImage = null;

            // 参照が残っているかを調べる前に、記法を消した結果を本文へ反映しておく
            Note.PlainText = FlowDocumentHelper.GetPlainText(NoteRichTextBox.Document);
            Note.ContentXaml = FlowDocumentHelper.SaveToXaml(NoteRichTextBox.Document);
            NoteManager.Instance.RequestAutoSave();

            // 他のどこからも参照されなくなった画像は、ファイルごと片付ける
            NoteManager.Instance.DeleteImageIfUnreferenced(markdownPath);
        }

        /// <summary>検索バーの表示を現在の検索状態に合わせる。</summary>
        private void UpdateSearchBar(SearchDisplayState state)
        {
            if (!state.IsActive)
            {
                SearchBar.Visibility = Visibility.Collapsed;
                return;
            }

            var theme = NoteColorTheme.GetTheme(Note.ColorTheme);
            SearchBar.Background = theme.HeaderBrush;
            SearchLabelTextBlock.Foreground = theme.HeaderForegroundBrush;
            SearchQueryTextBlock.Foreground = theme.HeaderForegroundBrush;

            SearchLabelTextBlock.Text = state.Forward ? "I-search:" : "I-search(逆):";
            SearchQueryTextBlock.Text = state.Query;

            if (!state.Found)
            {
                SearchStatusTextBlock.Text = "見つかりません";
            }
            else if (state.Wrapped)
            {
                SearchStatusTextBlock.Text = "折り返し";
            }
            else
            {
                SearchStatusTextBlock.Text = string.Empty;
            }

            SearchBar.Visibility = Visibility.Visible;
        }

        /// <summary>プレビュー内のリンクは既定のブラウザで開く。</summary>
        private void PreviewViewer_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = e.Uri.ToString(),
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[NoteWindow] RequestNavigate error: {ex.Message}");
            }

            e.Handled = true;
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
                            // 本文には記法だけを書き、画像はその記法を見て同期処理が表示する
                            FlowDocumentHelper.InsertImageMarker(NoteRichTextBox, fileName);
                            SyncImagePreviews();
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

            // 幅が変わったら画像を追従させる（折りたたみ中は本文が隠れているので不要）
            if (e.WidthChanged && !Note.IsFolded)
            {
                UpdateImageSizes();
                UpdatePreviewImageSizes();
            }
        }
    }
}
