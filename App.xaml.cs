using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace WinRemoteSharp
{
    public partial class App : System.Windows.Application
    {
        private TrayManager _trayManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException(args.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, args) => { LogException(args.Exception, "Dispatcher"); args.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, args) => { LogException(args.Exception, "TaskScheduler"); args.SetObserved(); };

            this.StartupUri = null;

            base.OnStartup(e);

            bool startMinimized = false;
            bool headless = false;

            foreach (string arg in e.Args)
            {
                string a = arg.ToLowerInvariant();
                if (a == "--minimized" || a == "-m")
                    startMinimized = true;
                else if (a == "--headless")
                    headless = true;
            }

            if (headless)
            {
                HeadlessRunner.RunAsync(e.Args).GetAwaiter().GetResult();
                return;
            }

            var mainWindow = new MainWindow();

            _trayManager = new TrayManager(mainWindow);
            mainWindow.SetTrayManager(_trayManager);

            if (!startMinimized)
            {
                mainWindow.Show();
            }
        }

        private void LogException(Exception ex, string source)
        {
            if (ex == null) return;
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = "[" + time + "] [" + source + "]\n" + ex.ToString() + "\n\n";
                File.AppendAllText(path, line);
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}
