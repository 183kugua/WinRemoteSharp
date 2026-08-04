using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;

namespace WinRemoteSharp.Core
{
    public class ServiceManager
    {
        private string _configPath;
        private string _serviceName = "WinRemoteAgent";

        public ServiceManager(string configPath = "config.json")
        {
            _configPath = configPath;
            try
            {
                var cfg = ConfigManager.Load(configPath);
                if (!string.IsNullOrEmpty(cfg.ServiceName))
                    _serviceName = cfg.ServiceName;
            }
            catch { }
        }

        public bool Install()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                string nssmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nssm.exe");

                if (!File.Exists(nssmPath))
                {
                    Console.WriteLine("[Service] nssm.exe not found. Please download from https://nssm.cc");
                    Console.WriteLine("[Service] Place nssm.exe in the same directory as the agent.");
                    return false;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = nssmPath,
                    Arguments = $"install {_serviceName} \"{exePath}\" --headless --config \"{_configPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    string error = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    Console.WriteLine(output);
                    if (!string.IsNullOrEmpty(error)) Console.WriteLine(error);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Install error: {ex.Message}");
                return false;
            }
        }

        public bool Uninstall()
        {
            try
            {
                string nssmPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nssm.exe");
                if (!File.Exists(nssmPath))
                {
                    // Try sc.exe as fallback
                    return UninstallWithSc();
                }

                var psi = new ProcessStartInfo
                {
                    FileName = nssmPath,
                    Arguments = $"remove {_serviceName} confirm",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Uninstall error: {ex.Message}");
                return false;
            }
        }

        private bool UninstallWithSc()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"delete {_serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return true;
                }
            }
            catch { return false; }
        }

        public bool Start()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start {_serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Start error: {ex.Message}");
                return false;
            }
        }

        public bool Stop()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"stop {_serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit();
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Service] Stop error: {ex.Message}");
                return false;
            }
        }

        public string GetStatus()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"query {_serviceName}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit();
                    if (output.Contains("RUNNING")) return "Running";
                    if (output.Contains("STOPPED")) return "Stopped";
                    if (output.Contains("START_PENDING")) return "Starting";
                    if (output.Contains("STOP_PENDING")) return "Stopping";
                    return "Unknown";
                }
            }
            catch { return "Error"; }
        }

        public string GetRecentLogs(int lines = 50)
        {
            try
            {
                var cfg = ConfigManager.Load(_configPath);
                string logDir = cfg.LogPath;
                if (!Path.IsPathRooted(logDir))
                    logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logDir);

                if (!Directory.Exists(logDir)) return "(no logs)";

                var files = Directory.GetFiles(logDir, "*.log");
                if (files.Length == 0) return "(no logs)";

                Array.Sort(files);
                string latest = files[files.Length - 1];
                var allLines = File.ReadAllLines(latest);
                var sb = new StringBuilder();
                int start = Math.Max(0, allLines.Length - lines);
                for (int i = start; i < allLines.Length; i++)
                    sb.AppendLine(allLines[i]);
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error: {ex.Message}";
            }
        }
    }
}
