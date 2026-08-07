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
            // 全局未捕获异常 -> 落地日志
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogException(args.ExceptionObject as Exception, "AppDomain");
            DispatcherUnhandledException += (s, args) => { LogException(args.Exception, "Dispatcher"); args.Handled = true; };
            TaskScheduler.UnobservedTaskException += (s, args) => { LogException(args.Exception, "TaskScheduler"); args.SetObserved(); };

            // 检查命令行参数（必须在 base.OnStartup 之前，因为 XAML StartupUri 可能自动建窗）
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
                // 无头模式：不调用 base.OnStartup（绕过 XAML StartupUri 自动建窗），
                // 设置 OnExplicitShutdown 防止 WPF 在没有窗口时自动退出。
                this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                HeadlessRunner.RunAsync(e.Args).GetAwaiter().GetResult();
                this.Shutdown();
                return;
            }

            // GUI 模式：让 WPF 按 XAML StartupUri 自动创建 MainWindow。
            // 注意：base.OnStartup 返回后 this.MainWindow 可能尚未赋值（窗口创建是异步的），
            // 因此通过 Activated 事件延迟获取窗口引用。
            base.OnStartup(e);

            // 通过 Activated 事件获取 WPF 自动创建的 MainWindow
            this.Activated += (s, ev) =>
            {
                // 只处理第一次激活
                if (_trayManager != null) return;

                if (this.MainWindow is MainWindow mainWindow)
                {
                    try
                    {
                        _trayManager = new TrayManager(mainWindow);
                        mainWindow.SetTrayManager(_trayManager);
                    }
                    catch (Exception ex)
                    {
                        LogException(ex, "TrayManager");
                    }

                    if (startMinimized)
                    {
                        mainWindow.Hide();
                    }
                }
            };
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
            catch { /* ignore */ }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            base.OnExit(e);
        }
    }
}
