using System;
using System.Diagnostics;
using System.IO;

namespace AppBlocker.Core.Security
{
    public static class ServiceRecoveryManager
    {
        public const string DefaultServiceName = "AppBlockerService";

        public static bool ConfigureServiceRecovery(string serviceName = DefaultServiceName, Action<string>? logger = null)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                // 1. Configure restart on 1st, 2nd, and subsequent failures (restart after 5000ms, reset counter after 1 day = 86400s)
                var failureArgs = $"failure \"{serviceName}\" reset= 86400 actions= restart/5000/restart/5000/restart/5000";
                var scResult = RunScCommand(failureArgs);
                logger?.Invoke($"sc.exe {failureArgs} returned code {scResult}");

                // 2. Enable failure actions flag so ungraceful crashes/kills trigger restart
                var flagArgs = $"failureflag \"{serviceName}\" 1";
                var flagResult = RunScCommand(flagArgs);
                logger?.Invoke($"sc.exe {flagArgs} returned code {flagResult}");

                return scResult == 0 && flagResult == 0;
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Failed to configure service recovery: {ex.Message}");
                return false;
            }
        }

        private static int RunScCommand(string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return -1;
            }

            process.WaitForExit(5000);
            return process.ExitCode;
        }

        public static string GenerateInstallScript(string serviceExePath, string serviceName = DefaultServiceName)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine(":: AppBlocker Windows Service Auto-Installer & Watchdog Recovery Configurator");
            sb.AppendLine(":: Must be run as Administrator");
            sb.AppendLine();
            sb.AppendLine($"echo Installing {serviceName}...");
            sb.AppendLine($"sc.exe create \"{serviceName}\" binPath= \"{serviceExePath}\" start= auto DisplayName= \"AppBlocker Enforcement Engine\"");
            sb.AppendLine($"sc.exe description \"{serviceName}\" \"Enforces application and website blocking rules with anti-tamper recovery.\"");
            sb.AppendLine();
            sb.AppendLine("echo Configuring Watchdog and Crash Recovery actions...");
            sb.AppendLine($"sc.exe failure \"{serviceName}\" reset= 86400 actions= restart/5000/restart/5000/restart/5000");
            sb.AppendLine($"sc.exe failureflag \"{serviceName}\" 1");
            sb.AppendLine();
            sb.AppendLine($"echo Starting {serviceName}...");
            sb.AppendLine($"sc.exe start \"{serviceName}\"");
            sb.AppendLine();
            sb.AppendLine("echo Service installation and recovery configuration complete.");
            return sb.ToString();
        }

        public static string GenerateUninstallScript(string serviceName = DefaultServiceName)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine(":: AppBlocker Windows Service Uninstaller");
            sb.AppendLine(":: Must be run as Administrator");
            sb.AppendLine();
            sb.AppendLine($"echo Stopping {serviceName}...");
            sb.AppendLine($"sc.exe stop \"{serviceName}\"");
            sb.AppendLine($"echo Deleting {serviceName}...");
            sb.AppendLine($"sc.exe delete \"{serviceName}\"");
            sb.AppendLine("echo Service uninstalled successfully.");
            return sb.ToString();
        }
    }
}
