using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace CrackerBlock.Launcher
{
    internal static class Program
    {
        private const string ServiceName = "AppBlockerService";
        private const string ServiceProcessName = "AppBlocker.Service";

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var serviceExe = ResolveServiceExecutable(baseDir);
                var uiExe = ResolveUiExecutable(baseDir);

                // 1. Check if background engine is active
                bool isRunning = IsServiceProcessRunning();

                if (!isRunning && !string.IsNullOrEmpty(serviceExe) && File.Exists(serviceExe))
                {
                    // Elevate once to install & start the Windows service automatically
                    StartOrInstallServiceElevated(serviceExe);

                    // Wait briefly for service daemon to spin up
                    for (int i = 0; i < 10; i++)
                    {
                        if (IsServiceProcessRunning()) break;
                        Thread.Sleep(300);
                    }
                }

                // 2. Launch the UI Dashboard
                if (!string.IsNullOrEmpty(uiExe) && File.Exists(uiExe))
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = uiExe,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(uiExe) ?? baseDir
                    };
                    Process.Start(psi);
                }
                else
                {
                    // Fallback alert if binaries were moved
                    System.Windows.Forms.MessageBox.Show(
                        "Cannot find CrackerBlock Dashboard at expected path:\n" + (uiExe ?? "Unknown") +
                        "\n\nPlease extract the full release package before launching.",
                        "CrackerBlock Launcher",
                        System.Windows.Forms.MessageBoxButtons.OK,
                        System.Windows.Forms.MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Error launching CrackerBlock:\n" + ex.Message,
                    "CrackerBlock Launcher",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
        }

        private static bool IsServiceProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName(ServiceProcessName).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void StartOrInstallServiceElevated(string serviceExe)
        {
            try
            {
                string cmdArgs = $"/c sc.exe query {ServiceName} >nul 2>&1 || sc.exe create {ServiceName} binPath= \"\"{serviceExe}\"\" start= auto DisplayName= \"CrackerBlock Enforcement Engine\" & sc.exe config {ServiceName} binPath= \"\"{serviceExe}\"\" start= auto & sc.exe failure {ServiceName} reset= 86400 actions= restart/5000/restart/5000/restart/5000 & sc.exe failureflag {ServiceName} 1 & if not exist \"%ProgramData%\\AppBlocker\" mkdir \"%ProgramData%\\AppBlocker\" & icacls \"%ProgramData%\\AppBlocker\" /grant \"*S-1-5-32-545:(OI)(CI)F\" /T /Q & sc.exe start {ServiceName}";

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    Verb = "runas", // Triggers standard Windows UAC prompt
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(6000);
            }
            catch
            {
                // If user clicks "No" on UAC, continue silently and let UI handle offline state
            }
        }

        private static string? ResolveServiceExecutable(string baseDir)
        {
            // 1. Release package layout: Service\AppBlocker.Service.exe
            var p1 = Path.Combine(baseDir, "Service", "AppBlocker.Service.exe");
            if (File.Exists(p1)) return p1;

            // 2. Direct folder layout
            var p2 = Path.Combine(baseDir, "AppBlocker.Service.exe");
            if (File.Exists(p2)) return p2;

            // 3. Workspace dev layouts
            var p3 = Path.GetFullPath(Path.Combine(baseDir, @"..\AppBlocker.Service\bin\Release\net9.0\AppBlocker.Service.exe"));
            if (File.Exists(p3)) return p3;

            var p4 = Path.GetFullPath(Path.Combine(baseDir, @"..\AppBlocker.Service\bin\Debug\net9.0\AppBlocker.Service.exe"));
            if (File.Exists(p4)) return p4;

            return null;
        }

        private static string? ResolveUiExecutable(string baseDir)
        {
            // 1. Release package layout: Dashboard\AppBlocker.UI.exe
            var p1 = Path.Combine(baseDir, "Dashboard", "AppBlocker.UI.exe");
            if (File.Exists(p1)) return p1;

            // 2. Direct folder layout
            var p2 = Path.Combine(baseDir, "AppBlocker.UI.exe");
            if (File.Exists(p2)) return p2;

            // 3. Workspace dev layouts
            var p3 = Path.GetFullPath(Path.Combine(baseDir, @"..\AppBlocker.UI\bin\Release\net9.0-windows\AppBlocker.UI.exe"));
            if (File.Exists(p3)) return p3;

            var p4 = Path.GetFullPath(Path.Combine(baseDir, @"..\AppBlocker.UI\bin\Debug\net9.0-windows\AppBlocker.UI.exe"));
            if (File.Exists(p4)) return p4;

            return null;
        }
    }
}
