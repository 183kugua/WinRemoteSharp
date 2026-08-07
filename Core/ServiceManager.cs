#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Text.RegularExpressions;

namespace WinRemoteSharp.Core
{
    public class ServiceManager : IDisposable
    {
        private readonly string _nssmPath;
        private readonly string _serviceName;
        private readonly string _exePath;
        private bool _disposed;

        public ServiceManager(string? nssmPath = null, string? serviceName = null, string? exePath = null)
        {
            _nssmPath = nssmPath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WinRemote", "nssm.exe");
            _serviceName = serviceName ?? "WinRemoteAgent";
            _exePath = exePath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "WinRemoteAgent.exe";
        }

        public string NssmPath => _nssmPath;
        public string ServiceName => _serviceName;

        public bool IsNssmAvailable()
        {
            if (File.Exists(_nssmPath)) return true;
            try
            {
                var psi = new ProcessStartInfo("where", "nssm")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                string outp = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit();
                return !string.IsNullOrWhiteSpace(outp);
            }
            catch { return false; }
        }

        public (bool running, string state) GetServiceState()
        {
            try
            {
                var psi = new ProcessStartInfo("sc", $"query {_serviceName}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                string output = p?.StandardOutput.ReadToEnd() ?? "";
                p?.WaitForExit();

                if (output.Contains("RUNNING")) return (true, "运行中");
                if (output.Contains("STOPPED")) return (false, "已停止");
                if (output.Contains("1060")) return (false, "未安装");
                return (false, "未知");
            }
            catch (Exception ex)
            {
                return (false, $"错误: {ex.Message}");
            }
        }

        public string Install()
        {
            if (!IsNssmAvailable()) return "❌ NSSM 未找到，请先安装 NSSM";
            try
            {
                string args = $"install {_serviceName} \"{_exePath}\" --headless";
                var (ok, output) = RunNssm(args);
                if (!ok) return $"❌ 安装失败: {output}";
                RunNssm($"set {_serviceName} Start SERVICE_AUTO_START");
                string? workDir = Path.GetDirectoryName(_exePath);
                if (!string.IsNullOrEmpty(workDir))
                    RunNssm($"set {_serviceName} AppDirectory \"{workDir}\"");
                return $"✅ 服务 [{_serviceName}] 安装成功";
            }
            catch (Exception ex) { return $"❌ 安装异常: {ex.Message}"; }
        }

        public string Uninstall()
        {
            if (!IsNssmAvailable()) return "❌ NSSM 未找到";
            try
            {
                RunNssm($"stop {_serviceName}");
                var (ok, output) = RunNssm($"remove {_serviceName} confirm");
                return ok ? $"✅ 服务 [{_serviceName}] 已卸载" : $"❌ 卸载失败: {output}";
            }
            catch (Exception ex) { return $"❌ 卸载异常: {ex.Message}"; }
        }

        public string Start()
        {
            if (!IsNssmAvailable()) return "❌ NSSM 未找到";
            var (ok, output) = RunNssm($"start {_serviceName}");
            return ok ? $"✅ 服务 [{_serviceName}] 已启动" : $"❌ 启动失败: {output}";
        }

        public string Stop()
        {
            if (!IsNssmAvailable()) return "❌ NSSM 未找到";
            var (ok, output) = RunNssm($"stop {_serviceName}");
            return ok ? $"✅ 服务 [{_serviceName}] 已停止" : $"❌ 停止失败: {output}";
        }

        public string Restart()
        {
            Stop();
            Thread.Sleep(1000);
            return Start();
        }

        public string ReadServiceLog()
        {
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WinRemote", "logs");
            string stdout = Path.Combine(logDir, "WinRemoteAgent.out.log");
            string stderr = Path.Combine(logDir, "WinRemoteAgent.err.log");

            var sb = new System.Text.StringBuilder();
            if (File.Exists(stdout))
                sb.AppendLine("=== STDOUT ===").AppendLine(File.ReadAllText(stdout));
            else
                sb.AppendLine($"(无标准输出日志: {stdout})");
            if (File.Exists(stderr))
                sb.AppendLine("=== STDERR ===").AppendLine(File.ReadAllText(stderr));
            else
                sb.AppendLine($"(无错误日志: {stderr})");
            return sb.ToString();
        }

        private (bool ok, string output) RunNssm(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(_nssmPath, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi)!;
                string output = p.StandardOutput.ReadToEnd();
                string error = p.StandardError.ReadToEnd();
                p.WaitForExit(30000);
                bool ok = p.ExitCode == 0;
                return (ok, ok ? output : error);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
