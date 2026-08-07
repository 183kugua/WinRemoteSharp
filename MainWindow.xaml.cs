using System;
#nullable enable
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
        private Core.AgentClient? _agent;
        private Core.Config _config;
        private Core.ServiceManager? _svcMgr;
        private TrayManager? _trayMgr;
        internal bool _closingToTray = true;

        public bool IsConnected => _agent?.IsConnected ?? false;

        public MainWindow()
        {
            InitializeComponent();
            
            // 设置窗口图标
            try
            {
                var iconUri = new Uri("pack://application:,,,/Resources/App.ico");
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(iconUri);
            }
            catch { }
            
            _config = Core.ConfigManager.Load();
            _svcMgr = new Core.ServiceManager();

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            Closing += MainWindow_Closing;
        }

        public void SetTrayManager(TrayManager trayMgr)
        {
            _trayMgr = trayMgr;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 确保窗口可见并置顶（防止某些情况下界面不显示）
            if (Visibility != Visibility.Visible)
                Show();
            WindowState = WindowState.Normal;
            Activate();
            Topmost = true;
            Topmost = false; // 取消置顶，让它回到正常层级
            
            LoadSettingsToUI();
            RefreshSystemInfo();
            RefreshServiceStatus();
            AddLog("WinRemote Agent V1.2 已就绪");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closingToTray)
            {
                e.Cancel = true;
                Hide();
                _trayMgr?.ShowBalloonTip("WinRemote Agent", "已最小化到托盘，双击图标显示窗口", System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                _agent?.DisconnectAsync().GetAwaiter().GetResult();
                _svcMgr?.Dispose();
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _agent?.DisconnectAsync().GetAwaiter().GetResult();
            _svcMgr?.Dispose();
        }

        #region 日志

        public void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(line + Environment.NewLine);
                TxtLog.ScrollToEnd();
                TxtFullLog.AppendText(line + Environment.NewLine);
                TxtFullLog.ScrollToEnd();
            });
        }

        private void UpdateStatusDot(bool connected)
        {
            StatusDot.Fill = connected
                ? new SolidColorBrush(Colors.LimeGreen)
                : new SolidColorBrush(Colors.Red);
            StatusText.Text = connected ? "已连接" : "Agent 已停止";
            _trayMgr?.UpdateConnectionStatus(connected);
        }

        #endregion

        #region Tab1 主控台

        private async void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_agent?.IsConnected == true)
            {
                AddLog("Agent 已在运行");
                return;
            }

            SyncUIToConfig();
            Core.ConfigManager.Save(_config);

            var agentConfig = new Core.AgentConfig
            {
                ServerUrl = TxtServerUrl.Text.Trim(),
                Token = TxtToken.Password,
                AgentId = TxtAgentId.Text.Trim(),
                HeartbeatIntervalSec = TryParseInt(TxtHeartbeat.Text, 30),
                CommandTimeoutSec = TryParseInt(TxtShellTimeout.Text, 30),
                EnableKeyboard = true,
                EnableMouse = true,
                EnableFileWrite = ChkAllowWrite?.IsChecked ?? false,
                FileReadWhitelist = (TxtWhitelist.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
                ScreenshotFormat = "jpg",
                ReconnectBaseDelaySec = 2,
                ReconnectMaxDelaySec = 60
            };

            _agent = new Core.AgentClient(agentConfig);
            _agent.OnLog += AddLog;
            _agent.OnConnectionChanged += (connected) =>
            {
                Dispatcher.Invoke(() => UpdateStatusDot(connected));
            };

            BtnConnect.IsEnabled = false;
            try
            {
                await Task.Run(async () =>
                {
                    try { await _agent.ConnectWithRetryAsync(); }
                    catch (Exception ex) { AddLog($"连接失败: {ex.Message}"); }
                    finally { Dispatcher.Invoke(() => BtnConnect.IsEnabled = true); }
                });
            }
            catch (Exception ex)
            {
                AddLog($"启动异常: {ex.Message}");
                BtnConnect.IsEnabled = true;
            }
        }

        private async void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            if (_agent != null)
            {
                await _agent.DisconnectAsync();
                _agent = null;
                AddLog("Agent 已停止");
            }
        }

        private void BtnShell_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("执行 CMD 命令", "请输入命令:");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                TxtTestCommand.Text = dlg.InputText;
                RunShellCommand(dlg.InputText, "cmd");
            }
        }

        private void BtnPowershell_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("执行 PowerShell 命令", "请输入命令:");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                TxtTestCommand.Text = dlg.InputText;
                RunShellCommand(dlg.InputText, "powershell");
            }
        }

        private async void RunShellCommand(string cmd, string shell)
        {
            AddLog($"执行 [{shell}]: {cmd}");
            TxtTestResult.Text = "执行中...";
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = shell == "powershell" ? "powershell.exe" : "cmd.exe",
                    Arguments = shell == "powershell" ? $"-NoProfile -Command \"{cmd}\"" : $"/c {cmd}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                using var proc = Process.Start(psi)!;
                var outputTask = proc.StandardOutput.ReadToEndAsync();
                var errorTask = proc.StandardError.ReadToEndAsync();
                var timeoutTask = Task.Delay(30000);
                var completed = await Task.WhenAny(Task.WhenAll(outputTask, errorTask), timeoutTask);
                if (completed == timeoutTask)
                {
                    try { proc.Kill(); } catch { }
                    TxtTestResult.Text = "[ERROR] 命令执行超时";
                    return;
                }
                string output = outputTask.Result;
                string err = errorTask.Result;
                string result = string.IsNullOrEmpty(err) ? output : output + "\n[stderr] " + err;
                TxtTestResult.Text = result;
                AddLog("执行完成");
            }
            catch (Exception ex)
            {
                TxtTestResult.Text = "错误: " + ex.Message;
                AddLog($"执行失败: {ex.Message}");
            }
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                    ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                string path = Path.Combine(Path.GetTempPath(), $"WinRemote_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                TxtTestResult.Text = $"截图已保存到: {path}";
                AddLog($"截图已保存: {path}");
            }
            catch (Exception ex) { AddLog($"截图失败: {ex.Message}"); }
        }

        private void BtnKeypress_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("模拟按键", "请输入要发送的按键组合 (如 ^{ESC}):");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                try
                {
                    System.Windows.Forms.SendKeys.SendWait(dlg.InputText);
                    AddLog($"已发送按键: {dlg.InputText}");
                }
                catch (Exception ex) { AddLog($"发送失败: {ex.Message}"); }
            }
        }

        private void BtnMouse_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int x = System.Windows.Forms.Cursor.Position.X;
                int y = System.Windows.Forms.Cursor.Position.Y;
                Core.AgentClient.mouse_event(Core.AgentClient.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                Core.AgentClient.mouse_event(Core.AgentClient.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                AddLog($"左键点击 @ ({x},{y})");
            }
            catch (Exception ex) { AddLog($"操作失败: {ex.Message}"); }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("打开程序/文件", "请输入程序路径或文件路径:");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(dlg.InputText) { UseShellExecute = true });
                    AddLog($"已启动: {dlg.InputText}");
                }
                catch (Exception ex) { AddLog($"启动失败: {ex.Message}"); }
            }
        }

        private void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("读取文件", "请输入文件路径:");
            dlg.Owner = this;
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.InputText))
            {
                try
                {
                    string content = File.ReadAllText(dlg.InputText);
                    TxtTestResult.Text = content;
                    AddLog($"已读取: {dlg.InputText} ({content.Length} 字节)");
                }
                catch (Exception ex)
                {
                    TxtTestResult.Text = "错误: " + ex.Message;
                    AddLog($"读取失败: {ex.Message}");
                }
            }
        }

        private void BtnWriteFile_Click(object sender, RoutedEventArgs e)
        {
            AddLog("文件写入功能请通过远程连接使用");
        }

        private async void BtnSendTest_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtServerUrl.Text.Trim();
            if (string.IsNullOrEmpty(url)) { AddLog("请输入服务器地址"); return; }
            AddLog($"测试连接: {url}");
            await Task.Run(async () =>
            {
                try
                {
                    using var ws = new System.Net.WebSockets.ClientWebSocket();
                    var cts = new System.Threading.CancellationTokenSource(5000);
                    await ws.ConnectAsync(new Uri(url), cts.Token);
                    Dispatcher.Invoke(() => AddLog("连接成功！服务器可达"));
                    await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "test", System.Threading.CancellationToken.None);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AddLog($"连接失败: {ex.Message}"));
                }
            });
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        #endregion

        #region Tab2 设置

        private void LoadSettingsToUI()
        {
            var c = _config;

            // --- 修复：回填连接核心字段（之前缺失，导致 UI 显示空白）---
            TxtServerUrl.Text = c.ServerUrl;
            TxtToken.Password = c.Token;
            TxtAgentId.Text = c.AgentId;
            // ---

            TxtHeartbeat.Text = c.HeartbeatInterval.ToString();
            TxtShellTimeout.Text = c.ConnectionTimeout.ToString();
            TxtMaxOutput.Text = c.MaxOutputBytes.ToString();
            CmbScreenshotFmt.SelectedIndex = 0;
            
            // 截图设置
            TxtScreenshotQuality.Text = c.ScreenshotQuality > 0 ? c.ScreenshotQuality.ToString() : "80";
            
            // 安全设置
            ChkAllowPowershell.IsChecked = c.AllowPowerShell;
            ChkAllowWrite.IsChecked = c.AllowWrite;
            ChkAutoReconnect.IsChecked = c.AutoReconnect;
            ChkStrictWhitelist.IsChecked = c.StrictWhitelist;
            ChkPasswordGuard.IsChecked = c.PasswordGuardEnabled;
            TxtPasswordGuard.Password = c.PasswordGuard ?? "";
            TxtMaxReadBytes.Text = c.MaxReadBytes > 0 ? c.MaxReadBytes.ToString() : "1048576";
            
            // 路径白名单 / 黑名单
            TxtWhitelist.Text = string.Join(Environment.NewLine, _config.FileReadWhitelist ?? Array.Empty<string>());
            TxtBlacklist.Text = c.BlockedKeywords ?? "";
        }

        private void SyncUIToConfig()
        {
            var c = _config;
            c.ServerUrl = TxtServerUrl.Text.Trim();
            c.Token = TxtToken.Password;
            c.AgentId = TxtAgentId.Text.Trim();
            if (int.TryParse(TxtHeartbeat.Text, out int hb)) c.HeartbeatInterval = hb;
            if (int.TryParse(TxtShellTimeout.Text, out int to)) c.ConnectionTimeout = to;
            if (int.TryParse(TxtMaxOutput.Text, out int mo)) c.MaxOutputBytes = mo;
            if (int.TryParse(TxtScreenshotQuality.Text, out int sq)) c.ScreenshotQuality = sq;
            c.AllowPowerShell = ChkAllowPowershell?.IsChecked ?? true;
            c.AllowWrite = ChkAllowWrite?.IsChecked ?? false;
            c.AutoReconnect = ChkAutoReconnect?.IsChecked ?? true;
            c.StrictWhitelist = ChkStrictWhitelist?.IsChecked ?? false;
            c.PasswordGuardEnabled = ChkPasswordGuard?.IsChecked ?? false;
            c.PasswordGuard = TxtPasswordGuard?.Password ?? "";
            if (int.TryParse(TxtMaxReadBytes?.Text, out int mr)) c.MaxReadBytes = mr;
            c.BlockedKeywords = TxtBlacklist?.Text ?? "";
            c.FileReadWhitelist = (TxtWhitelist?.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            SyncUIToConfig();
            Core.ConfigManager.Save(_config);
            AddLog("设置已保存");
            System.Windows.Forms.MessageBox.Show("设置已保存！", "WinRemote",
                System.Windows.Forms.MessageBoxButtons.OK,
                System.Windows.Forms.MessageBoxIcon.Information);
        }

        #endregion

        #region Tab3 系统服务

        private void RefreshServiceStatus()
        {
            if (_svcMgr == null) return;
            var (running, state) = _svcMgr.GetServiceState();
            ServiceStatusText.Text = state;
            ServiceDot.Fill = running
                ? new SolidColorBrush(Colors.LimeGreen)
                : new SolidColorBrush(Colors.Gray);
            bool nssm = _svcMgr.IsNssmAvailable();
            NssmStatusText.Text = nssm ? "已安装" : "未安装";
        }

        private void BtnSvcInstall_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr!.Install();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSvcUninstall_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr!.Uninstall();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSvcStart_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr!.Start();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSvcStop_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr!.Stop();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSvcRestart_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr!.Restart();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnDownloadNssm_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://nssm.cc/download") { UseShellExecute = true });
                AddLog("已打开 NSSM 下载页面");
            }
            catch { AddLog("无法打开浏览器"); }
        }

        private void BtnViewLogs_Click(object sender, RoutedEventArgs e)
        {
            string log = _svcMgr!.ReadServiceLog();
            TxtServiceLog.Text = log;
        }

        private void BtnClearServiceLog_Click(object sender, RoutedEventArgs e)
        {
            TxtServiceLog.Clear();
        }

        private void BtnRefreshLog_Click(object sender, RoutedEventArgs e)
        {
            TxtFullLog.ScrollToEnd();
        }

        #endregion

        #region 系统信息

        private void RefreshSystemInfo()
        {
            TxtHostname.Text = Environment.MachineName;
            TxtOS.Text = Environment.OSVersion.ToString();
            TxtUsername.Text = Environment.UserName;
            TxtDotNetVer.Text = Environment.Version.ToString();
        }

        #endregion

        #region 托盘回调

        public void TrayConnect() => Dispatcher.Invoke(() => BtnConnect_Click(null!, null!));
        public void TrayDisconnect() => Dispatcher.Invoke(() => BtnDisconnect_Click(null!, null!));
        public void TrayInstallService() => Dispatcher.Invoke(() => BtnSvcInstall_Click(null!, null!));
        public void TrayUninstallService() => Dispatcher.Invoke(() => BtnSvcUninstall_Click(null!, null!));
        public void TrayStartService() => Dispatcher.Invoke(() => BtnSvcStart_Click(null!, null!));
        public void TrayStopService() => Dispatcher.Invoke(() => BtnSvcStop_Click(null!, null!));
        public void TrayServiceStatus() => Dispatcher.Invoke(() =>
        {
            System.Windows.Forms.MessageBox.Show(
                "服务状态请查看「系统服务」选项卡",
                "WinRemote - 服务状态");
        });
        public void TrayCheckUpdate() => Dispatcher.Invoke(() =>
        {
            System.Windows.Forms.MessageBox.Show(
                "WinRemote Agent V1.2\n\n当前已是最新版本。",
                "关于");
        });
        public void TrayRefreshLogs() => Dispatcher.Invoke(() => TxtFullLog.ScrollToEnd());
        public void TrayOpenLogDir() => Dispatcher.Invoke(() =>
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WinRemote", "logs");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            Process.Start("explorer.exe", logDir);
        });

        #endregion

        #region 工具方法

        private static int TryParseInt(string s, int def)
        {
            return int.TryParse(s, out int v) ? v : def;
        }

        #endregion
    }
}
