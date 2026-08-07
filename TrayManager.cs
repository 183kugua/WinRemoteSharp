#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    /// <summary>
    /// 系统托盘管理器 - 支持最小化到托盘、右键菜单、开机自启
    /// </summary>
    public class TrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly MainWindow _mainWindow;
        /// <summary>保持 bitmap 存活，防止 Icon.FromHandle 创建的 HICON 失效。</summary>
        private System.Drawing.Bitmap _trayIconBitmap;
        private bool _disposed = false;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHGetFolderPath(IntPtr hwndOwner, int nFolder, IntPtr hToken, uint dwFlags, System.Text.StringBuilder lpszPath);

        private const int CSIDL_STARTUP = 7;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            _notifyIcon = new NotifyIcon
            {
                Visible = true,
                Text = "WinRemote Agent",
                Icon = CreateTrayIcon()
            };

            _contextMenu = new ContextMenuStrip();
            BuildContextMenu();
            _notifyIcon.ContextMenuStrip = _contextMenu;

            _notifyIcon.DoubleClick += (s, e) => ToggleWindow();
            _mainWindow.StateChanged += (s, e) => UpdateTrayTooltip();
        }

        private Icon CreateTrayIcon()
        {
            // 优先：EmbeddedResource（.csproj 中配置为 <EmbeddedResource>）
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
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayManager] EmbeddedResource load failed: {ex.Message}");
            }

            // 回退：WPF Resource（pack:// 方式）
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/DialogIcon.png");
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo?.Stream != null)
                {
                    _trayIconBitmap = new System.Drawing.Bitmap(streamInfo.Stream);
                    return System.Drawing.Icon.FromHandle(_trayIconBitmap.GetHicon());
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrayManager] WPF Resource load failed: {ex.Message}");
            }

            // 最终回退：生成绿色盾牌图标
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
            catch { }

            return SystemIcons.Application;
        }

        private void BuildContextMenu()
        {
            _contextMenu.Items.Clear();

            var showHideItem = new ToolStripMenuItem("显示窗口");
            showHideItem.Click += (s, e) => ToggleWindow();
            _contextMenu.Items.Add(showHideItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var connectItem = new ToolStripMenuItem("连接服务器");
            connectItem.Click += (s, e) => _mainWindow.TrayConnect();
            _contextMenu.Items.Add(connectItem);

            var disconnectItem = new ToolStripMenuItem("断开连接");
            disconnectItem.Click += (s, e) => _mainWindow.TrayDisconnect();
            _contextMenu.Items.Add(disconnectItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var serviceMenu = new ToolStripMenuItem("服务管理");
            serviceMenu.DropDownItems.Add("安装服务", null, (s, e) => _mainWindow.TrayInstallService());
            serviceMenu.DropDownItems.Add("卸载服务", null, (s, e) => _mainWindow.TrayUninstallService());
            serviceMenu.DropDownItems.Add(new ToolStripSeparator());
            serviceMenu.DropDownItems.Add("启动服务", null, (s, e) => _mainWindow.TrayStartService());
            serviceMenu.DropDownItems.Add("停止服务", null, (s, e) => _mainWindow.TrayStopService());
            serviceMenu.DropDownItems.Add("查看状态", null, (s, e) => _mainWindow.TrayServiceStatus());
            _contextMenu.Items.Add(serviceMenu);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var autoStartItem = new ToolStripMenuItem("开机自启");
            autoStartItem.CheckOnClick = true;
            autoStartItem.Checked = IsAutoStartEnabled();
            autoStartItem.Click += (s, e) => ToggleAutoStart(autoStartItem);
            _contextMenu.Items.Add(autoStartItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var aboutItem = new ToolStripMenuItem("关于");
            aboutItem.Click += (s, e) => _mainWindow.TrayCheckUpdate();
            _contextMenu.Items.Add(aboutItem);

            var refreshLogsItem = new ToolStripMenuItem("刷新日志");
            refreshLogsItem.Click += (s, e) => _mainWindow.TrayRefreshLogs();
            _contextMenu.Items.Add(refreshLogsItem);

            var openLogDirItem = new ToolStripMenuItem("打开日志目录");
            openLogDirItem.Click += (s, e) => _mainWindow.TrayOpenLogDir();
            _contextMenu.Items.Add(openLogDirItem);

            _contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += (s, e) => ExitApplication();
            _contextMenu.Items.Add(exitItem);
        }

        private void ToggleWindow()
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

        private void UpdateTrayTooltip()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                string status = _mainWindow.IsConnected ? "已连接" : "未连接";
                _notifyIcon.Text = $"WinRemote Agent - {status}";
            });
        }

        public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }

        public void UpdateConnectionStatus(bool connected)
        {
            UpdateTrayTooltip();
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key?.GetValue("WinRemoteAgent") != null;
                }
            }
            catch { return false; }
        }

        private void ToggleAutoStart(ToolStripMenuItem menuItem)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;
                
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (menuItem.Checked)
                    {
                        key?.SetValue("WinRemoteAgent", $"\"{exePath}\" --minimized");
                    }
                    else
                    {
                        key?.DeleteValue("WinRemoteAgent", false);
                    }
                }
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.AddLog($"开机自启已{(menuItem.Checked ? "启用" : "禁用")}"));
            }
            catch (Exception ex)
            {
                menuItem.Checked = !menuItem.Checked;
                _mainWindow.Dispatcher.Invoke(() => _mainWindow.AddLog($"设置开机自启失败: {ex.Message}"));
            }
        }

        private void ExitApplication()
        {
            _mainWindow.Dispatcher.Invoke(() =>
            {
                _mainWindow._closingToTray = false;
                System.Windows.Application.Current.Shutdown();
            });
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _notifyIcon?.Dispose();
                _contextMenu?.Dispose();
                _trayIconBitmap?.Dispose();
                _disposed = true;
            }
        }
    }
}
