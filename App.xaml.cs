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

            // 关键：设为 OnExplicitShutdown，关闭窗口不会退出程序，托盘继续运行
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            base.OnStartup(e);

            if (this.MainWindow is MainWindow mw)
            {
                try
                {
                    // 在窗口 Show 之前创建托盘，确保两者同时出现
                    _trayManager = new TrayManager(mw);
                    mw.SetTrayManager(_trayManager);
                    Log("TrayManager created successfully");
                }
                catch (Exception ex)
                {
                    Log(ex);
                    Log($"TrayManager creation failed: {ex}");
                }

                if (!hideWindow)
                {
                    mw.Show();
                    Log("MainWindow shown");
                }
            }
            else
            {
                Log("MainWindow is null after base.OnStartup");
            }
        }

        private static void Log(string msg)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
            }
            catch { }
        }

        private static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "error.log"),
                    $"[{DateTime.Now:HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// 由 MainWindow 或 TrayManager 调用以真正退出程序
        /// </summary>
        public void ShutdownApp()
        {
            _trayManager?.Dispose();
            this.Shutdown();
        }
    }
}
