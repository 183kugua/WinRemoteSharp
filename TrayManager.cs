#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    /// <summary>
    /// 系统托盘管理器 — 使用独立 WinForms 线程 + Application.Run() 作为托盘消息宿主。
    /// NotifyIcon 运行在纯 WinForms 消息泵线程中，与 WPF 渲染管线完全隔离。
    /// </summary>
    public class TrayManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private readonly Thread _trayThread;
        private readonly ManualResetEventSlim _trayReady = new(false);
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private System.Drawing.Bitmap? _trayIconBitmap;
        private bool _disposed;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            _trayThread = new Thread(RunTrayMessageLoop)
            {
                Name = "TrayIconThread",
                IsBackground = true,
            };
            _trayThread.SetApartmentState(ApartmentState.STA);
            _trayThread.Start();

            if (!_trayReady.Wait(TimeSpan.FromSeconds(5)))
            {
                Debug.WriteLine("[TrayManager] Timeout waiting for tray thread to start");
            }
        }

        private void RunTrayMessageLoop()
        {
            try
            {
                System.Windows.Forms.Application.EnableVisualStyles();
                System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

                CreateTrayOnTrayThread();
                _trayReady.Set();
                System.Windows.Forms.Application.Run();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayManager] Tray thread crashed: {ex}");
                _trayReady.Set();
            }
        }

        private void CreateTrayOnTrayThread()
        {
            try
            {
                var icon = LoadOrCreateIcon();
                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = icon,
                    Text = "WinRemote Agent",
                    Visible = true
                };

                _notifyIcon.DoubleClick += (s, e) => ToggleWindow();
                _notifyIcon.ContextMenuStrip = BuildContextMenu();

                Debug.WriteLine("[TrayManager] NotifyIcon created on dedicated WinForms thread — Visible=true");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayManager] CreateTrayOnTrayThread failed: {ex}");
            }
        }

        private System.Drawing.Icon LoadOrCreateIcon()
        {
            // 方案 1: EmbeddedResource
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using (var stream = assembly.GetManifestResourceStream("WinRemoteSharp.Resources.DialogIcon.png"))
                {
                    if (stream != null)
                    {
                        _trayIconBitmap = new System.Drawing.Bitmap(stream);
                        return System.Drawing.Icon.FromHandle(_trayIconBitmap.GetHicon());
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] EmbeddedResource load failed: {ex}"); }

            // 方案 2: 文件系统直接加载
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var pngPath = Path.Combine(baseDir, "Resources", "DialogIcon.png");
                if (File.Exists(pngPath))
                {
                    _trayIconBitmap = new System.Drawing.Bitmap(pngPath);
                    return System.Drawing.Icon.FromHandle(_trayIconBitmap.GetHicon());
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] File load failed: {ex}"); }

            // 方案 3: 代码生成绿色盾牌图标
            return GenerateFallbackIcon();
        }

        private System.Drawing.Icon GenerateFallbackIcon()
        {
            try
            {
                _trayIconBitmap = new System.Drawing.Bitmap(32, 32);
                using (var g = System.Drawing.Graphics.FromImage(_trayIconBitmap))
                {
                    g.Clear(System.Drawing.Color.Transparent);
                    using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(46, 139, 87)))
                    using (var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(34, 139, 34), 2))
                    {
                        var pts = new System.Drawing.Point[] {
                            new(4, 4), new(27, 4), new(27, 14),
                            new(16, 28), new(4, 14)
                        };
                        g.FillPolygon(brush, pts);
                        g.DrawPolygon(pen, pts);
                    }
                }
                return System.Drawing.Icon.FromHandle(_trayIconBitmap.GetHicon());
            }
            catch
            {
                return System.Drawing.SystemIcons.Application;
            }
        }

        private System.Windows.Forms.ContextMenuStrip BuildContextMenu()
        {
            var menu = new System.Windows.Forms.ContextMenuStrip();

            menu.Items.Add("显示窗口", null, (s, e) => ToggleWindow());
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("连接服务器", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayConnect()));
            menu.Items.Add("断开连接", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayDisconnect()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var svcMenu = new System.Windows.Forms.ToolStripMenuItem("服务管理");
            svcMenu.DropDownItems.Add("安装服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayInstallService()));
            svcMenu.DropDownItems.Add("卸载服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayUninstallService()));
            svcMenu.DropDownItems.Add(new System.Windows.Forms.ToolStripSeparator());
            svcMenu.DropDownItems.Add("启动服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayStartService()));
            svcMenu.DropDownItems.Add("停止服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayStopService()));
            svcMenu.DropDownItems.Add("查看状态", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayServiceStatus()));
            menu.Items.Add(svcMenu);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

            var autoItem = new System.Windows.Forms.ToolStripMenuItem("开机自启");
            autoItem.CheckOnClick = true;
            autoItem.Checked = IsAutoStartEnabled();
            autoItem.Click += (s, e) => ToggleAutoStart(autoItem);
            menu.Items.Add(autoItem);

            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("关于", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayCheckUpdate()));
            menu.Items.Add("刷新日志", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayRefreshLogs()));
            menu.Items.Add("打开日志目录", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayOpenLogDir()));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, (s, e) => ExitApplication());

            return menu;
        }

        private void InvokeOnMain(Action action)
        {
            try { _mainWindow.Dispatcher.Invoke(action); }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] InvokeOnMain failed: {ex}"); }
        }

        private void ToggleWindow()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    if (_mainWindow.Visibility == Visibility.Visible)
                    {
                        _mainWindow.Hide();
                    }
                    else
                    {
                        _mainWindow.Show();
                        _mainWindow.WindowState = WindowState.Normal;
                        _mainWindow.Activate();
                    }
                });
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] ToggleWindow failed: {ex}"); }
        }

        public void ShowBalloonTip(string title, string message)
        {
            try { _notifyIcon?.ShowBalloonTip(3000, title, message, System.Windows.Forms.ToolTipIcon.Info); }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] ShowBalloonTip failed: {ex}"); }
        }

        public void UpdateConnectionStatus(bool connected)
        {
            try
            {
                if (_notifyIcon != null)
                    _notifyIcon.Text = connected ? "WinRemote Agent - 已连接" : "WinRemote Agent - 未连接";
            }
            catch (Exception ex) { Debug.WriteLine($"[TrayManager] UpdateConnectionStatus failed: {ex}"); }
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key?.GetValue("WinRemoteAgent") != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart(System.Windows.Forms.ToolStripMenuItem item)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (item.Checked)
                        key?.SetValue("WinRemoteAgent", $"\"{exePath}\" --hide");
                    else
                        key?.DeleteValue("WinRemoteAgent", false);
                }

                InvokeOnMain(() => _mainWindow.AddLog($"开机自启已{(item.Checked ? "启用" : "禁用")}"));
            }
            catch (Exception ex)
            {
                item.Checked = !item.Checked;
                InvokeOnMain(() => _mainWindow.AddLog($"设置开机自启失败: {ex.Message}"));
            }
        }

        private void ExitApplication()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow._closingToTray = false;
                    System.Windows.Application.Current.Shutdown();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayManager] ExitApplication failed: {ex}");
                Environment.Exit(0);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_notifyIcon != null)
            {
                try { _notifyIcon.Visible = false; _notifyIcon.Dispose(); _notifyIcon = null; }
                catch { }
            }

            try { System.Windows.Forms.Application.Exit(); }
            catch { }

            if (_trayThread.IsAlive)
                _trayThread.Join(TimeSpan.FromSeconds(3));

            if (_trayIconBitmap != null)
            {
                try { _trayIconBitmap.Dispose(); _trayIconBitmap = null; }
                catch { }
            }

            _trayReady.Dispose();
        }
    }
}
