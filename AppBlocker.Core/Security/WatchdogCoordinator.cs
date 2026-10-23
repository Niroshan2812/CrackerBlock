using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;

namespace AppBlocker.Core.Security
{
    /// <summary>
    /// Coordinates Layer 4 Dual-Process Mutual Watchdog monitoring between
    /// AppBlocker.Service and AppBlocker.Watchdog.
    /// Uses cross-session named events and mutual process polling to make
    /// enforcement resilient against terminal termination ('taskkill /F').
    /// </summary>
    public class WatchdogCoordinator
    {
        public const string ShutdownEventName = @"Global\AppBlocker_CleanShutdown_Event";
        public const string ServiceName = "AppBlockerService";
        public const string WatchdogProcessName = "AppBlocker.Watchdog";
        public const string ServiceProcessName = "AppBlocker.Service";

        private readonly Action<string>? _logger;

        public WatchdogCoordinator(Action<string>? logger = null)
        {
            _logger = logger;
        }

        private void Log(string message)
        {
            _logger?.Invoke($"[WatchdogCoordinator] {message}");
        }

        private static readonly object _eventSync = new object();
        private static EventWaitHandle? _heldShutdownEvent;

        /// <summary>
        /// Gets or creates the system-wide clean shutdown manual-reset event,
        /// maintaining an open process handle to prevent Windows kernel object deallocation.
        /// </summary>
        public static EventWaitHandle GetOrCreateShutdownEvent()
        {
            lock (_eventSync)
            {
                if (_heldShutdownEvent != null)
                {
                    return _heldShutdownEvent;
                }

                if (EventWaitHandle.TryOpenExisting(ShutdownEventName, out var existing))
                {
                    _heldShutdownEvent = existing;
                    return _heldShutdownEvent;
                }

                _heldShutdownEvent = CreateOrOpenShutdownEvent(out _);
                return _heldShutdownEvent;
            }
        }

        /// <summary>
        /// Creates or opens the system-wide clean shutdown manual-reset event.
        /// Configures DACL to allow FullControl to Everyone (WorldSid) so both
        /// elevated services and user-session processes can interact with it.
        /// </summary>
        public static EventWaitHandle CreateOrOpenShutdownEvent(out bool createdNew)
        {
            try
            {
                var security = new EventWaitHandleSecurity();
                var worldSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                security.AddAccessRule(new EventWaitHandleAccessRule(
                    worldSid,
                    EventWaitHandleRights.FullControl,
                    AccessControlType.Allow));

                return EventWaitHandleAcl.Create(
                    initialState: false,
                    mode: EventResetMode.ManualReset,
                    name: ShutdownEventName,
                    createdNew: out createdNew,
                    eventSecurity: security);
            }
            catch
            {
                // Fallback for environments where ACL manipulation might be constrained
                return new EventWaitHandle(false, EventResetMode.ManualReset, ShutdownEventName, out createdNew);
            }
        }

        /// <summary>
        /// Signals that a legitimate, authorized shutdown has been requested
        /// (via stop_service.bat, uninstall_service.bat, or PIN-authorized exit).
        /// When this event is set, the watchdog will not attempt to revive the service.
        /// </summary>
        public static void SignalCleanShutdown()
        {
            try
            {
                var handle = GetOrCreateShutdownEvent();
                handle.Set();
            }
            catch
            {
                // Suppress if event cannot be signaled
            }
        }

        /// <summary>
        /// Resets the clean shutdown event at service/watchdog startup
        /// so that unexpected terminations trigger watchdog revival.
        /// </summary>
        public static void ResetCleanShutdown()
        {
            try
            {
                var handle = GetOrCreateShutdownEvent();
                handle.Reset();
            }
            catch
            {
                // Suppress
            }
        }

        /// <summary>
        /// Checks if the clean shutdown event has been signaled.
        /// Returns true if an authorized shutdown is in progress.
        /// </summary>
        public static bool IsCleanShutdownSignaled()
        {
            try
            {
                var handle = GetOrCreateShutdownEvent();
                return handle.WaitOne(0);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Checks if the AppBlocker.Service process is currently running.
        /// </summary>
        public static bool IsServiceProcessRunning()
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

        /// <summary>
        /// Checks if the AppBlocker.Watchdog process is currently running.
        /// </summary>
        public static bool IsWatchdogProcessRunning()
        {
            try
            {
                return Process.GetProcessesByName(WatchdogProcessName).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Launches the companion AppBlocker.Watchdog process targeting the specified PID.
        /// </summary>
        public Process? LaunchWatchdog(int targetPid)
        {
            try
            {
                var watchdogExePath = FindWatchdogExecutable();
                if (string.IsNullOrEmpty(watchdogExePath) || !File.Exists(watchdogExePath))
                {
                    Log($"Watchdog executable not found. Searched base path: {AppDomain.CurrentDomain.BaseDirectory}");
                    return null;
                }

                Log($"Launching companion Watchdog from '{watchdogExePath}' targeting PID {targetPid}...");
                var psi = new ProcessStartInfo
                {
                    FileName = watchdogExePath,
                    Arguments = $"--target-pid {targetPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    Log($"Watchdog companion launched successfully with PID {proc.Id}.");
                }
                return proc;
            }
            catch (Exception ex)
            {
                Log($"Failed to launch Watchdog process: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Revives the AppBlocker background service when an unexpected termination is detected.
        /// First attempts to start the registered Windows Service via sc.exe start AppBlockerService.
        /// If not running as a registered Windows Service, attempts direct executable launch.
        /// </summary>
        public bool ReviveService()
        {
            Log("Tamper attempt detected! Reviving AppBlocker Service immediately...");

            // 1. Attempt starting Windows Service via sc.exe
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"start {ServiceName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                proc?.WaitForExit(3000);

                // Wait briefly and verify if service process is now alive
                Thread.Sleep(500);
                if (IsServiceProcessRunning())
                {
                    Log("AppBlocker Windows Service successfully revived via SCM.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log($"sc.exe service start attempt failed: {ex.Message}");
            }

            // 2. Fallback: Launch AppBlocker.Service.exe directly if sc.exe failed (e.g. standalone/debug mode)
            try
            {
                var serviceExePath = FindServiceExecutable();
                if (!string.IsNullOrEmpty(serviceExePath) && File.Exists(serviceExePath))
                {
                    Log($"Starting AppBlocker.Service executable directly from '{serviceExePath}'...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = serviceExePath,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                    Thread.Sleep(500);
                    return IsServiceProcessRunning();
                }
            }
            catch (Exception ex)
            {
                Log($"Direct service executable revival failed: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// Terminates any lingering Watchdog processes cleanly during an authorized shutdown.
        /// </summary>
        public static void TerminateAllWatchdogs()
        {
            try
            {
                var watchdogs = Process.GetProcessesByName(WatchdogProcessName);
                foreach (var w in watchdogs)
                {
                    try
                    {
                        w.Kill(entireProcessTree: true);
                        w.Dispose();
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static string? FindWatchdogExecutable()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Direct alongside current binary
            var direct = Path.Combine(baseDir, "AppBlocker.Watchdog.exe");
            if (File.Exists(direct)) return direct;

            // Relative path in workspace (Debug/Release build layout)
            var devCandidate = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\AppBlocker.Watchdog\bin\Debug\net9.0\AppBlocker.Watchdog.exe"));
            if (File.Exists(devCandidate)) return devCandidate;

            // Parallel directory layout
            var parallelCandidate = Path.GetFullPath(Path.Combine(baseDir, @"..\AppBlocker.Watchdog\AppBlocker.Watchdog.exe"));
            if (File.Exists(parallelCandidate)) return parallelCandidate;

            return null;
        }

        public static string? FindServiceExecutable()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Direct alongside current binary
            var direct = Path.Combine(baseDir, "AppBlocker.Service.exe");
            if (File.Exists(direct)) return direct;

            // Relative path in workspace (Debug build layout)
            var devCandidate = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\AppBlocker.Service\bin\Debug\net9.0\AppBlocker.Service.exe"));
            if (File.Exists(devCandidate)) return devCandidate;

            // Parallel directory layout
            var parallelCandidate = Path.GetFullPath(Path.Combine(baseDir, @"..\AppBlocker.Service\AppBlocker.Service.exe"));
            if (File.Exists(parallelCandidate)) return parallelCandidate;

            return null;
        }
    }
}
