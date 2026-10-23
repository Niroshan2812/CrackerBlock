using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using AppBlocker.UI.ViewModels;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class AnalyticsAndLogsTests : IDisposable
    {
        private readonly string _testDbPath;

        public AnalyticsAndLogsTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"analytics_test_{Guid.NewGuid():N}.db");
            using var db = new AppBlockerDbContext(_testDbPath);
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath)) File.Delete(_testDbPath);
            }
            catch { }
        }

        [Fact]
        public async Task AnalyticsCalculation_AggregatesKpisAndTopDistractors()
        {
            // Seed sample test logs into database
            await using (var db = new AppBlockerDbContext(_testDbPath))
            {
                db.Logs.AddRange(
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Blocked App", Description = "Terminated restricted process: 'discord' (PID: 101). Schedule: Always" },
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Blocked App", Description = "Terminated restricted process: 'discord' (PID: 102). Schedule: Always" },
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Blocked App", Description = "Terminated restricted process: 'steam' (PID: 103). Schedule: Always" },
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Blocked Web", Description = "Restricted access to domain: 'youtube.com'" },
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Blocked Web", Description = "Intercepted 'reddit.com' -> Redirected to: 'https://docs.microsoft.com'" },
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Tamper Detected", Description = "Service was terminated abruptly. Watchdog revived." },
                    new SystemLog { Timestamp = DateTime.UtcNow, EventType = "Focus Session Started", Description = "Started 'Pomodoro Sprint' for 25m." }
                );
                await db.SaveChangesAsync();
            }

            var vm = new LogsViewModel(() => new AppBlockerDbContext(_testDbPath));
            await vm.LoadLogsAsync();

            // 1. Verify KPI metrics
            Assert.Equal(5, vm.TotalInterceptions);
            Assert.Equal(3, vm.TotalAppsBlocked);
            Assert.Equal(2, vm.TotalWebsBlocked);
            Assert.Equal(1, vm.TamperAttemptsCount);
            Assert.Equal("25m", vm.TimeSavedDisplay); // 5 * 5 = 25m
            Assert.NotEmpty(vm.FocusScoreDisplay);

            // 2. Verify Top Blocked Apps
            Assert.NotEmpty(vm.TopBlockedApps);
            var topApp = vm.TopBlockedApps.First();
            Assert.Equal("discord", topApp.Name);
            Assert.Equal(2, topApp.Count);

            // 3. Verify Top Blocked Websites
            Assert.NotEmpty(vm.TopBlockedWebsites);
            var domains = vm.TopBlockedWebsites.Select(w => w.Name).ToList();
            Assert.Contains("youtube.com", domains);
            Assert.Contains("reddit.com", domains);

            // 4. Test Category Filter for Blocked Apps
            await vm.SelectCategoryCommand.ExecuteAsync("Blocked Apps");
            Assert.All(vm.Logs, log => Assert.Equal("Blocked App", log.EventType));

            // 5. Test Category Filter for Blocked Web
            await vm.SelectCategoryCommand.ExecuteAsync("Blocked Web");
            Assert.All(vm.Logs, log => Assert.Equal("Blocked Web", log.EventType));

            // 6. Test Category Filter for Anti-Tamper
            await vm.SelectCategoryCommand.ExecuteAsync("Anti-Tamper");
            Assert.All(vm.Logs, log => Assert.Contains("Tamper", log.EventType));
        }
    }
}
