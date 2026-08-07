#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

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

        public void UpdateConfig(AgentConfig newConfig)
        {
            _config = newConfig ?? throw new ArgumentNullException(nameof(newConfig));
            OnLog?.Invoke("[Config] 配置已更新");
        }

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
                    attempt = 0;
                    await RunReceiveLoop();
                }
                catch (Exception ex)
                {
                    OnLog?.Invoke($"[Agent] 连接错误: {ex.Message}");
                }
                if (!_running || _disposed) break;
                int delay = Math.Min(_config.ReconnectBaseDelaySec * (int)Math.Pow(2, Math.Min(attempt - 1, 5)), _config.ReconnectMaxDelaySec);
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
            OnLog?.Invoke("[Agent] 已连接，发送握手...");
            await SendAuthAsync();
            _ = Task.Run(HeartbeatLoopAsync);
        }

        private async Task SendAuthAsync()
        {
            var auth = new
            {
                type = "handshake",
                token = _config.Token,
                agent_id = string.IsNullOrEmpty(_config.AgentId) ? Environment.MachineName : _config.AgentId,
                info = new { hostname = Environment.MachineName, username = Environment.UserName, platform = "windows" }
            };
            await SendJsonAsync(auth);
        }

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
            catch (Exception ex) when (!_disposed) { OnLog?.Invoke($"[Agent] 接收循环错误: {ex.Message}"); }
            finally { _connected = false; OnConnectionChanged?.Invoke(false); }
        }

        private async Task HandleMessageAsync(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string type = GetString(root, "type", "");
                OnMessage?.Invoke(json);
                switch (type)
                {
                    case "command": await HandleCommandAsync(root); break;
                    case "auth_ok": OnLog?.Invoke("[Agent] 认证成功"); break;
                    case "error": OnLog?.Invoke($"[Agent] 服务器返回错误: {GetString(root, "message", "")}"); break;
                }
            }
            catch (Exception ex) { OnLog?.Invoke($"[Agent] 消息处理错误: {ex.Message}"); }
        }

        private async Task HandleCommandAsync(JsonElement root)
        {
            string id = GetString(root, "id", "");
            string action = GetString(root, "action", "");
            if (!root.TryGetProperty("params", out var prm) || prm.ValueKind != JsonValueKind.Object)
            { using var d = JsonDocument.Parse("{}"); prm = d.RootElement.Clone(); }
            try
            {
                switch (action)
                {
                    case "shell": case "powershell": await HandleShellAsync(id, action, prm); break;
                    case "screenshot": await HandleScreenshotAsync(id, prm); break;
                    case "keypress": await HandleKeyboardAsync(id, prm); break;
                    case "mouse": await HandleMouseAsync(id, prm); break;
                    case "open": await HandleOpenAsync(id, prm); break;
                    case "read_file": case "readfile": await HandleFileReadAsync(id, prm); break;
                    case "writefile": case "write_file": await HandleFileWriteAsync(id, prm); break;
                    case "ping": await HandlePingAsync(id); break;
                    default: await SendResultAsync(id, action, new { ok = false, error = $"未知动作: {action}" }); break;
                }
            }
            catch (Exception ex) { await SendResultAsync(id, action, new { ok = false, error = ex.Message }); }
        }

        private async Task HandleShellAsync(string id, string action, JsonElement prm)
        {
            string cmd = GetString(prm, "command", "");
            var r = await ExecuteCommandAsync(cmd, action == "powershell" ? "powershell" : "cmd", false);
            if (r.TimedOut)
                await SendResultAsync(id, action, new { ok = false, stdout = "", stderr = $"超时({_config.CommandTimeoutSec}s)已强杀", returncode = -1 });
            else
                await SendResultAsync(id, action, new { ok = r.ExitCode == 0, stdout = r.Output, stderr = "", returncode = r.ExitCode });
        }

        private async Task HandleScreenshotAsync(string id, JsonElement prm)
        {
            try
            {
                string fmt = (GetString(prm, "format", "JPEG")).ToUpperInvariant();
                int quality = GetInt(prm, "quality", 75);
                bool png = fmt.Contains("PNG");
                string mime = png ? "image/png" : "image/jpeg";
                byte[] data;
                using (var bmp = CaptureScreen())
                using (var ms = new MemoryStream())
                {
                    if (png) { bmp.Save(ms, ImageFormat.Png); }
                    else
                    {
                        var jpegEncoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
                        var ep = new System.Drawing.Imaging.EncoderParameters(1);
                        ep.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)Math.Max(1, Math.Min(100, quality)));
                        bmp.Save(ms, jpegEncoder, ep);
                    }
                    data = ms.ToArray();
                }
                string b64 = Convert.ToBase64String(data);
                await SendResultAsync(id, "screenshot", new { ok = true, format = mime, size = data.Length });
                await SendJsonAsync(new { type = "chunk", id, format = mime, data = b64 });
            }
            catch (Exception ex) { await SendResultAsync(id, "screenshot", new { ok = false, error = ex.Message }); }
        }

        private async Task HandleKeyboardAsync(string id, JsonElement prm)
        {
            if (!_config.EnableKeyboard) { await SendResultAsync(id, "keypress", new { ok = false, error = "键盘模拟未启用" }); return; }
            try { SendKeyCombo(GetString(prm, "keys", "")); await SendResultAsync(id, "keypress", new { ok = true }); }
            catch (Exception ex) { await SendResultAsync(id, "keypress", new { ok = false, error = ex.Message }); }
        }

        private async Task HandleMouseAsync(string id, JsonElement prm)
        {
            if (!_config.EnableMouse) { await SendResultAsync(id, "mouse", new { ok = false, error = "鼠标模拟未启用" }); return; }
            string button = GetString(prm, "button", "click");
            int x = GetInt(prm, "x", Cursor.Position.X);
            int y = GetInt(prm, "y", Cursor.Position.Y);
            try
            {
                Cursor.Position = new System.Drawing.Point(x, y);
                switch (button.ToLowerInvariant())
                {
                    case "right": mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0); mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0); break;
                    case "double": mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0); mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0); mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0); mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0); break;
                    case "move": break;
                    default: mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0); mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0); break;
                }
                await SendResultAsync(id, "mouse", new { ok = true });
            }
            catch (Exception ex) { await SendResultAsync(id, "mouse", new { ok = false, error = ex.Message }); }
        }

        private async Task HandleOpenAsync(string id, JsonElement prm)
        {
            string target = GetString(prm, "target", "");
            try
            {
                if (string.IsNullOrWhiteSpace(target)) { await SendResultAsync(id, "open", new { ok = false, error = "目标为空" }); return; }
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true, CreateNoWindow = true });
                await SendResultAsync(id, "open", new { ok = true });
            }
            catch (Exception ex) { await SendResultAsync(id, "open", new { ok = false, error = ex.Message }); }
        }

        private async Task HandleFileReadAsync(string id, JsonElement prm)
        {
            string path = GetString(prm, "path", "");
            int max = GetInt(prm, "max_bytes", 1048576);
            try
            {
                if (string.IsNullOrWhiteSpace(path)) { await SendResultAsync(id, "read_file", new { ok = false, error = "路径为空" }); return; }
                if (!IsPathAllowed(path)) { await SendResultAsync(id, "read_file", new { ok = false, error = "路径不在白名单内" }); return; }
                byte[] raw = File.ReadAllBytes(path);
                if (max > 0 && raw.Length > max) raw = raw.Take(max).ToArray();
                string content = Encoding.UTF8.GetString(raw);
                await SendResultAsync(id, "read_file", new { ok = true, content, bytes = Encoding.UTF8.GetByteCount(content) });
            }
            catch (Exception ex) { await SendResultAsync(id, "read_file", new { ok = false, error = ex.Message }); }
        }

        private async Task HandleFileWriteAsync(string id, JsonElement prm)
        {
            if (!_config.EnableFileWrite) { await SendResultAsync(id, "writefile", new { ok = false, error = "文件写入未启用" }); return; }
            string path = GetString(prm, "path", "");
            string content = GetString(prm, "content", "");
            try
            {
                if (string.IsNullOrWhiteSpace(path)) { await SendResultAsync(id, "writefile", new { ok = false, error = "路径为空" }); return; }
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, content);
                await SendResultAsync(id, "writefile", new { ok = true, bytes = Encoding.UTF8.GetByteCount(content) });
            }
            catch (Exception ex) { await SendResultAsync(id, "writefile", new { ok = false, error = ex.Message }); }
        }

        private async Task HandlePingAsync(string id)
        {
            await SendResultAsync(id, "ping", new { ok = true, pong = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
        }

        private async Task<CmdResult> ExecuteCommandAsync(string command, string shell, bool elevated)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                RedirectStandardOutput = !elevated,
                RedirectStandardError = !elevated,
                UseShellExecute = elevated,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Arguments = shell == "powershell"
                ? $"/c chcp 65001 >nul && powershell -NoProfile -ExecutionPolicy Bypass -Command \"{command}\""
                : $"/c chcp 65001 >nul && {command}";
            if (elevated) psi.Verb = "runas";
            using var proc = Process.Start(psi)!;
            if (!elevated)
            {
                var outB = new StringBuilder(); var errB = new StringBuilder();
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) outB.AppendLine(e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) errB.AppendLine(e.Data); };
                proc.BeginOutputReadLine(); proc.BeginErrorReadLine();
                var waitTask = proc.WaitForExitAsync();
                var timeoutTask = Task.Delay(_config.CommandTimeoutSec * 1000);
                if (await Task.WhenAny(waitTask, timeoutTask) == timeoutTask) { try { proc.Kill(); } catch { } return new CmdResult { TimedOut = true, ExitCode = -1 }; }
                string output = outB.ToString().TrimEnd(), err = errB.ToString().TrimEnd();
                return new CmdResult { Output = string.IsNullOrEmpty(err) ? output : output + "\n[stderr] " + err, ExitCode = proc.ExitCode };
            }
            else
            {
                var waitTask = proc.WaitForExitAsync();
                var timeoutTask = Task.Delay(_config.CommandTimeoutSec * 1000);
                if (await Task.WhenAny(waitTask, timeoutTask) == timeoutTask) { try { proc.Kill(); } catch { } return new CmdResult { TimedOut = true, ExitCode = -1 }; }
                return new CmdResult { Output = $"[INFO] 命令已执行 (ExitCode: {proc.ExitCode})", ExitCode = proc.ExitCode };
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

        private static readonly Dictionary<string, int> _specialKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            { "enter", 0x0D }, { "return", 0x0D }, { "tab", 0x09 }, { "esc", 0x1B }, { "escape", 0x1B },
            { "backspace", 0x08 }, { "delete", 0x2E }, { "del", 0x2E }, { "space", 0x20 }, { " ", 0x20 },
            { "up", 0x26 }, { "down", 0x28 }, { "left", 0x25 }, { "right", 0x27 },
            { "home", 0x24 }, { "end", 0x23 }, { "pgup", 0x21 }, { "pgdn", 0x22 }, { "insert", 0x2D },
            { "f1", 0x70 }, { "f2", 0x71 }, { "f3", 0x72 }, { "f4", 0x73 }, { "f5", 0x74 }, { "f6", 0x75 },
            { "f7", 0x76 }, { "f8", 0x77 }, { "f9", 0x78 }, { "f10", 0x79 }, { "f11", 0x7A }, { "f12", 0x7B },
            { "capslock", 0x14 }, { "numlock", 0x90 }, { "scrolllock", 0x91 }, { "printscreen", 0x2C },
            { "pause", 0x13 }, { "win", 0x5B }, { "windows", 0x5B }, { "lwin", 0x5B }, { "rwin", 0x5C },
            { "menu", 0x5D }, { "apps", 0x5D }
        };

        private static int CharToVk(string key)
        {
            if (_specialKeys.TryGetValue(key, out int vk)) return vk;
            if (key.Length == 1) { short v = VkKeyScan(key[0]); if (v != -1) return v & 0xFF; }
            return 0;
        }

        private static void SendKeyCombo(string combo)
        {
            if (string.IsNullOrWhiteSpace(combo)) return;
            var parts = combo.Split('+');
            var modifiers = new List<int>();
            string mainKey = parts[^1];
            for (int i = 0; i < parts.Length - 1; i++)
            {
                int mv = parts[i].Trim().ToLowerInvariant() switch
                {
                    "ctrl" or "control" => 0x11, "alt" => 0x12, "shift" => 0x10,
                    "win" or "windows" or "lwin" => 0x5B, "rwin" => 0x5C, _ => 0
                };
                if (mv != 0) modifiers.Add(mv);
            }
            int vk = CharToVk(mainKey.Trim());
            if (vk == 0) throw new Exception($"无法识别的按键: {mainKey}");
            foreach (var m in modifiers) keybd_event((byte)m, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event((byte)vk, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
            keybd_event((byte)vk, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            foreach (var m in ((IEnumerable<int>)modifiers).Reverse()) keybd_event((byte)m, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        private async Task HeartbeatLoopAsync()
        {
            while (_ws?.State == WebSocketState.Open && _running && !_disposed)
            {
                try
                {
                    await Task.Delay(_config.HeartbeatIntervalSec * 1000, _cts?.Token ?? CancellationToken.None);
                    if (_disposed) break;
                    await SendJsonAsync(new { type = "heartbeat", agent_id = string.IsNullOrEmpty(_config.AgentId) ? Environment.MachineName : _config.AgentId, t = DateTimeOffset.UtcNow.ToUnixTimeSeconds() });
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
                await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, true, _cts!.Token);
            }
            catch (Exception ex) { OnLog?.Invoke($"[发送] 错误: {ex.Message}"); }
        }

        private async Task SendResultAsync(string id, string action, object resultObj)
        {
            await SendJsonAsync(new { type = "result", id, action, result = resultObj });
            if (action is "shell" or "powershell" or "read_file" or "writefile" or "screenshot")
                OnCommandResult?.Invoke(id, JsonSerializer.Serialize(resultObj));
        }

        public async Task DisconnectAsync()
        {
            _running = false;
            try { if (_ws?.State == WebSocketState.Open) await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_close", CancellationToken.None); }
            catch { }
            finally
            {
                _connected = false; OnConnectionChanged?.Invoke(false);
                _cts?.Cancel(); _cts?.Dispose(); _cts = null;
                _ws?.Dispose(); _ws = null;
            }
        }

        private bool IsPathAllowed(string path)
        {
            if (_config.FileReadWhitelist == null || _config.FileReadWhitelist.Length == 0) return true;
            string full = Path.GetFullPath(path);
            foreach (var w in _config.FileReadWhitelist)
                if (full.StartsWith(Path.GetFullPath(w), StringComparison.OrdinalIgnoreCase)) return true;
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

        public const int MOUSEEVENTF_LEFTDOWN = 0x02;
        public const int MOUSEEVENTF_LEFTUP = 0x04;
        public const int MOUSEEVENTF_RIGHTDOWN = 0x08;
        public const int MOUSEEVENTF_RIGHTUP = 0x10;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")] public static extern void mouse_event(int dwFlags, int dx, int dy, int dwData, int dwExtraInfo);
        [DllImport("user32.dll")] public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] public static extern short VkKeyScan(char ch);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true; _running = false;
            try { _cts?.Cancel(); } catch { }
            try { if (_ws?.State == WebSocketState.Open) _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None).Wait(1000); } catch { }
            _cts?.Dispose(); _ws?.Dispose();
        }

        private sealed class CmdResult { public string Output = ""; public int ExitCode; public bool TimedOut; }
    }
}
