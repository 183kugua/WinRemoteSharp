using System;
<<<<<<< HEAD
using System.ComponentModel;
=======
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
<<<<<<< HEAD
using System.Windows.Media.Animation;
=======
using System.Windows.Media.Imaging;
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
<<<<<<< HEAD
        private AgentClient _agent;
        private Config _config;
        private bool _isConnected = false;
        private TrayManager _trayManager;
        public bool _closingToTray = true; // true = 最小化到托盘，false = 真正退出
=======
        private AgentClient? _agent;
        private readonly Core.ConfigManager _cfgMgr;
        private readonly Core.ServiceManager _svcMgr;
        private TrayManager? _trayMgr;
        private bool _closingToTray = true;
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)

        public bool IsConnected => _isConnected;

        public bool IsConnected => _agent?.IsConnected == true;

        public MainWindow()
        {
<<<<<<< HEAD
            try
            {
                InitializeComponent();
            }
            catch (Exception ex)
            {
                LogCrash(ex, "MainWindow.InitializeComponent");
                throw;
            }
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
        }

        private void LogCrash(Exception ex, string where)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup_crash.log");
                string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                string line = "[" + time + "] [" + where + "]\n" + ex.ToString() + "\n\n";
                File.AppendAllText(path, line);
            }
            catch { /* ignore */ }
=======
            InitializeComponent();
            _cfgMgr = new Core.ConfigManager();
            _cfgMgr.Load();
            _svcMgr = new Core.ServiceManager();

            // 缓存画刷
            _brushStatusOn = FindResource("StatusOnBrush") as MediaBrush ?? Brushes.LimeGreen;
            _brushStatusOff = FindResource("StatusOffBrush") as MediaBrush ?? Brushes.Gray;
            _brushStatusErr = FindResource("StatusErrBrush") as MediaBrush ?? Brushes.Red;
            _brushStatusWarn = FindResource("StatusWarnBrush") as MediaBrush ?? Brushes.Orange;

            // 初始化托盘
            _trayMgr = new TrayManager(this);

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
<<<<<<< HEAD
            try
=======
            LoadSettingsToUI();
            RefreshServiceStatus();
            RefreshDependencies();
            AddLog("✅ WinRemote Agent V1.2 已就绪");
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
                _ = _agent?.DisconnectAsync();
                _trayMgr?.Dispose();
            }
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            _ = _agent?.DisconnectAsync();
            _trayMgr?.Dispose();
            _svcMgr?.Dispose();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                _trayMgr?.ShowBalloonTip("WinRemote Agent", "已最小化到托盘，双击图标显示窗口", System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        #region 公共日志方法（供 TrayManager 调用）

        public void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Dispatcher.Invoke(() =>
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
            {
                _config = ConfigManager.Load("config.json");
                ApplyConfigToUI();
                UpdateFooterTime();
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (s, ev) => UpdateFooterTime();
                timer.Start();

                // 初始化系统托盘
                _trayManager = new TrayManager(this);
                _trayManager.UpdateConnectionStatus(_isConnected);

                // 如果配置了开机自启且当前是最小化启动，检查是否需要自动连接
                if (_config.AutoStart && !IsVisible)
                {
                    AddLog("AutoStart enabled, attempting to connect...");
                    // 这里可以添加自动连接逻辑
                }
            }
            catch (Exception ex)
            {
                LogCrash(ex, "MainWindow_Loaded");
                throw;
            }
        }

<<<<<<< HEAD
        private void MainWindow_Closing(object sender, CancelEventArgs e)
=======
        #endregion

        #region 托盘菜单回调方法

        public void TrayConnect()
        {
            Dispatcher.Invoke(async () => await BtnStart_Click(null!, null!));
        }

        public void TrayDisconnect()
        {
            Dispatcher.Invoke(async () => await BtnStop_Click(null!, null!));
        }

        public void TrayInstallService()
        {
            Dispatcher.Invoke(() => BtnSrvInstall_Click(null!, null!));
        }

        public void TrayUninstallService()
        {
            Dispatcher.Invoke(() => BtnSrvUninstall_Click(null!, null!));
        }

        public void TrayStartService()
        {
            Dispatcher.Invoke(() => BtnSrvStart_Click(null!, null!));
        }

        public void TrayStopService()
        {
            Dispatcher.Invoke(() => BtnSrvStop_Click(null!, null!));
        }

        public void TrayServiceStatus()
        {
            Dispatcher.Invoke(() =>
            {
                var (running, state) = _svcMgr.GetServiceState();
                MessageBox.Show($"服务状态: {state}\nNSSM: {(_svcMgr.IsNssmAvailable() ? "已安装" : "未安装")}", "WinRemote - 服务状态");
            });
        }

        public void TrayCheckUpdate()
        {
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show("WinRemote Agent V1.2\n\n当前已是最新版本。", "关于", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }

        public void TrayRefreshLogs()
        {
            Dispatcher.Invoke(() => TxtAllLog.ScrollToEnd());
        }

        public void TrayOpenLogDir()
        {
            Dispatcher.Invoke(() =>
            {
                string logDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "WinRemote", "logs");
                if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
                Process.Start("explorer.exe", logDir);
            });
        }

        #endregion

        #region Tab1 主控台

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
        {
            if (_closingToTray)
            {
<<<<<<< HEAD
                // 最小化到托盘而不是退出
                e.Cancel = true;
                Hide();
                _trayManager?.ShowBalloonTip("WinRemote Agent", "已最小化到系统托盘，双击图标可显示窗口", System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                // 真正退出：断开连接并清理
                if (_agent != null && _agent.IsConnected())
                {
                    _agent.Disconnect();
                }
                _trayManager?.Dispose();
            }
        }

        private void ApplyConfigToUI()
        {
            TxtServerUrl.Text = _config.ServerUrl;
            TxtShellTimeout.Text = _config.ConnectionTimeout.ToString();
            TxtHeartbeat.Text = _config.HeartbeatInterval.ToString();
            TxtScreenshotQuality.Text = _config.ScreenshotQuality.ToString();
            TxtWhitelist.Text = _config.AllowedIPs;
            TxtToken.Password = _config.Token ?? "";
            TxtMaxOutput.Text = _config.MaxOutputBytes.ToString();
            TxtMaxReadBytes.Text = _config.MaxReadBytes.ToString();
            TxtBlacklist.Text = _config.BlockedKeywords;
            ChkAllowPowershell.IsChecked = _config.AllowPowerShell;
            ChkAllowWrite.IsChecked = _config.AllowWrite;
            ChkAutoReconnect.IsChecked = _config.AutoReconnect;
            ChkStrictWhitelist.IsChecked = _config.StrictWhitelist;
            ChkPasswordGuard.IsChecked = _config.PasswordGuardEnabled;
            TxtPasswordGuard.Password = _config.PasswordGuard ?? "";
        }

        private void UpdateFooterTime()
        {
            StatusMessage.Text = "就绪 · " + DateTime.Now.ToString("HH:mm:ss");
        }

        public void AddLog(string msg)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            TxtLog.AppendText("[" + ts + "] " + msg + "\n");
            TxtLog.ScrollToEnd();
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtServerUrl.Text.Trim();
            string token = TxtToken.Password;
            if (string.IsNullOrEmpty(url))
            {
                AddLog("错误：服务器地址不能为空");
                return;
            }
            _agent = new AgentClient(_config);
            _agent.OnLog += (s, m) => Dispatcher.Invoke(() => AddLog(m));
            _agent.OnStatusChanged += (s, connected) => Dispatcher.Invoke(() => UpdateConnectionUI(connected));
            _agent.OnAgentIdReceived += (s, id) => Dispatcher.Invoke(() => TxtAgentId.Text = id);
            try
            {
                _agent.ConnectAsync(url, token);
                AddLog("正在连接 " + url + " ...");
            }
            catch (Exception ex)
            {
                AddLog("连接失败：" + ex.Message);
=======
                AddLog("⚠️ Agent 已在运行");
                return;
            }

            SyncUIToConfig();
            _cfgMgr.Save();

            _agent = new AgentClient(_cfgMgr.Config);
            _agent.OnLog += AddLog;
            _agent.OnConnectionChanged += (connected) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var brush = connected ? _brushStatusOn : _brushStatusOff;
                    if (DotConnection != null) DotConnection.Fill = brush;
                    TxtConnection.Text = connected ? "● 已连接" : "● 未连接";
                    TxtStatusDetail.Text = connected ? "已连接" : "未连接";
                    if (DotStatusDetail != null) DotStatusDetail.Fill = brush;
                    TxtAgentState.Text = connected ? "Agent 运行中" : "Agent 已停止";
                    if (DotAgent != null) DotAgent.Fill = connected ? _brushStatusOn : _brushStatusOff;
                });
            };

            BtnStart.IsEnabled = false;
            TxtStatus.Text = "正在连接...";
            try
            {
                _ = Task.Run(async () =>
                {
                    try { await _agent.ConnectWithRetryAsync(); }
                    catch (Exception ex) { AddLog($"❌ 连接失败: {ex.Message}"); }
                    finally { Dispatcher.Invoke(() => BtnStart.IsEnabled = true); }
                });
            }
            catch (Exception ex)
            {
                AddLog($"❌ 启动异常: {ex.Message}");
                BtnStart.IsEnabled = true;
            }
            await Task.CompletedTask;
        }

        private async void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (_agent != null)
            {
                await _agent.DisconnectAsync();
                _agent = null;
                AddLog("⏹ Agent 已停止");
            }
            TxtStatus.Text = "就绪";
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtServer.Text.Trim();
            if (string.IsNullOrEmpty(url)) { AddLog("⚠️ 请输入服务器地址"); return; }
            AddLog($"🔌 测试连接: {url}");
            try
            {
                using var ws = new System.Net.WebSockets.ClientWebSocket();
                var cts = new System.Threading.CancellationTokenSource(5000);
                await ws.ConnectAsync(new Uri(url), cts.Token);
                AddLog("✅ 连接成功！服务器可达");
                await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "test", System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                AddLog($"❌ 连接失败: {ex.Message}");
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
<<<<<<< HEAD
            _agent?.Disconnect();
            AddLog("已断开连接");
=======
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            string token = Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 32);
            TxtToken.Password = token;
            AddLog($"🎲 已生成随机令牌");
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
        }

        private void UpdateConnectionUI(bool connected)
        {
            _isConnected = connected;
            StatusDot.Fill = connected ? (System.Windows.Media.Brush)FindResource("SuccessBrush") : (System.Windows.Media.Brush)FindResource("ErrorBrush");
            StatusText.Text = connected ? "Agent 运行中" : "Agent 已停止";
            BtnConnect.IsEnabled = !connected;
            BtnDisconnect.IsEnabled = connected;
            _trayManager?.UpdateConnectionStatus(connected);
        }

        private void BtnShell_Click(object sender, RoutedEventArgs e)
        {
<<<<<<< HEAD
            var dlg = new InputDialog("执行命令", "请输入命令（如：ipconfig /all）", "cmd /c ipconfig");
            if (dlg.ShowDialog() == true) SendCommand("shell", dlg.Result);
=======
            _closingToTray = false;
            _ = _agent?.DisconnectAsync();
            Close();
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
        }

        private void BtnPowershell_Click(object sender, RoutedEventArgs e)
        {
<<<<<<< HEAD
            var dlg = new InputDialog("PowerShell 指令", "请输入 PowerShell 指令", "Get-Process");
            if (dlg.ShowDialog() == true) SendCommand("powershell", dlg.Result);
=======
            var c = _cfgMgr.Config;
            SetServer.Text = c.ServerUrl;
            SetHeartbeat.Text = c.HeartbeatIntervalSec.ToString();
            SetTimeout.Text = c.CommandTimeoutSec.ToString();
            SetScreenshotFmt.SelectedIndex = c.ScreenshotFormat == "jpg" ? 1 : 0;
            ChkKeyboard.IsChecked = c.EnableKeyboard;
            ChkFileWrite.IsChecked = c.EnableFileWrite;
            TxtWhitelist.Text = string.Join(Environment.NewLine, c.FileReadWhitelist ?? Array.Empty<string>());
            TxtServer.Text = c.ServerUrl;
            TxtAgentId.Text = c.AgentId;
        }

        private void SyncUIToConfig()
        {
            var c = _cfgMgr.Config;
            c.ServerUrl = TxtServer.Text.Trim();
            c.AgentId = TxtAgentId.Text.Trim();
            c.Token = TxtToken.Password;
            if (int.TryParse(SetHeartbeat.Text, out int hb)) c.HeartbeatIntervalSec = hb;
            if (int.TryParse(SetTimeout.Text, out int to)) c.CommandTimeoutSec = to;
            c.ScreenshotFormat = (SetScreenshotFmt.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "png";
            c.EnableKeyboard = ChkKeyboard.IsChecked ?? true;
            c.EnableMouse = ChkKeyboard.IsChecked ?? true;
            c.EnableFileWrite = ChkFileWrite.IsChecked ?? false;
            c.FileReadWhitelist = TxtWhitelist.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SyncUIToConfig();
            _cfgMgr.Save();
            
            // 如果 Agent 正在运行，更新其配置
            if (_agent != null)
            {
                _agent.UpdateConfig(_cfgMgr.Config);
                AddLog("💾 设置已保存并应用到运行中的 Agent");
            }
            else
            {
                AddLog("💾 设置已保存");
            }
            MessageBox.Show("设置已保存！", "WinRemote", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            _cfgMgr.ApplyDefaults();
            LoadSettingsToUI();
            AddLog("🔄 已恢复默认设置");
        }

        #endregion

        #region Tab3 系统服务

        private void RefreshServiceStatus()
        {
            var (running, state) = _svcMgr.GetServiceState();
            TxtServiceState.Text = state;
            if (DotService != null)
                DotService.Fill = running ? _brushStatusOn : _brushStatusOff;

            bool nssm = _svcMgr.IsNssmAvailable();
            TxtNssmStatus.Text = nssm ? "✅ 已安装" : "❌ 未安装";
        }

        private void RefreshDependencies()
        {
            GrdDeps.Children.Clear();
            var deps = new[]
            {
                ("WinRemoteAgent.exe", File.Exists("WinRemoteAgent.exe")),
                ("nssm.exe", _svcMgr.IsNssmAvailable()),
                ("System.Text.Json 8.0.5", true),
                (".NET 8 Runtime", true),
                ("管理员权限", IsAdmin()),
            };
            int col = 0;
            foreach (var (name, ok) in deps)
            {
                var tb = new TextBlock
                {
                    Text = (ok ? "✅ " : "❌ ") + name,
                    Foreground = ok ? _brushStatusOn : _brushStatusErr,
                    FontFamily = new System.Windows.Media.FontFamily("Microsoft YaHei UI"),
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 16, 4)
                };
                Grid.SetColumn(tb, col % 3);
                GrdDeps.Children.Add(tb);
                col++;
            }
        }

        private bool IsAdmin()
        {
            try
            {
                var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(id);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void BtnSrvInstall_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr.Install();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSrvStart_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr.Start();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSrvStop_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr.Stop();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSrvRestart_Click(object sender, RoutedEventArgs e)
        {
            string result = _svcMgr.Restart();
            AddLog(result);
            RefreshServiceStatus();
        }

        private void BtnSrvUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("确定要卸载服务吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                string result = _svcMgr.Uninstall();
                AddLog(result);
                RefreshServiceStatus();
            }
        }

        private void BtnViewSvcLog_Click(object sender, RoutedEventArgs e)
        {
            string log = _svcMgr.ReadServiceLog();
            TxtSvcLog.Text = log;
        }

        private void BtnSvcLogClear_Click(object sender, RoutedEventArgs e)
        {
            TxtSvcLog.Clear();
        }

        #endregion

        #region Tab4 运行日志

        private void BtnLogRefresh_Click(object sender, RoutedEventArgs e)
        {
            TxtAllLog.ScrollToEnd();
        }

        private void BtnLogSave_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "文本文件|*.txt|所有文件|*.*",
                FileName = $"WinRemote_Log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };
            if (dlg.ShowDialog() == true)
            {
                File.WriteAllText(dlg.FileName, TxtAllLog.Text);
                AddLog($"💾 日志已保存: {dlg.FileName}");
            }
        }

        #endregion

        #region Tab5 工具箱

        private void BtnRunCmd_Click(object sender, RoutedEventArgs e)
        {
            string cmd = TxtCmd.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            string shell = (CmbShell.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "CMD";
            AddLog($"▶ 执行 [{shell}]: {cmd}");
            _ = Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = shell == "PowerShell" ? "powershell.exe" : "cmd.exe",
                        Arguments = shell == "PowerShell" ? $"-NoProfile -Command \"{cmd}\"" : $"/c {cmd}",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi)!;
                    string output = proc.StandardOutput.ReadToEnd();
                    string err = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(_cfgMgr.Config.CommandTimeoutSec * 1000);
                    string result = string.IsNullOrEmpty(err) ? output : output + "\n[stderr] " + err;
                    Dispatcher.Invoke(() => { TxtResult.Text = result; });
                    AddLog($"✅ 执行完成 ({result.Length} 字节)");
                }
                catch (Exception ex) { AddLog($"❌ 执行失败: {ex.Message}"); }
            });
        }

        private void BtnSendKeys_Click(object sender, RoutedEventArgs e)
        {
            if (!_cfgMgr.Config.EnableKeyboard) { AddLog("⚠️ 键盘模拟未启用"); return; }
            string keys = TxtSendKeys.Text;
            try
            {
                System.Windows.Forms.SendKeys.SendWait(keys);
                AddLog($"⌨️ 已发送按键: {keys}");
            }
            catch (Exception ex) { AddLog($"❌ 发送失败: {ex.Message}"); }
        }

        private void BtnMouseClick_Click(object sender, RoutedEventArgs e)
        {
            if (!_cfgMgr.Config.EnableMouse) { AddLog("⚠️ 鼠标模拟未启用"); return; }
            if (int.TryParse(TxtMouseX.Text, out int x) && int.TryParse(TxtMouseY.Text, out int y))
            {
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
                AgentClient.mouse_event(AgentClient.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                AgentClient.mouse_event(AgentClient.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                AddLog($"🖱 左键点击 @ ({x},{y})");
            }
        }

        private void BtnMouseRight_Click(object sender, RoutedEventArgs e)
        {
            if (!_cfgMgr.Config.EnableMouse) { AddLog("⚠️ 鼠标模拟未启用"); return; }
            AgentClient.mouse_event(AgentClient.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            AgentClient.mouse_event(AgentClient.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            AddLog("🖱 右键点击");
        }

        private void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtReadFile.Text.Trim();
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!_cfgMgr.Config.FileReadWhitelist.Any(w => Path.GetFullPath(path).StartsWith(Path.GetFullPath(w), StringComparison.OrdinalIgnoreCase)))
                {
                    AddLog("⚠️ 路径不在白名单内");
                    return;
                }
                string content = File.ReadAllText(path);
                TxtResult.Text = content;
                AddLog($"📄 已读取: {path} ({content.Length} 字节)");
            }
            catch (Exception ex) { AddLog($"❌ 读取失败: {ex.Message}"); }
        }

        private void BtnOpenProg_Click(object sender, RoutedEventArgs e)
        {
            string prog = TxtOpenProg.Text.Trim();
            try
            {
                Process.Start(new ProcessStartInfo(prog) { UseShellExecute = true });
                AddLog($"🚀 已启动: {prog}");
            }
            catch (Exception ex) { AddLog($"❌ 启动失败: {ex.Message}"); }
        }

        private void BtnNotify_Click(object sender, RoutedEventArgs e)
        {
            string title = TxtNotifyTitle.Text;
            string text = TxtNotifyText.Text;
            try
            {
                var t = new System.Threading.Thread(() =>
                {
                    System.Windows.Forms.MessageBox.Show(text, title, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                });
                t.SetApartmentState(System.Threading.ApartmentState.STA);
                t.Start();
                AddLog($"🔔 通知已发送: {title}");
            }
            catch (Exception ex) { AddLog($"❌ 通知失败: {ex.Message}"); }
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
<<<<<<< HEAD
            SendCommand("screenshot", "");
=======
            try
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                string path = Path.Combine(Path.GetTempPath(), $"WinRemote_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                TxtResult.Text = $"截图已保存到: {path}";
                AddLog($"📸 截图已保存: {path}");
            }
            catch (Exception ex) { AddLog($"❌ 截图失败: {ex.Message}"); }
>>>>>>> 598c82d (fix: 全面修复客户端代码 Bug)
        }

        private void BtnKeypress_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("模拟按键", "键名（如：Enter, Ctrl+C, Win+R）", "Enter");
            if (dlg.ShowDialog() == true) SendCommand("keypress", dlg.Result);
        }

        private void BtnMouse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("鼠标操作", "格式：x,y,action (如：500,300,click)", "500,300,click");
            if (dlg.ShowDialog() == true) SendCommand("mouse", dlg.Result);
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("打开程序", "程序路径或命令（如：notepad.exe）", "notepad.exe");
            if (dlg.ShowDialog() == true) SendCommand("open", dlg.Result);
        }

        private void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("读取文件", "文件完整路径", "C:\\Windows\\System32\\drivers\\etc\\hosts");
            if (dlg.ShowDialog() == true) SendCommand("readfile", dlg.Result);
        }

        private void BtnWriteFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("写入文件", "格式：路径|内容", "C:\\test.txt|Hello WinRemote");
            if (dlg.ShowDialog() == true) SendCommand("writefile", dlg.Result);
        }

        private void SendCommand(string type, string payload)
        {
            if (_agent == null || !_agent.IsConnected()) { AddLog("未连接"); return; }
            _agent.SendCommand(type, payload);
            AddLog("已发送 [" + type + "]：" + payload);
        }

        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        private void BtnSaveConfig_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _config.ServerUrl = TxtServerUrl.Text.Trim();
                _config.ConnectionTimeout = int.Parse(TxtShellTimeout.Text);
                _config.HeartbeatInterval = int.Parse(TxtHeartbeat.Text);
                _config.ScreenshotQuality = int.Parse(TxtScreenshotQuality.Text);
                _config.AllowedIPs = TxtWhitelist.Text;
                _config.Token = TxtToken.Password;
                _config.MaxOutputBytes = long.Parse(TxtMaxOutput.Text);
                _config.MaxReadBytes = long.Parse(TxtMaxReadBytes.Text);
                _config.BlockedKeywords = TxtBlacklist.Text;
                _config.AllowPowerShell = ChkAllowPowershell.IsChecked == true;
                _config.AllowWrite = ChkAllowWrite.IsChecked == true;
                _config.AutoReconnect = ChkAutoReconnect.IsChecked == true;
                _config.StrictWhitelist = ChkStrictWhitelist.IsChecked == true;
                _config.PasswordGuardEnabled = ChkPasswordGuard.IsChecked == true;
                _config.PasswordGuard = TxtPasswordGuard.Password;
                ConfigManager.Save(_config, "config.json");
                AddLog("配置已保存");
                if (_agent != null) _agent.UpdateConfig(_config);
            }
            catch (Exception ex)
            {
                AddLog("保存失败：" + ex.Message);
            }
        }

        // ========== 托盘菜单调用的方法 ==========

        public void TrayConnect()
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isConnected)
                {
                    BtnConnect_Click(this, new RoutedEventArgs());
                }
            });
        }

        public void TrayDisconnect()
        {
            Dispatcher.Invoke(() =>
            {
                if (_isConnected)
                {
                    BtnDisconnect_Click(this, new RoutedEventArgs());
                }
            });
        }

        public void TrayInstallService()
        {
            Dispatcher.Invoke(() => RunNssm("install"));
        }

        public void TrayUninstallService()
        {
            Dispatcher.Invoke(() => RunNssm("uninstall"));
        }

        public void TrayStartService()
        {
            Dispatcher.Invoke(() => RunNssm("start"));
        }

        public void TrayStopService()
        {
            Dispatcher.Invoke(() => RunNssm("stop"));
        }

        public void TrayServiceStatus()
        {
            Dispatcher.Invoke(() =>
            {
                RefreshServiceStatus();
                _trayManager?.ShowBalloonTip("服务状态", ServiceStatusText.Text, System.Windows.Forms.ToolTipIcon.Info);
            });
        }

        public void TrayCheckUpdate()
        {
            Dispatcher.Invoke(() =>
            {
                _trayManager?.ShowBalloonTip("关于", "WinRemote Agent V1.2\nC# WPF 中文版\n与 AstrBot astrbot_plugin_winremote 协议兼容", System.Windows.Forms.ToolTipIcon.Info);
            });
        }

        public void TrayRefreshLogs()
        {
            Dispatcher.Invoke(() =>
            {
                var sm = new ServiceManager("config.json");
                TxtFullLog.Text = sm.GetRecentLogs(200);
                LogStatusText.Text = "已刷新 · " + DateTime.Now.ToString("HH:mm:ss");
                _trayManager?.ShowBalloonTip("日志已刷新", "已获取最新 200 行服务日志", System.Windows.Forms.ToolTipIcon.Info);
            });
        }

        public void TrayOpenLogDir()
        {
            Dispatcher.Invoke(() =>
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (Directory.Exists(logDir))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = logDir,
                        UseShellExecute = true
                    });
                }
                else
                {
                    _trayManager?.ShowBalloonTip("日志目录", "日志目录不存在：" + logDir, System.Windows.Forms.ToolTipIcon.Warning);
                }
            });
        }

        private void BtnSvcStart_Click(object sender, RoutedEventArgs e) { RunNssm("start"); }
        private void BtnSvcStop_Click(object sender, RoutedEventArgs e) { RunNssm("stop"); }
        private void BtnSvcRestart_Click(object sender, RoutedEventArgs e) { RunNssm("restart"); }
        private void BtnSvcInstall_Click(object sender, RoutedEventArgs e) { RunNssm("install"); }
        private void BtnSvcUninstall_Click(object sender, RoutedEventArgs e) { RunNssm("uninstall"); }

        private void RunNssm(string action)
        {
            AddLog("NSSM " + action + " ...");
            var sm = new ServiceManager("config.json");
            bool ok = false;
            switch (action)
            {
                case "start": ok = sm.Start(); break;
                case "stop": ok = sm.Stop(); break;
                case "restart": ok = sm.Stop() && sm.Start(); break;
                case "install": ok = sm.Install(); break;
                case "uninstall": ok = sm.Uninstall(); break;
            }
            AddLog("NSSM " + action + (ok ? " 成功" : " 失败"));
            RefreshServiceStatus();
        }

        private void RefreshServiceStatus()
        {
            var sm = new ServiceManager("config.json");
            var status = sm.GetStatus();
            ServiceStatusText.Text = status;
            ServiceDot.Fill = status.Contains("Running") ? (System.Windows.Media.Brush)FindResource("SuccessBrush") : (System.Windows.Media.Brush)FindResource("ErrorBrush");
        }

        private void BtnDownloadNssm_Click(object sender, RoutedEventArgs e)
        {
            AddLog("正在下载 NSSM ...");
            var sm = new ServiceManager("config.json");
            if (sm.EnsureNssm()) { AddLog("NSSM 已就绪"); NssmStatusText.Text = "NSSM 状态：已就绪"; }
            else { AddLog("NSSM 下载失败"); NssmStatusText.Text = "NSSM 状态：下载失败"; }
        }

        private void BtnViewLogs_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager("config.json");
            TxtServiceLog.Text = sm.GetRecentLogs(50);
        }

        private void BtnRefreshLog_Click(object sender, RoutedEventArgs e)
        {
            var sm = new ServiceManager("config.json");
            TxtFullLog.Text = sm.GetRecentLogs(200);
            LogStatusText.Text = "已刷新 · " + DateTime.Now.ToString("HH:mm:ss");
        }

        private void BtnClearServiceLog_Click(object sender, RoutedEventArgs e)
        {
            TxtFullLog.Clear();
            LogStatusText.Text = "已清空";
        }

        private void BtnSendTest_Click(object sender, RoutedEventArgs e)
        {
            if (_agent == null || !_agent.IsConnected()) { TxtTestResult.Text = "未连接"; return; }
            _agent.SendCommand("shell", TxtTestCommand.Text);
            TxtTestResult.Text = "已发送测试指令：" + TxtTestCommand.Text;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshServiceStatus();
            var sm = new ServiceManager("config.json");
            NssmStatusText.Text = sm.NssmExists() ? "NSSM 状态：已就绪" : "NSSM 状态：未下载";
        }
    }
}