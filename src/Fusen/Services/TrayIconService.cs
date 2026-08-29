using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Fusen.Services;

namespace Fusen.Services
{
    public class TrayIconService : IDisposable
    {
        private const int WM_USER = 0x0400;
        private const int WM_TRAYICON = WM_USER + 100;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_LBUTTONDBLCLK = 0x0203;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public uint uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO icon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateBitmap(int nWidth, int nHeight, uint cPlanes, uint cBitsPerPel, IntPtr lpvBits);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadIcon(IntPtr hInstance, IntPtr lpIconName);

        private const int IDI_APPLICATION = 32512;

        private HwndSource? _hwndSource;
        private NOTIFYICONDATA _nid;
        private IntPtr _hIcon = IntPtr.Zero;
        private ContextMenu? _contextMenu;
        private bool _isDisposed;

        public void Initialize()
        {
            var parameters = new HwndSourceParameters("FusenTraySource")
            {
                WindowStyle = 0,
                Width = 0,
                Height = 0,
                PositionX = 0,
                PositionY = 0
            };

            _hwndSource = new HwndSource(parameters);
            _hwndSource.AddHook(WndProc);

            _hIcon = CreateCustomFusenIcon();
            if (_hIcon == IntPtr.Zero)
            {
                _hIcon = LoadIcon(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
            }

            _nid = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATA)),
                hWnd = _hwndSource.Handle,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "fusen - デスクトップ付箋"
            };

            Shell_NotifyIcon(NIM_ADD, ref _nid);

            CreateContextMenu();
        }

        private IntPtr CreateCustomFusenIcon()
        {
            try
            {
                int width = 32;
                int height = 32;

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // 付箋本体（イエロー、丸角、縁取り）
                    var noteBrush = new SolidColorBrush(Color.FromRgb(255, 241, 118));
                    var borderPen = new Pen(new SolidColorBrush(Color.FromRgb(245, 127, 23)), 1.5);
                    var linePen = new Pen(new SolidColorBrush(Color.FromRgb(251, 192, 45)), 1.8);

                    dc.DrawRoundedRectangle(noteBrush, borderPen, new Rect(3, 3, 26, 26), 4, 4);

                    // 付箋内のメモ罫線
                    dc.DrawLine(linePen, new Point(8, 10), new Point(22, 10));
                    dc.DrawLine(linePen, new Point(8, 16), new Point(24, 16));
                    dc.DrawLine(linePen, new Point(8, 22), new Point(18, 22));
                }

                var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);

                var pixels = new int[width * height];
                rtb.CopyPixels(pixels, width * 4, 0);

                var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                IntPtr hbmColor = IntPtr.Zero;
                IntPtr hbmMask = IntPtr.Zero;
                try
                {
                    hbmColor = CreateBitmap(width, height, 1, 32, handle.AddrOfPinnedObject());
                    
                    // マスクビットマップ (1bpp)
                    var maskBytes = new byte[(width * height) / 8];
                    hbmMask = CreateBitmap(width, height, 1, 1, IntPtr.Zero);

                    var iconInfo = new ICONINFO
                    {
                        fIcon = true,
                        xHotspot = 0,
                        yHotspot = 0,
                        hbmMask = hbmColor,
                        hbmColor = hbmColor
                    };

                    return CreateIconIndirect(ref iconInfo);
                }
                finally
                {
                    handle.Free();
                    if (hbmColor != IntPtr.Zero) DeleteObject(hbmColor);
                    if (hbmMask != IntPtr.Zero) DeleteObject(hbmMask);
                }
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenu
            {
                FontFamily = new FontFamily("Segoe UI, Yu Gothic UI, Meiryo"),
                FontSize = 12.5
            };

            var itemNew = new MenuItem { Header = "📝 新しい付箋を作成", FontWeight = FontWeights.SemiBold };
            itemNew.Click += (s, e) => NoteManager.Instance.CreateNewNote();

            var itemList = new MenuItem { Header = "📋 メモ一覧マネージャー" };
            itemList.Click += (s, e) => NoteManager.Instance.ShowNoteList();

            var itemOpenDir = new MenuItem { Header = "📂 データフォルダを開く" };
            itemOpenDir.Click += (s, e) => OpenDataDirectory();

            var itemOpenConfig = new MenuItem { Header = "⚙️ 設定 (fusen.ini) を開く" };
            itemOpenConfig.Click += (s, e) => OpenConfigFile();

            var itemExpandAll = new MenuItem { Header = "▲ すべて展開" };
            itemExpandAll.Click += (s, e) => NoteManager.Instance.FoldAll(false);

            var itemFoldAll = new MenuItem { Header = "▼ すべて折りたたむ" };
            itemFoldAll.Click += (s, e) => NoteManager.Instance.FoldAll(true);

            var itemExit = new MenuItem { Header = "🚪 終了" };
            itemExit.Click += (s, e) => Application.Current.Shutdown();

            _contextMenu.Items.Add(itemNew);
            _contextMenu.Items.Add(itemList);
            _contextMenu.Items.Add(itemOpenDir);
            _contextMenu.Items.Add(itemOpenConfig);
            _contextMenu.Items.Add(new Separator());
            _contextMenu.Items.Add(itemExpandAll);
            _contextMenu.Items.Add(itemFoldAll);
            _contextMenu.Items.Add(new Separator());
            _contextMenu.Items.Add(itemExit);
        }

        private void OpenDataDirectory()
        {
            try
            {
                var dir = StorageService.Instance.DataDirectory;
                if (Directory.Exists(dir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dir,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"データフォルダを開けませんでした:\n{ex.Message}", "fusen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenConfigFile()
        {
            try
            {
                AppConfig.Instance.OpenConfigFileInEditor();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"設定ファイルを開けませんでした:\n{ex.Message}", "fusen", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                var mouseMsg = lParam.ToInt32();
                if (mouseMsg == WM_LBUTTONUP)
                {
                    // 左クリック: 新しい付箋を作成
                    NoteManager.Instance.CreateNewNote();
                    handled = true;
                }
                else if (mouseMsg == WM_RBUTTONUP)
                {
                    // 右クリック: コンテキストメニューを表示
                    ShowContextMenu();
                    handled = true;
                }
            }

            return IntPtr.Zero;
        }

        private void ShowContextMenu()
        {
            if (_contextMenu == null || _hwndSource == null) return;

            SetForegroundWindow(_hwndSource.Handle);
            GetCursorPos(out var pt);

            _contextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.AbsolutePoint;
            _contextMenu.HorizontalOffset = pt.X;
            _contextMenu.VerticalOffset = pt.Y;
            _contextMenu.IsOpen = true;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            Shell_NotifyIcon(NIM_DELETE, ref _nid);

            if (_hIcon != IntPtr.Zero)
            {
                DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }

            if (_hwndSource != null)
            {
                _hwndSource.RemoveHook(WndProc);
                _hwndSource.Dispose();
                _hwndSource = null;
            }
        }
    }
}
