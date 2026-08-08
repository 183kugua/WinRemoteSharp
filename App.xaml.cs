using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    public partial class App : System.Windows.Application
    {
        private NotifyIcon _notifyIcon;
        private MainWindow _mainWindow;
        private TrayManager _trayManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 先设 ShutdownMode，防止窗口关闭时退出进程
            this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            bool hideWindow = false;
            foreach (string arg in e.Args)
            {
                string a = arg.ToLowerInvariant();
                if (a == "--hide" || a == "-h") hideWindow = true;
                else if (a == "--headless") { RunHeadless(e.Args); return; }
            }

            // 手动创建 MainWindow（不用 StartupUri，更可控）
            _mainWindow = new MainWindow();

            // ===== 先创建托盘 =====
            try
            {
                // 用最简单的图标 — 系统内置图标
                _notifyIcon = new NotifyIcon
                {
                    Icon = System.Drawing.SystemIcons.Shield,
                    Text = "WinRemote Agent",
                    Visible = true
                };
                _notifyIcon.DoubleClick += (s, args) =>
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        _mainWindow.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                        _mainWindow.Activate();
                    });
                };

                var menu = new ContextMenuStrip();
                menu.Items.Add("📋 显示窗口", null, (s, args) =>
                {
                    _mainWindow.Dispatcher.Invoke(() =>
                    {
                        _mainWindow.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                        _mainWindow.Activate();
                    });
                });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("❌ 退出程序", null, (s, args) =>
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _mainWindow._closingToTray = false;
                    this.Shutdown();
                });
                _notifyIcon.ContextMenuStrip = menu;

                _notifyIcon.ShowBalloonTip(2000, "WinRemote Agent", "托盘已启动", ToolTipIcon.Info);

                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.log"),
                    $"[{DateTime.Now:HH:mm:ss}] TRAY CREATED OK\n");
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.log"),
                    $"[{DateTime.Now:HH:mm:ss}] TRAY FAILED: {ex}\n");
            }

            // ===== 再创建 TrayManager（高级功能） =====
            try
            {
                _trayManager = new TrayManager(_mainWindow);
                _mainWindow.SetTrayManager(_trayManager);
            }
            catch (Exception ex)
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.log"),
                    $"[{DateTime.Now:HH:mm:ss}] TrayManager FAILED: {ex}\n");
            }

            // ===== 显示窗口 =====
            if (!hideWindow)
            {
                _mainWindow.Show();
            }

            File.AppendAllText(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app_log.log"),
                $"[{DateTime.Now:HH:mm:ss}] Window shown, hideWindow={hideWindow}\n");
        }

        private void RunHeadless(string[] args)
        {
            HeadlessRunner.RunAsync(args).GetAwaiter().GetResult();
            this.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _trayManager?.Dispose();
            if (_notifyIcon != null)
            {
                try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); } catch { }
            }
            base.OnExit(e);
        }
    }
}
