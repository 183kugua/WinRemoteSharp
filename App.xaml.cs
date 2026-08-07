using System;
using System.IO;
using System.Windows;

namespace WinRemoteSharp
{
    public partial class App : System.Windows.Application
    {
        private TrayManager _trayManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) => Log(args.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, args) => { Log(args.Exception); args.Handled = true; };

            bool hideWindow = false;
            bool headless = false;

            foreach (string arg in e.Args)
            {
                string a = arg.ToLowerInvariant();
                if (a == "--hide" || a == "-h")
                    hideWindow = true;
                else if (a == "--headless")
                    headless = true;
            }

            if (headless)
            {
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                HeadlessRunner.RunAsync(e.Args).GetAwaiter().GetResult();
                this.Shutdown();
                return;
            }

            base.OnStartup(e);

            if (this.MainWindow is MainWindow mw)
            {
                if (hideWindow)
                {
                    mw.Hide();
                }
                else
                {
                    mw.Show();
                }

                mw.Loaded += (s, args) =>
                {
                    mw.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            _trayManager = new TrayManager(mw);
                            mw.SetTrayManager(_trayManager);
                        }
                        catch (Exception ex) { Log(ex); }
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                };
            }
        }

        private void Log(Exception ex)
        {
            if (ex == null) return;
            try { File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"), $"[{DateTime.Now:HH:mm:ss}] {ex}\n\n"); }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}
