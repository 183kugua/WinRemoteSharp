#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace WinRemoteSharp.Core
{
    public class AgentClient : IDisposable
    {
        private AgentConfig _config;
        private ClientWebSocket? _ws;
        private CancellationTokenSource? _cts;
        private bool _running;
        private bool _connected;
        private bool _disposed;

        // 事件
        public event Action<string>? OnLog;
        public event Action<bool>? OnConnectionChanged;
        public event Action<string>? OnMessage;
        public event Action<string, string>? OnCommandResult;

        public AgentClient(AgentConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public bool IsConnected => _connected && _ws?.State == WebSocketState.Open;

        public AgentConfig CurrentConfig => _config;

        #region 配置更新

        public void UpdateConfig(AgentConfig newConfig)
        {
            if (newConfig == null) throw new ArgumentNullException(nameof(newConfig));
            _config = newConfig;
            OnLog?.Invoke("[Config] 配置已更新");
        }

        #endregion

        #region 连接 / 重连

        public async Task ConnectWithRetryAsync(CancellationToken? extToken = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AgentClient));
            
            _running = true;
            int attempt = 0;
            while (_running && !_disposed)
            {
                try
                {
                    attempt++;
                    OnLog?.Invoke($"[Agent] 正在连接 (第 {attempt} 次) → {_config.ServerUrl}");
                    await ConnectAsync();
                    attempt = 0; // 重置计数
                    await RunReceiveLoop();
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Agent] 连接错误: {ex.Message}");
                }

                if (!_running || _disposed) break;

                // 指数退避
                int delay = Math.Min(
                    _config.ReconnectBaseDelaySec * (int)Math.Pow(2, Math.Min(attempt - 1, 5)),
                    _config.ReconnectMaxDelaySec);
                OnLog?.Invoke($"[Agent] {delay} 秒后重试...");
                try { await Task.Delay(delay * 1000, extToken ?? CancellationToken.None); }
                catch (TaskCanceledException) { break; }
                catch (OperationCanceledException) { break; }
            }
        }

        public async Task ConnectAsync()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AgentClient));
            
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();
            if (!string.IsNullOrEmpty(_config.Token))
                _ws.Options.SetRequestHeader("Authorization", $"Bearer {_config.Token}");

            await _ws.ConnectAsync(new Uri(_config.ServerUrl), _cts.Token);
            _connected = true;
            OnConnectionChanged?.Invoke(true);
            OnLog?.Invoke("[Agent] 已连接，发送认证...");

            await SendAuthAsync();
            _ = Task.Run(HeartbeatLoopAsync);
        }

        private async Task SendAuthAsync()
        {
            var auth = new
            {
                type = "auth",
                token = _config.Token,
                agent_id = _config.AgentId,
                version = "1.2",
                capabilities = new[] { "cmd", "powershell", "screenshot", "keyboard", "mouse", "file_read", "file_write", "notify" }
            };
            await SendJsonAsync(auth);
        }

        #endregion

        #region 接收循环

        private async Task RunReceiveLoop()
        {
            var buffer = new byte[65536];
            var ms = new MemoryStream();
            try
            {
                while (_ws?.State == WebSocketState.Open && _running && !_disposed)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), _cts!.Token);
                        if (result.MessageType == WebSocketMessageType.Close) break;
                        ms.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close) break;

                    string msg = Encoding.UTF8.GetString(ms.ToArray());
                    ms.SetLength(0);
                    await HandleMessageAsync(msg);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) when (!_disposed)
            {
                OnLog?.Invoke($"[Agent] 接收循环错误: {ex.Message}");
            }
            finally
            {
                _connected = false;
                OnConnectionChanged?.Invoke(false);
            }
        }

        private async Task HandleMessageAsync(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string type = root.GetProperty("type").GetString() ?? "";
                OnMessage?.Invoke(json);

                switch (type)
                {
                    case "cmd":
                    case "command":
                        await HandleCommandAsync(root);
                        break;
                    case "screenshot":
                        await HandleScreenshotAsync(root);
                        break;
                    case "keyboard":
                        HandleKeyboard(root);
                        break;
                    case "mouse":
                        HandleMouse(root);
                        break;
                    case "file_read":
                        await HandleFileReadAsync(root);
                        break;
                    case "file_write":
                        await HandleFileWriteAsync(root);
                        break;
                    case "notify":
                        HandleNotify(root);
                        break;
                    case "ping":
                        await SendJsonAsync(new { type = "pong", t = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
                        break;
                    case "config_update":
                        HandleConfigUpdate(root);
                        break;
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Agent] 消息处理错误: {ex.Message}");
            }
        }

        private void HandleConfigUpdate(JsonElement root)
        {
            try
            {
                if (root.TryGetProperty("config", out var configElement))
                {
                    var newConfig = JsonSerializer.Deserialize<AgentConfig>(configElement.GetRawText());
                    if (newConfig != null)
                    {
                        _config = newConfig;
                        OnLog?.Invoke("[Config] 收到服务器配置更新");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Config] 配置更新解析失败: {ex.Message}");
            }
        }

        #endregion

        #region 指令处理

        private async Task HandleCommandAsync(JsonElement root)
        {
            string cmdId = GetString(root, "id", "");
            string cmd = GetString(root, "command", "");
            string shell = GetString(root, "shell", "cmd");
            bool elevated = GetBool(root, "elevated", false);

            OnLog?.Invoke($"[CMD] {shell}: {cmd}");

            try
            {
                string output = await ExecuteCommandAsync(cmd, shell, elevated);
                await SendResultAsync(cmdId, true, output);
            }
            catch (Exception ex)
            {
                await SendResultAsync(cmdId, false, ex.Message);
            }
        }

        private async Task<string> ExecuteCommandAsync(string command, string shell, bool elevated)
        {
            var psi = new ProcessStartInfo
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (shell == "powershell")
            {
                psi.FileName = "powershell.exe";
                psi.Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"";
            }
            else
            {
                psi.FileName = "cmd.exe";
                psi.Arguments = $"/c {command}";
            }

            if (elevated)
            {
                psi.Verb = "runas";
                psi.UseShellExecute = true;
                // elevated 需要重新设置 redirect
                psi.RedirectStandardOutput = false;
                psi.RedirectStandardError = false;
            }

            using var proc = Process.Start(psi)!;
            
            if (!elevated)
            {
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();
                
                proc.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        outputBuilder.AppendLine(e.Data);
                };
                proc.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                        errorBuilder.AppendLine(e.Data);
                };

                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();

                var timeoutTask = Task.Delay(_config.CommandTimeoutSec * 1000);
                var waitTask = proc.WaitForExitAsync();
                var completed = await Task.WhenAny(waitTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    try { proc.Kill(); } catch { }
                    return "[ERROR] 命令执行超时";
                }

                string output = outputBuilder.ToString().TrimEnd();
                string err = errorBuilder.ToString().TrimEnd();
                return string.IsNullOrEmpty(err) ? output : output + "\n[stderr] " + err;
            }
            else
            {
                // Elevated 模式无法重定向输出
                var waitTask = proc.WaitForExitAsync();
                var timeoutTask = Task.Delay(_config.CommandTimeoutSec * 1000);
                var completed = await Task.WhenAny(waitTask, timeoutTask);
                
                if (completed == timeoutTask)
                {
                    try { proc.Kill(); } catch { }
                    return "[ERROR] 命令执行超时";
                }
                
                return $"[INFO] 命令已执行 (ExitCode: {proc.ExitCode}) - 管理员模式下无法捕获输出";
            }
        }

        private async Task HandleScreenshotAsync(JsonElement root)
        {
            string cmdId = GetString(root, "id", "");
            try
            {
                // 截图必须在 STA 线程运行
                string b64 = await Task.Run(() =>
                {
                    using var bmp = CaptureScreen();
                    using var ms = new MemoryStream();
                    bmp.Save(ms, _config.ScreenshotFormat == "jpg" ? ImageFormat.Jpeg : ImageFormat.Png);
                    byte[] data = ms.ToArray();
                    return Convert.ToBase64String(data);
                });

                await SendJsonAsync(new
                {
                    type = "screenshot_result",
                    id = cmdId,
                    format = _config.ScreenshotFormat,
                    data = b64,
                    size = b64.Length * 3 / 4 // 近似原始大小
                });
                OnLog?.Invoke($"[截图] 已发送 ({b64.Length} chars base64)");
            }
            catch (Exception ex)
            {
                await SendResultAsync(cmdId, false, ex.Message);
            }
        }

        private Bitmap CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            var bmp = new Bitmap(bounds.Width, bounds.Height);
            using var g = Graphics.FromImage(bmp);
            g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            return bmp;
        }

        private void HandleKeyboard(JsonElement root)
        {
            if (!_config.EnableKeyboard) { OnLog?.Invoke("[键盘] 未启用"); return; }
            string keys = GetString(root, "keys", "");
            try
            {
                SendKeys.SendWait(keys);
                OnLog?.Invoke($"[键盘] 已发送: {keys}");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[键盘] 错误: {ex.Message}");
            }
        }

        private void HandleMouse(JsonElement root)
        {
            if (!_config.EnableMouse) { OnLog?.Invoke("[鼠标] 未启用"); return; }
            string action = GetString(root, "action", "click");
            int x = GetInt(root, "x", Cursor.Position.X);
            int y = GetInt(root, "y", Cursor.Position.Y);

            Cursor.Position = new Point(x, y);
            switch (action)
            {
                case "click":
                case "left":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
                case "right":
                    mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0);
                    break;
                case "double":
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                    mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                    break;
                case "move":
                    break;
            }
            OnLog?.Invoke($"[鼠标] {action} @ ({x},{y})");
        }

        private async Task HandleFileReadAsync(JsonElement root)
        {
            string cmdId = GetString(root, "id", "");
            string path = GetString(root, "path", "");
            try
            {
                if (!IsPathAllowed(path))
                {
                    await SendResultAsync(cmdId, false, "路径不在白名单内");
                    return;
                }
                string content = File.ReadAllText(path);
                await SendJsonAsync(new { type = "file_result", id = cmdId, success = true, content, path });
            }
            catch (Exception ex)
            {
                await SendResultAsync(cmdId, false, ex.Message);
            }
        }

        private async Task HandleFileWriteAsync(JsonElement root)
        {
            string cmdId = GetString(root, "id", "");
            if (!_config.EnableFileWrite)
            {
                await SendResultAsync(cmdId, false, "文件写入未启用（高风险功能）");
                return;
            }
            string path = GetString(root, "path", "");
            string content = GetString(root, "content", "");
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
                await SendResultAsync(cmdId, true, $"已写入 {path}");
            }
            catch (Exception ex)
            {
                await SendResultAsync(cmdId, false, ex.Message);
            }
        }

        private void HandleNotify(JsonElement root)
        {
            string title = GetString(root, "title", "WinRemote");
            string text = GetString(root, "text", "");
            try
            {
                var t = new Thread(() =>
                {
                    MessageBox.Show(text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                });
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[通知] 错误: {ex.Message}");
            }
        }

        #endregion

        #region 心跳 / 发送

        private async Task HeartbeatLoopAsync()
        {
            while (_ws?.State == WebSocketState.Open && _running && !_disposed)
            {
                try
                {
                    await Task.Delay(_config.HeartbeatIntervalSec * 1000, _cts?.Token ?? CancellationToken.None);
                    if (_disposed) break;
                    
                    await SendJsonAsync(new
                    {
                        type = "heartbeat",
                        agent_id = _config.AgentId,
                        t = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                    });
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { break; }
            }
        }

        private async Task SendJsonAsync(object obj)
        {
            if (_ws?.State != WebSocketState.Open || _disposed) return;
            try
            {
                string json = JsonSerializer.Serialize(obj);
                byte[] data = Encoding.UTF8.GetBytes(json);
                await _ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, _cts!.Token);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[发送] 错误: {ex.Message}");
            }
        }

        private async Task SendResultAsync(string id, bool success, string output)
        {
            await SendJsonAsync(new
            {
                type = "command_result",
                id,
                success,
                output = output ?? ""
            });
            OnCommandResult?.Invoke(id, output ?? "");
        }

        #endregion

        #region 工具方法

        public async Task DisconnectAsync()
        {
            _running = false;
            try
            {
                if (_ws?.State == WebSocketState.Open)
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_close", CancellationToken.None);
            }
            catch { }
            finally
            {
                _connected = false;
                OnConnectionChanged?.Invoke(false);
                
                _cts?.Cancel();
                _cts?.Dispose();
                _cts = null;
                _ws?.Dispose();
                _ws = null;
            }
        }

        private bool IsPathAllowed(string path)
        {
            if (_config.FileReadWhitelist == null || _config.FileReadWhitelist.Length == 0) return false;
            string full = Path.GetFullPath(path);
            foreach (var w in _config.FileReadWhitelist)
            {
                string wf = Path.GetFullPath(w);
                if (full.StartsWith(wf, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static string GetString(JsonElement root, string name, string def)
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String) return v.GetString() ?? def;
            return def;
        }
        private static int GetInt(JsonElement root, string name, int def)
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number) return v.GetInt32();
            return def;
        }
        private static bool GetBool(JsonElement root, string name, bool def)
        {
            if (root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True) return true;
            if (root.TryGetProperty(name, out v) && v.ValueKind == JsonValueKind.False) return false;
            return def;
        }

        #endregion

        #region Win32

        public const int MOUSEEVENTF_LEFTDOWN = 0x02;
        public const int MOUSEEVENTF_LEFTUP = 0x04;
        public const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const int MOUSEEVENTF_RIGHTUP = 0x10;

        [DllImport("user32.dll")]
        public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;
            
            try
            {
                _cts?.Cancel();
            }
            catch { }
            
            try
            {
                if (_ws?.State == WebSocketState.Open)
                {
                    _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None).Wait(1000);
                }
            }
            catch { }
            
            _cts?.Dispose();
            _ws?.Dispose();
        }

        #endregion
    }
}