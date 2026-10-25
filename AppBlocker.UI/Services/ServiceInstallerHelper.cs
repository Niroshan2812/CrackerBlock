using System;
using System.Diagnostics;
using System.IO;

namespace AppBlocker.UI.Services
{
    public static class ServiceInstallerHelper
    {
        public const string ServiceName = "AppBlockerService";

        public static bool IsServiceRunning()
        {
            try
            {
                return Process.GetProcessesByName("AppBlocker.Service").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        public static string? FindServiceExecutable()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. In relative ../Service folder (Release package layout)
            var relService = Path.GetFullPath(Path.Combine(baseDir, "..", "Service", "AppBlocker.Service.exe"));
            if (File.Exists(relService)) return relService;

            // 2. In same folder
            var direct = Path.Combine(baseDir, "AppBlocker.Service.exe");
            if (File.Exists(direct)) return direct;

            // 3. In workspace Debug layout
            var debugLayout = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\AppBlocker.Service\bin\Debug\net9.0\AppBlocker.Service.exe"));
            if (File.Exists(debugLayout)) return debugLayout;

            // 4. In workspace Release layout
            var releaseLayout = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\AppBlocker.Service\bin\Release\net9.0\AppBlocker.Service.exe"));
            if (File.Exists(releaseLayout)) return releaseLayout;

            return null;
        }

        public static bool EnsureServiceStartedElevated()
        {
            try
            {
                var serviceExe = FindServiceExecutable();
                string cmdArgs;

                if (!string.IsNullOrEmpty(serviceExe) && File.Exists(serviceExe))
                {
                    cmdArgs = $"/c sc.exe query {ServiceName} >nul 2>&1 || sc.exe create {ServiceName} binPath= \"\"{serviceExe}\"\" start= auto DisplayName= \"CrackerBlock Enforcement Engine\" & sc.exe config {ServiceName} binPath= \"\"{serviceExe}\"\" start= auto & sc.exe failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000 & sc.exe failureflag {ServiceName} 1 & if not exist \"%ProgramData%\\AppBlocker\" mkdir \"%ProgramData%\\AppBlocker\" & icacls \"%ProgramData%\\AppBlocker\" /grant \"*S-1-5-32-545:(OI)(CI)F\" /T /Q & sc.exe start {ServiceName}";
                }
                else
                {
                    cmdArgs = $"/c sc.exe start {ServiceName}";
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    Verb = "runas", // Triggers Windows UAC prompt
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(5000);
                return IsServiceRunning();
            }
            catch
            {
                return false;
            }
        }
    }
}
