using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using WinRemoteSharp.Core;

namespace WinRemoteSharp
{
    public partial class MainWindow : Window
    {
        private AgentClient _agent;
        private Config _config;
        private bool _isConnected = false;
        private TrayManager _trayManager;
        public bool _closingToTray = true; // true = 最小化到托盘，false = 真正退出

        public bool IsConnected => _isConnected;

        public MainWindow()
        {
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
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
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

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (_closingToTray)
            {
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
            }
        }

        private void BtnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            _agent?.Disconnect();
            AddLog("已断开连接");
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
            var dlg = new InputDialog("执行命令", "请输入命令（如：ipconfig /all）", "cmd /c ipconfig");
            if (dlg.ShowDialog() == true) SendCommand("shell", dlg.Result);
        }

        private void BtnPowershell_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("PowerShell 指令", "请输入 PowerShell 指令", "Get-Process");
            if (dlg.ShowDialog() == true) SendCommand("powershell", dlg.Result);
        }

        private void BtnScreenshot_Click(object sender, RoutedEventArgs e)
        {
            SendCommand("screenshot", "");
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