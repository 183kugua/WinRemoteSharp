#nullable enable
using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace WinRemoteSharp
{
    /// <summary>
    /// 托盘管理器 — 负责状态图标切换和右键菜单高级功能。
    /// 基础托盘由 App.xaml.cs 直接创建。
    /// </summary>
    public class TrayManager : IDisposable
    {
        private readonly MainWindow _mainWindow;
        private Icon? _iconOnline;
        private Icon? _iconOffline;
        private bool _disposed;

        public TrayManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow;

            try
            {
                _iconOnline = CreateShieldIcon("#FF00B894");
                _iconOffline = CreateShieldIcon("#FF5A6280");
                Log("[TrayManager] Icons created OK");
            }
            catch (Exception ex)
            {
                Log($"[TrayManager] FAILED: {ex}");
            }
        }

        public void UpdateConnectionStatus(bool connected)
        {
            try
            {
                // App.xaml.cs 中的 _notifyIcon 不可访问，通过反射或委托更新
                // 这里不做操作，图标切换由 App 层处理
            }
            catch (Exception ex)
            {
                Log($"[TrayManager] UpdateStatus FAILED: {ex}");
            }
        }

        public void ShowBalloonTip(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
        {
            // 由 App 层处理
        }

        // ===== 图标生成 =====

        private static Icon? CreateShieldIcon(string colorHex)
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
                    var pts = new System.Drawing.Point[] {
                        new System.Drawing.Point(4, 3), new System.Drawing.Point(28, 3), new System.Drawing.Point(28, 14),
                        new System.Drawing.Point(16, 29), new System.Drawing.Point(4, 14)
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
                Log($"[TrayManager] CreateShieldIcon FAILED: {ex}");
                return null;
            }
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tray_error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _iconOnline?.Dispose();
            _iconOffline?.Dispose();
        }
    }
}
