using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using AppBlocker.Core.Security;

namespace AppBlocker.Watchdog
{
    public class Program
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AppBlocker",
            "watchdog.log");

        public static int Main(string[] args)
        {
            // Parse command-line flags
            if (args.Contains("--signal-shutdown", StringComparer.OrdinalIgnoreCase))
            {
                WatchdogCoordinator.SignalCleanShutdown();
                LogMessage("Clean shutdown signal dispatched successfully.");
                return 0;
            }

            if (args.Contains("--status", StringComparer.OrdinalIgnoreCase))
            {
                bool isCleanShutdown = WatchdogCoordinator.IsCleanShutdownSignaled();
                bool isServiceRunning = WatchdogCoordinator.IsServiceProcessRunning();
                Console.WriteLine($"Service Running: {isServiceRunning}, Clean Shutdown Signaled: {isCleanShutdown}");
                return 0;
            }

            int targetPid = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--target-pid", StringComparison.OrdinalIgnoreCase))
                {
                    int.TryParse(args[i + 1], out targetPid);
                    break;
                }
            }

            // Ensure single instance for the watchdog monitoring daemon
            using var singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\AppBlocker_Watchdog_Daemon_Mutex", out bool isOnlyInstance);
            if (!isOnlyInstance)
            {
                LogMessage("Another AppBlocker.Watchdog instance is already active. Exiting duplicate process.");
                return 0;
            }

            LogMessage($"AppBlocker Watchdog companion daemon active (Target PID: {targetPid}). Starting 500ms mutual guard loop.");

            var coordinator = new WatchdogCoordinator(LogMessage);
            int currentPid = targetPid;

            // If targetPid was not passed, discover existing AppBlocker.Service process
            if (currentPid <= 0)
            {
                var existingServices = Process.GetProcessesByName(WatchdogCoordinator.ServiceProcessName);
                if (existingServices.Length > 0)
                {
                    currentPid = existingServices[0].Id;
                    LogMessage($"Discovered existing AppBlocker.Service process (PID: {currentPid}).");
                }
            }

            while (true)
            {
                // 1. Check for authorized administrator shutdown
                if (WatchdogCoordinator.IsCleanShutdownSignaled())
                {
                    LogMessage("Authorized clean shutdown signal detected. Watchdog terminating gracefully.");
                    break;
                }

                // 2. Check if the target service process is alive
                bool isAlive = false;
                if (currentPid > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById(currentPid);
                        isAlive = !proc.HasExited && proc.ProcessName.Contains("AppBlocker.Service", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        isAlive = false;
                    }
                }
                else
                {
                    isAlive = WatchdogCoordinator.IsServiceProcessRunning();
                }

                // 3. If service died, inspect if clean shutdown was signaled during termination
                if (!isAlive)
                {
                    // Re-check clean shutdown event
                    if (WatchdogCoordinator.IsCleanShutdownSignaled())
                    {
                        LogMessage("Service exited during clean shutdown sequence. Watchdog exiting.");
                        break;
                    }

                    // Unauthorized termination / taskkill detected!
                    LogMessage($"Tamper detected! AppBlocker.Service (PID: {currentPid}) was killed without clean shutdown authorization. Reviving immediately...");

                    RecordTamperEvent(currentPid);

                    // Revive service
                    coordinator.ReviveService();

                    // Wait up to 10 seconds for new service process to materialize
                    int attempts = 0;
                    bool resurrected = false;
                    while (attempts < 20)
                    {
                        Thread.Sleep(500);
                        attempts++;

                        if (WatchdogCoordinator.IsCleanShutdownSignaled())
                        {
                            return 0;
                        }

                        var newServices = Process.GetProcessesByName(WatchdogCoordinator.ServiceProcessName);
                        if (newServices.Length > 0)
                        {
                            currentPid = newServices[0].Id;
                            LogMessage($"AppBlocker.Service resurrected successfully with new PID {currentPid}. Dual protection restored.");
                            resurrected = true;
                            break;
                        }
                    }

                    if (!resurrected)
                    {
                        LogMessage("Failed to detect revived AppBlocker.Service within 10 seconds. Retrying revival loop...");
                    }
                }

                Thread.Sleep(500);
            }

            return 0;
        }

        private static void RecordTamperEvent(int killedPid)
        {
            try
            {
                using var db = AppBlockerDbContext.CreateContext();
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Tamper Detected",
                    Description = $"Service (PID: {killedPid}) was terminated abruptly without authorization. Watchdog resurrected the service."
                });
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                LogMessage($"Failed to log tamper event to DB: {ex.Message}");
            }
        }

        private static void LogMessage(string message)
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [Watchdog] {message}";
            try
            {
                Console.WriteLine(line);
                var dir = Path.GetDirectoryName(LogFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Suppress file IO issues during rapid shutdown
            }
        }
    }
}
