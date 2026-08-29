using System;
using System.Windows;
using Fusen.Services;

namespace Fusen
{
    public partial class App : Application
    {
        private TrayIconService? _trayIconService;

        private void Application_Startup(object sender, StartupEventArgs e)
        {
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
