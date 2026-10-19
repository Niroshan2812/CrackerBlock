using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Models;
using Xunit;

namespace AppBlocker.Tests
{
    public class AppLockingEngineTests : IDisposable
    {
        private readonly string _testDbPath;

        public AppLockingEngineTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"appblocker_engine_test_{Guid.NewGuid():N}.db");
            using var db = new AppBlockerDbContext(_testDbPath);
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
            }
            catch { }
        }

        [Fact]
        public void ProcessSafetyGuard_NeverAllowsKillingSystemAndDevTools()
        {
            Assert.True(ProcessSafetyGuard.IsProtected("explorer"));
            Assert.True(ProcessSafetyGuard.IsProtected("explorer.exe"));
            Assert.True(ProcessSafetyGuard.IsProtected("taskmgr"));
            Assert.True(ProcessSafetyGuard.IsProtected("svchost"));
            Assert.True(ProcessSafetyGuard.IsProtected("csrss"));
            Assert.True(ProcessSafetyGuard.IsProtected("winlogon"));
            Assert.True(ProcessSafetyGuard.IsProtected("services"));
            Assert.True(ProcessSafetyGuard.IsProtected("cmd"));
            Assert.True(ProcessSafetyGuard.IsProtected("powershell"));
            Assert.True(ProcessSafetyGuard.IsProtected("pwsh"));
            Assert.True(ProcessSafetyGuard.IsProtected("dotnet"));
            Assert.True(ProcessSafetyGuard.IsProtected("code"));
            Assert.True(ProcessSafetyGuard.IsProtected("antigravity"));
            Assert.True(ProcessSafetyGuard.IsProtected("AppBlocker.UI"));
            Assert.True(ProcessSafetyGuard.IsProtected("AppBlocker.Service"));

            // Non-protected apps can be blocked
            Assert.False(ProcessSafetyGuard.IsProtected("calc"));
            Assert.False(ProcessSafetyGuard.IsProtected("notepad"));
            Assert.False(ProcessSafetyGuard.IsProtected("spotify"));
            Assert.False(ProcessSafetyGuard.IsProtected("discord"));
            Assert.False(ProcessSafetyGuard.IsProtected("steam"));
        }

        [Fact]
        public void AppLockingEngine_TemporaryUnlock_PreventsBlockingUntilExpired()
        {
            var engine = new AppLockingEngine();

            Assert.False(engine.IsTemporarilyUnlocked("spotify"));

            // Unlock for 5 minutes
            engine.TemporaryUnlockApp("spotify", TimeSpan.FromMinutes(5));
            Assert.True(engine.IsTemporarilyUnlocked("spotify"));
            Assert.True(engine.IsTemporarilyUnlocked("spotify.exe")); // Case and extension insensitive

            // Test immediate expiry
            engine.TemporaryUnlockApp("spotify", TimeSpan.FromMilliseconds(-100));
            Assert.False(engine.IsTemporarilyUnlocked("spotify"));
        }

        [Fact]
        public async Task AppLockingEngine_PausedState_SkipsScanning()
        {
            var engine = new AppLockingEngine();
            engine.IsPaused = true;

            var terminated = await engine.RunScanCycleAsync();
            Assert.Equal(0, terminated);
        }

        [Fact]
        public async Task AppLockingEngine_SafelyKillsTargetedIsolatedTestProcess()
        {
            // Spawn a benign child process solely created for this test: ping 127.0.0.1 -n 30
            var startInfo = new ProcessStartInfo
            {
                FileName = "PING.EXE",
                Arguments = "127.0.0.1 -n 30",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process? testProcess = null;
            try
            {
                testProcess = Process.Start(startInfo);
                Assert.NotNull(testProcess);
                Assert.False(testProcess.HasExited);

                var engine = new AppLockingEngine();
                // Configure rule targeting this benign child process
                engine.SetRules(new[]
                {
                    new AppRule
                    {
                        ProcessName = "ping",
                        IsActive = true,
                        ScheduleType = ScheduleTypes.Always
                    }
                });

                bool eventFired = false;
                engine.ProcessTerminated += (name, pid, sched) =>
                {
                    if (name.Equals("ping", StringComparison.OrdinalIgnoreCase))
                    {
                        eventFired = true;
                    }
                };

                // Execute one scan cycle
                var terminated = await engine.RunScanCycleAsync();

                Assert.True(terminated >= 1);
                Assert.True(eventFired);
                Assert.True(testProcess.HasExited);
            }
            finally
            {
                try
                {
                    if (testProcess != null && !testProcess.HasExited)
                    {
                        testProcess.Kill();
                    }
                    testProcess?.Dispose();
                }
                catch { }
            }
        }
    }
}
