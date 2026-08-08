#nullable enable
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    /// <summary>
    /// 系统托盘管理器。
    /// 关闭窗口后托盘继续运行，双击恢复窗口，右键菜单操作。
    /// 运行在 WPF 主线程上（不需要独立线程）。
    /// </summary>
    public class TrayManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private NotifyIcon? _notifyIcon;
        private Icon? _iconOnline;
        private Icon? _iconOffline;
        private bool _disposed;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            try
            {
                // 代码生成彩色盾牌图标（离线=灰色，在线=绿色）
                _iconOnline = CreateShieldIcon("#FF00B894");
                _iconOffline = CreateShieldIcon("#FF5A6280");

                var icon = _iconOffline ?? _iconOnline ?? SystemIcons.Application;

                _notifyIcon = new NotifyIcon
                {
                    Icon = icon,
                    Text = "WinRemote Agent — 未连接",
                    Visible = true
                };

                _notifyIcon.DoubleClick += OnTrayDoubleClick;
                _notifyIcon.ContextMenuStrip = BuildContextMenu();

                // 启动时显示气泡确认托盘工作正常
                _notifyIcon.ShowBalloonTip(2000, "WinRemote Agent", "程序已启动，关闭窗口后将在后台运行",
                    ToolTipIcon.Info);

                LogToFile("[TrayManager] Created successfully on main thread");
            }
            catch (Exception ex)
            {
                LogToFile($"[TrayManager] Constructor FAILED: {ex}");
            }
        }

        private void OnTrayDoubleClick(object? sender, EventArgs e)
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow.Show();
                    _mainWindow.WindowState = WindowState.Normal;
                    _mainWindow.Activate();
                });
            }
            catch (Exception ex)
            {
                LogToFile($"[TrayManager] ShowWindow FAILED: {ex}");
            }
        }

        public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            try { _notifyIcon?.ShowBalloonTip(3000, title, message, icon); }
            catch (Exception ex) { LogToFile($"[TrayManager] BalloonTip FAILED: {ex}"); }
        }

        public void UpdateConnectionStatus(bool connected)
        {
            try
            {
                if (_notifyIcon == null) return;
                if (connected && _iconOnline != null)
                {
                    _notifyIcon.Icon = _iconOnline;
                    _notifyIcon.Text = "WinRemote Agent — 已连接";
                }
                else if (_iconOffline != null)
                {
                    _notifyIcon.Icon = _iconOffline;
                    _notifyIcon.Text = "WinRemote Agent — 未连接";
                }
            }
            catch (Exception ex)
            {
                LogToFile($"[TrayManager] UpdateStatus FAILED: {ex}");
            }
        }

        // ===== 图标生成 =====

        private Icon? CreateShieldIcon(string colorHex)
        {
            try
            {
                var c = ColorTranslator.FromHtml(colorHex);
                using var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                    using var brush = new SolidBrush(c);
                    var pts = new Point[] {
                        new(4, 3), new(28, 3), new(28, 14),
                        new(16, 29), new(4, 14)
                    };
                    g.FillPolygon(brush, pts);

                    using var pen = new Pen(Color.White, 2f);
                    g.DrawLine(pen, 8, 9, 11, 19);
                    g.DrawLine(pen, 11, 19, 14, 13);
                    g.DrawLine(pen, 14, 13, 16, 21);
                    g.DrawLine(pen, 16, 21, 18, 13);
                    g.DrawLine(pen, 18, 13, 21, 19);
                    g.DrawLine(pen, 21, 19, 24, 9);
                }

                return Icon.FromHandle(bmp.GetHicon());
            }
            catch (Exception ex)
            {
                LogToFile($"[TrayManager] CreateShieldIcon FAILED: {ex}");
                return null;
            }
        }

        // ===== 右键菜单 =====

        private ContextMenuStrip BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new DarkMenuRenderer();

            var showItem = menu.Items.Add("📋 显示窗口");
            showItem.Click += (s, e) => OnTrayDoubleClick(null, EventArgs.Empty);

            menu.Items.Add(new ToolStripSeparator());

            var connItem = menu.Items.Add("🔗 连接服务器");
            connItem.Click += (s, e) => InvokeOnMain(() => _mainWindow.TrayConnect());

            var discItem = menu.Items.Add("⛓️ 断开连接");
            discItem.Click += (s, e) => InvokeOnMain(() => _mainWindow.TrayDisconnect());

            menu.Items.Add(new ToolStripSeparator());

            var svcMenu = new ToolStripMenuItem("⚙️ 服务管理");
            svcMenu.DropDownItems.Add("安装服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayInstallService()));
            svcMenu.DropDownItems.Add("卸载服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayUninstallService()));
            svcMenu.DropDownItems.Add(new ToolStripSeparator());
            svcMenu.DropDownItems.Add("启动服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayStartService()));
            svcMenu.DropDownItems.Add("停止服务", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayStopService()));
            menu.Items.Add(svcMenu);

            menu.Items.Add(new ToolStripSeparator());

            var autoItem = new ToolStripMenuItem("🚀 开机自启");
            autoItem.CheckOnClick = true;
            autoItem.Checked = IsAutoStartEnabled();
            autoItem.Click += (s, e) => ToggleAutoStart(autoItem);
            menu.Items.Add(autoItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("📁 日志目录", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayOpenLogDir()));
            menu.Items.Add("ℹ️ 关于", null, (s, e) => InvokeOnMain(() => _mainWindow.TrayCheckUpdate()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("❌ 退出程序", null, (s, e) => ExitApplication());

            return menu;
        }

        private void InvokeOnMain(Action action)
        {
            try { _mainWindow.Dispatcher.Invoke(action); }
            catch (Exception ex) { LogToFile($"[TrayManager] Invoke FAILED: {ex}"); }
        }

        // ===== 开机自启 =====

        private bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("WinRemoteAgent") != null;
            }
            catch { return false; }
        }

        private void ToggleAutoStart(ToolStripMenuItem item)
        {
            try
            {
                string? exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (item.Checked)
                    key?.SetValue("WinRemoteAgent", $"\"{exePath}\" --hide");
                else
                    key?.DeleteValue("WinRemoteAgent", false);

                InvokeOnMain(() => _mainWindow.AddLog($"开机自启已{(item.Checked ? "启用" : "禁用")}"));
            }
            catch (Exception ex)
            {
                item.Checked = !item.Checked;
                LogToFile($"[TrayManager] AutoStart FAILED: {ex}");
            }
        }

        private void ExitApplication()
        {
            try
            {
                _mainWindow.Dispatcher.Invoke(() =>
                {
                    _mainWindow._closingToTray = false;
                    ((App)System.Windows.Application.Current).ShutdownApp();
                });
            }
            catch
            {
                Environment.Exit(0);
            }
        }

        // ===== 错误日志 =====

        private static void LogToFile(string message)
        {
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray_error.log");
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        // ===== Dispose =====

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_notifyIcon != null)
            {
                try
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }
                catch { }
            }

            _iconOnline?.Dispose();
            _iconOffline?.Dispose();
        }
    }

    /// <summary>
    /// 托盘右键菜单的暗色渲染器
    /// </summary>
    internal class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkColorTable()) { }

        private class DarkColorTable : System.Windows.Forms.ProfessionalColorTable
        {
            public override Color MenuItemBorder => Color.FromArgb(45, 50, 70);
            public override Color MenuItemSelected => Color.FromArgb(91, 94, 166);
            public override Color MenuItemSelectedGradientBegin => Color.FromArgb(91, 94, 166);
            public override Color MenuItemSelectedGradientEnd => Color.FromArgb(72, 75, 138);
            public override Color ToolStripDropDownBackground => Color.FromArgb(30, 35, 55);
            public override Color ImageMarginGradientBegin => Color.FromArgb(30, 35, 55);
            public override Color ImageMarginGradientMiddle => Color.FromArgb(30, 35, 55);
            public override Color ImageMarginGradientEnd => Color.FromArgb(30, 35, 55);
            public override Color MenuBorder => Color.FromArgb(45, 50, 70);
            public override Color SeparatorDark => Color.FromArgb(45, 50, 70);
            public override Color SeparatorLight => Color.FromArgb(45, 50, 70);
        }
    }
}
