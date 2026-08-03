using System;
using System.Diagnostics;

namespace WinRemoteSharp.Core
{
    public class ServiceResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
    }

    public class ServiceManager
    {
        public string ServiceName { get; set; } = "WinRemoteAgent";

        public string FindNssm()
        {
            var paths = new[]
            {
                @"C:\nssm\nssm.exe",
                @"C:\Program Files\nssm\nssm.exe",
                @"C:\Program Files (x86)\nssm\nssm.exe",
            };
            foreach (var p in paths) if (System.IO.File.Exists(p)) return p;
            // PATH
            var psi = new ProcessStartInfo("where", "nssm")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using var p = Process.Start(psi);
                var line = p?.StandardOutput.ReadLine();
                if (!string.IsNullOrEmpty(line) && System.IO.File.Exists(line)) return line;
            }
            catch { }
            return "";
        }

        public string GetStatus()
        {
            try
            {
                var psi = new ProcessStartInfo("sc", $"query {ServiceName}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var output = p?.StandardOutput.ReadToEnd() ?? "";
                if (output.Contains("RUNNING")) return "Running";
                if (output.Contains("STOPPED")) return "Stopped";
                return "NotFound";
            }
            catch { return "Unknown"; }
        }

        public string GetStartType()
        {
            try
            {
                var psi = new ProcessStartInfo("sc", $"qc {ServiceName}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var outp = p?.StandardOutput.ReadToEnd() ?? "";
                if (outp.Contains("AUTO_START")) return "自动";
                if (outp.Contains("DEMAND_START")) return "手动";
                if (outp.Contains("DISABLED")) return "禁用";
                return "未知";
            }
            catch { return "未知"; }
        }

        public string GetExePath()
        {
            try
            {
                var psi = new ProcessStartInfo("sc", $"qc {ServiceName}")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                var outp = p?.StandardOutput.ReadToEnd() ?? "";
                var lines = outp.Split('\n');
                foreach (var l in lines)
                {
                    if (l.Contains("BINARY_PATH_NAME"))
                    {
                        var idx = l.IndexOf(':');
                        if (idx > 0) return l.Substring(idx + 1).Trim();
                    }
                }
                return "-";
            }
            catch { return "-"; }
        }

        public ServiceResult Install(string exePath)
        {
            var nssm = FindNssm();
            if (string.IsNullOrEmpty(nssm)) return new() { Success = false, Message = "未找到 NSSM，请先安装" };
            if (!System.IO.File.Exists(exePath)) return new() { Success = false, Message = $"文件不存在: {exePath}" };

            var psi = new ProcessStartInfo(nssm, $"install {ServiceName} \"{exePath}\" --mode agent")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var msg = p?.StandardOutput.ReadToEnd() ?? "";
            return new() { Success = msg.Contains("success") || msg.Contains("成功"), Message = msg.Trim() };
        }

        public ServiceResult Uninstall()
        {
            var nssm = FindNssm();
            if (string.IsNullOrEmpty(nssm)) return new() { Success = false, Message = "未找到 NSSM" };
            var psi = new ProcessStartInfo(nssm, $"remove {ServiceName} confirm")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var msg = p?.StandardOutput.ReadToEnd() ?? "";
            return new() { Success = !msg.Contains("error"), Message = msg.Trim() };
        }

        public ServiceResult Start()
        {
            var psi = new ProcessStartInfo("sc", $"start {ServiceName}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var msg = p?.StandardOutput.ReadToEnd() ?? "";
            return new() { Success = !msg.Contains("FAILED"), Message = msg.Trim() };
        }

        public ServiceResult Stop()
        {
            var psi = new ProcessStartInfo("sc", $"stop {ServiceName}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            var msg = p?.StandardOutput.ReadToEnd() ?? "";
            return new() { Success = !msg.Contains("FAILED"), Message = msg.Trim() };
        }

        public string GetStdoutPath() => $@"C:\Program Files\WinRemoteAgent\logs\stdout.log";
        public string GetStderrPath() => $@"C:\Program Files\WinRemoteAgent\logs\stderr.log";
    }
}
