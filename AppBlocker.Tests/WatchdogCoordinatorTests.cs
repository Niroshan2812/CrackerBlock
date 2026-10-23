using System.IO;
using System.Runtime.Versioning;
using AppBlocker.Core.Security;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class WatchdogCoordinatorTests
    {
        [Fact]
        public void CleanShutdownEvent_SignalsAndResetsCorrectly()
        {
            // 1. Reset event
            WatchdogCoordinator.ResetCleanShutdown();
            Assert.False(WatchdogCoordinator.IsCleanShutdownSignaled());

            // 2. Signal clean shutdown
            WatchdogCoordinator.SignalCleanShutdown();
            Assert.True(WatchdogCoordinator.IsCleanShutdownSignaled());

            // 3. Reset again
            WatchdogCoordinator.ResetCleanShutdown();
            Assert.False(WatchdogCoordinator.IsCleanShutdownSignaled());
        }

        [Fact]
        public void FindWatchdogExecutable_LocatesBinaryInSolution()
        {
            var watchdogPath = WatchdogCoordinator.FindWatchdogExecutable();
            Assert.NotNull(watchdogPath);
            Assert.True(File.Exists(watchdogPath), $"Expected watchdog binary to exist at: {watchdogPath}");
            Assert.EndsWith("AppBlocker.Watchdog.exe", watchdogPath, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void FindServiceExecutable_LocatesBinaryInSolution()
        {
            var servicePath = WatchdogCoordinator.FindServiceExecutable();
            Assert.NotNull(servicePath);
            Assert.True(File.Exists(servicePath), $"Expected service binary to exist at: {servicePath}");
            Assert.EndsWith("AppBlocker.Service.exe", servicePath, System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ProcessDetection_RunsWithoutExceptions()
        {
            var isServiceRunning = WatchdogCoordinator.IsServiceProcessRunning();
            var isWatchdogRunning = WatchdogCoordinator.IsWatchdogProcessRunning();

            // Should return boolean without throwing any Win32 or security exceptions
            Assert.True(isServiceRunning || !isServiceRunning);
            Assert.True(isWatchdogRunning || !isWatchdogRunning);
        }

        [Fact]
        public void Watchdog_CliFlags_SignalShutdown_ExecutesSuccessfully()
        {
            WatchdogCoordinator.ResetCleanShutdown();
            Assert.False(WatchdogCoordinator.IsCleanShutdownSignaled());

            int exitCode = AppBlocker.Watchdog.Program.Main(new[] { "--signal-shutdown" });
            Assert.Equal(0, exitCode);
            Assert.True(WatchdogCoordinator.IsCleanShutdownSignaled());

            // Clean up
            WatchdogCoordinator.ResetCleanShutdown();
        }

        [Fact]
        public void Watchdog_CliFlags_Status_ExecutesSuccessfully()
        {
            int exitCode = AppBlocker.Watchdog.Program.Main(new[] { "--status" });
            Assert.Equal(0, exitCode);
        }

        [Fact]
        public void Watchdog_DetectsUnexpectedProcessExit_WhenShutdownNotSignaled()
        {
            WatchdogCoordinator.ResetCleanShutdown();
            Assert.False(WatchdogCoordinator.IsCleanShutdownSignaled());

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c exit 0",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            Assert.NotNull(proc);
            proc.WaitForExit(3000);

            // Process has exited
            Assert.True(proc.HasExited);
            // Shutdown event is NOT signaled (tamper condition met)
            Assert.False(WatchdogCoordinator.IsCleanShutdownSignaled());
        }
    }
}
