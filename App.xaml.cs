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
            // 全局未捕获异常 -> 落地日志
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException(args.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, args) => { LogException(args.Exception, "Dispatcher"); args.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, args) => { LogException(args.Exception, "TaskScheduler"); args.SetObserved(); };

            base.OnStartup(e);

            // 检查命令行参数
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
                // 无头模式：不创建 UI，直接运行 HeadlessRunner
                return;
            }

            // 创建主窗口
            var mainWindow = new MainWindow();

            // 创建托盘管理器
            _trayManager = new TrayManager(mainWindow);
            mainWindow.SetTrayManager(_trayManager);

            // 如果指定了最小化启动，不显示窗口
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
                string line = "[" + time + "] [" + source + "]" + "
" + ex.ToString() + "

";
                File.AppendAllText(path, line);
            }
            catch { /* ignore */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}