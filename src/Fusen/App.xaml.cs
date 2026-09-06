using System;
using System.Windows;
using Fusen.Helpers;
using Fusen.Services;

namespace Fusen
{
    public partial class App : Application
    {
        private TrayIconService? _trayIconService;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // 付箋を1枚も作る前に済ませること。マウスでの範囲選択が IME(TSF) の応答待ちで
            // 止まるのを防ぐ。詳細は ImeCompat のコメントを参照。
            ImeCompat.DisableTextServicesFramework();

            AppConfig.Instance.Initialize();

            _trayIconService = new TrayIconService();
            _trayIconService.Initialize();

            NoteManager.Instance.Initialize();
        }

        private void Application_Exit(object sender, ExitEventArgs e)
        {
            _trayIconService?.Dispose();
            _trayIconService = null;

            NoteManager.Instance.Shutdown();
        }
    }
}
