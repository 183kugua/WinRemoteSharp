using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
        private AgentClient? _agent;
        private readonly Core.Config _config;
        private readonly Core.ServiceManager _svcMgr;
        private TrayManager? _trayMgr;
        private bool _closingToTray = true;

        // 画�刷�缓存（走资源，不�硬编码）
        private System.Windows.Media.Brush? _brushStatusOn;
        private System.Windows.Media.Brush? _brushStatusOff;
        private System.Windows.Media.Brush? _brushStatusErr;
        private System.Windows.Media.Brush? _brushStatusWarn;

        public bool IsConnected => _agent?.IsConnected == true;

        public MainWindow()
        {
            InitializeComponent();
            _config = Core.ConfigManager.Load();
            
            _svcMgr = new Core.ServiceManager();

            // �缓存画�刷
            _brushStatusOn = FindResource("StatusOnBrush") as System.Windows.Media.Brush ?? Brushes.LimeGreen;
            _brushStatusOff = FindResource("StatusOffBrush") as System.Windows.Media.Brush ?? Brushes.Gray;
            _brushStatusErr = FindResource("StatusErrBrush") as System.Windows.Media.Brush ?? Brushes.Red;
            _brushStatusWarn = FindResource("StatusWarnBrush") as System.Windows.Media.Brush ?? Brushes.Orange;

            // 初始化托�盘
            _trayMgr = new TrayManager(this);

            Loaded += MainWindow_Loaded;
            Closed += MainWindow_Closed;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettingsToUI();
            RefreshServiceStatus();
            RefreshDependencies();
            AddLog("��✅ WinRemote Agent V1.2 � 已就�绪");
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_closingToTray)
            {
                e.Cancel = true;
                Hide();
                _trayMgr?.ShowBalloonTip("WinRemote Agent", "已最小化到托�盘，双击图标显示�窗口", System.Windows.Forms.ToolTipIcon.Info);
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
                _trayMgr?.ShowBalloonTip("WinRemote Agent", "已最小化到托�盘，双击图标显示�窗口", System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        #region 公共日志方法（供 TrayManager �� 调用）

        public void AddLog(string msg)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {msg}";
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText(line + Environment.NewLine);
                TxtLog.ScrollToEnd();
                TxtAllLog.AppendText(line + Environment.NewLine);
                TxtAllLog.ScrollToEnd();
            });
        }

        #endregion

        #region 托�盘菜单回调方法

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
        {
            if (_agent != null && _agent.IsConnected)
            {
                AddLog("��⚠��️ Agent � 已在运行");
                return;
            }

            SyncUIToConfig();
            Core.ConfigManager.Save(_config);

            _agent = new AgentClient(_config);
            _agent.OnLog += AddLog;
            _agent.OnConnectionChanged += (connected) =>
            {
                Dispatcher.Invoke(() =>
                {
                    var brush = connected ? _brushStatusOn : _brushStatusOff;
                    if (DotConnection != null) DotConnection.Fill = brush;
                    TxtConnection.Text = connected ? "● � 已连接" : "● 未连接";
                    TxtStatusDetail.Text = connected ? "已连接" : "未连接";
                    if (DotStatusDetail != null) DotStatusDetail.Fill = brush;
                    TxtAgentState.Text = connected ? "Agent 运行中" : "Agent � 已停止";
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
                    catch (Exception ex) { AddLog($"��❌ 连接失败: {ex.Message}"); }
                    finally { Dispatcher.Invoke(() => BtnStart.IsEnabled = true); }
                });
            }
            catch (Exception ex)
            {
                AddLog($"��❌ 启动�异常: {ex.Message}");
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
                AddLog("��⏹ Agent � 已停止");
            }
            TxtStatus.Text = "就�绪";
        }

        private async void BtnTest_Click(object sender, RoutedEventArgs e)
        {
            string url = TxtServer.Text.Trim();
            if (string.IsNullOrEmpty(url)) { AddLog("��⚠��️ 请输入服务器地�址"); return; }
            AddLog($"���🔌 � 测试连接: {url}");
            try
            {
                using var ws = new System.Net.WebSockets.ClientWebSocket();
                var cts = new System.Threading.CancellationTokenSource(5000);
                await ws.ConnectAsync(new Uri(url), cts.Token);
                AddLog("��✅ 连接成功！服务器可达");
                await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "test", System.Threading.CancellationToken.None);
            }
            catch (Exception ex)
            {
                AddLog($"��❌ 连接失败: {ex.Message}");
            }
        }

        private void BtnGenToken_Click(object sender, RoutedEventArgs e)
        {
            var bytes = new byte[32];
            using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            string token = Convert.ToBase64String(bytes).Replace("+", "").Replace("/", "").Replace("=", "").Substring(0, 32);
            TxtToken.Password = token;
            AddLog($"���🎲 � 已生成随机令牌");
        }

        private void BtnLogClear_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            _closingToTray = false;
            _ = _agent?.DisconnectAsync();
            Close();
        }

        #endregion

        #region Tab2 设置

        private void LoadSettingsToUI()
        {
            var c = _config;
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
            var c = _config;
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
            Core.ConfigManager.Save(_config);
            
            // 如果 Agent 正在运行，更新其配置
            if (_agent != null)
            {
                _agent.UpdateConfig(_config);
                AddLog("���💾 设置已保存并应用到运行中的 Agent");
            }
            else
            {
                AddLog("���💾 设置已保存");
            }
            MessageBox.Show("设置已保存！", "WinRemote", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            Core.ConfigManager.ApplyDefaults(_config);
            LoadSettingsToUI();
            AddLog("���🔄 � 已�恢复默认设置");
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
            TxtNssmStatus.Text = nssm ? "��✅ � 已安装" : "��❌ 未安装";
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
                    Text = (ok ? "��✅ " : "��❌ ") + name,
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
            if (MessageBox.Show("确定要�卸载服务吗？", "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
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
                AddLog($"���💾 日志已保存: {dlg.FileName}");
            }
        }

        #endregion

        #region Tab5 � 工具�箱

        private void BtnRunCmd_Click(object sender, RoutedEventArgs e)
        {
            string cmd = TxtCmd.Text.Trim();
            if (string.IsNullOrEmpty(cmd)) return;
            string shell = (CmbShell.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "CMD";
            AddLog($"�▶ �执行 [{shell}]: {cmd}");
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
                    proc.WaitForExit(_config.CommandTimeoutSec * 1000);
                    string result = string.IsNullOrEmpty(err) ? output : output + "\n[stderr] " + err;
                    Dispatcher.Invoke(() => { TxtResult.Text = result; });
                    AddLog($"��✅ �执行完成 ({result.Length} 字节)");
                }
                catch (Exception ex) { AddLog($"��❌ �执行失败: {ex.Message}"); }
            });
        }

        private void BtnSendKeys_Click(object sender, RoutedEventArgs e)
        {
            if (!_config.EnableKeyboard) { AddLog("��⚠��️ �� 键�盘模�拟未启用"); return; }
            string keys = TxtSendKeys.Text;
            try
            {
                System.Windows.Forms.SendKeys.SendWait(keys);
                AddLog($"��⌨��️ � 已发送按�键: {keys}");
            }
            catch (Exception ex) { AddLog($"��❌ 发送失败: {ex.Message}"); }
        }

        private void BtnMouseClick_Click(object sender, RoutedEventArgs e)
        {
            if (!_config.EnableMouse) { AddLog("��⚠��️ �� 鼠标模�拟未启用"); return; }
            if (int.TryParse(TxtMouseX.Text, out int x) && int.TryParse(TxtMouseY.Text, out int y))
            {
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point(x, y);
                AgentClient.mouse_event(AgentClient.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                AgentClient.mouse_event(AgentClient.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                AddLog($"���🖱 � 左�键点击 @ ({x},{y})");
            }
        }

        private void BtnMouseRight_Click(object sender, RoutedEventArgs e)
        {
            if (!_config.EnableMouse) { AddLog("��⚠��️ �� 鼠标模�拟未启用"); return; }
            AgentClient.mouse_event(AgentClient.MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
            AgentClient.mouse_event(AgentClient.MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
            AddLog("���🖱 右�键点击");
        }

        private void BtnReadFile_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtReadFile.Text.Trim();
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (!_config.FileReadWhitelist.Any(w => Path.GetFullPath(path).StartsWith(Path.GetFullPath(w), StringComparison.OrdinalIgnoreCase)))
                {
                    AddLog("��⚠��️ � 路径不在白名单内");
                    return;
                }
                string content = File.ReadAllText(path);
                TxtResult.Text = content;
                AddLog($"���📄 � 已读取: {path} ({content.Length} 字节)");
            }
            catch (Exception ex) { AddLog($"��❌ 读取失败: {ex.Message}"); }
        }

        private void BtnOpenProg_Click(object sender, RoutedEventArgs e)
        {
            string prog = TxtOpenProg.Text.Trim();
            try
            {
                Process.Start(new ProcessStartInfo(prog) { UseShellExecute = true });
                AddLog($"���🚀 � 已启动: {prog}");
            }
            catch (Exception ex) { AddLog($"��❌ 启动失败: {ex.Message}"); }
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
                AddLog($"��🔔 通知已发送: {title}");
            }
            catch (Exception ex) { AddLog($"��❌ 通知失败: {ex.Message}"); }
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds ?? new System.Drawing.Rectangle(0, 0, 1920, 1080);
                using var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                    g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
                string path = Path.Combine(Path.GetTempPath(), $"WinRemote_Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                TxtResult.Text = $"截图已保存到: {path}";
                AddLog($"���📸 截图已保存: {path}");
            }
            catch (Exception ex) { AddLog($"��❌ 截图失败: {ex.Message}"); }
        }

        #endregion
    }

    // AgentClient 的�鼠标 P/Invoke �� 暴�露为静态方法，方便 UI �� 调用
    public partial class AgentClient
    {
        public const int MOUSEEVENTF_LEFTDOWN = 0x02;
        public const int MOUSEEVENTF_LEFTUP = 0x04;
        public const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const int MOUSEEVENTF_RIGHTUP = 0x10;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
    }
}