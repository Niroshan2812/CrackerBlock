using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using AppBlocker.UI.Models;
using AppBlocker.UI.Services;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class NotificationServiceTests : IDisposable
    {
        private readonly string _testDbPath;

        public NotificationServiceTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"notifications_test_{Guid.NewGuid():N}.db");
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
        public void ParseLogToNotification_CorrectlyMapsAllEventTypes()
        {
            // 1. Blocked App
            var appLog = new SystemLog
            {
                EventType = "Blocked App",
                Description = "Terminated restricted process 'discord' (PID: 1024). Schedule: Custom Work Window."
            };
            var appToast = NotificationService.ParseLogToNotification(appLog);
            Assert.NotNull(appToast);
            Assert.Equal(NotificationType.AppBlocked, appToast!.Type);
            Assert.Contains("discord", appToast.Message);
            Assert.Contains("Custom Work Window", appToast.Message);
            Assert.Equal("🚫", appToast.IconEmoji);
            Assert.Equal("#EF4444", appToast.AccentColorHex);
            Assert.Equal("BLOCKED APP", appToast.CategoryBadge);

            // 2. Blocked Web
            var webLog = new SystemLog
            {
                EventType = "Blocked Web",
                Description = "Intercepted 'reddit.com' -> Redirected to: 'https://docs.microsoft.com'"
            };
            var webToast = NotificationService.ParseLogToNotification(webLog);
            Assert.NotNull(webToast);
            Assert.Equal(NotificationType.WebBlocked, webToast!.Type);
            Assert.Contains("reddit.com", webToast.Message);
            Assert.Equal("🌐", webToast.IconEmoji);
            Assert.Equal("#06B6D4", webToast.AccentColorHex);

            // 3. Focus Session Started
            var focusStartLog = new SystemLog
            {
                EventType = "Focus Session Started",
                Description = "Started 'Deep Work' for 50m. Strict Nuclear Mode: ON."
            };
            var focusStartToast = NotificationService.ParseLogToNotification(focusStartLog);
            Assert.NotNull(focusStartToast);
            Assert.Equal(NotificationType.FocusStarted, focusStartToast!.Type);
            Assert.Contains("Deep Work", focusStartToast.Message);
            Assert.Contains("50 minutes", focusStartToast.Message);
            Assert.Contains("Nuclear Mode", focusStartToast.Message);
            Assert.Equal("🎯", focusStartToast.IconEmoji);

            // 4. Focus Session Completed
            var focusEndLog = new SystemLog
            {
                EventType = "Focus Session Completed",
                Description = "Session 'Deep Work' concluded successfully."
            };
            var focusEndToast = NotificationService.ParseLogToNotification(focusEndLog);
            Assert.NotNull(focusEndToast);
            Assert.Equal(NotificationType.FocusCompleted, focusEndToast!.Type);
            Assert.Contains("Deep Work", focusEndToast.Message);
            Assert.Equal("🏆", focusEndToast.IconEmoji);
            Assert.Equal("#10B981", focusEndToast.AccentColorHex);

            // 5. Tamper Alert
            var tamperLog = new SystemLog
            {
                EventType = "Tamper Detected",
                Description = "Worker process terminated abruptly. Dual-process watchdog resurrected service."
            };
            var tamperToast = NotificationService.ParseLogToNotification(tamperLog);
            Assert.NotNull(tamperToast);
            Assert.Equal(NotificationType.TamperAlert, tamperToast!.Type);
            Assert.Contains("watchdog", tamperToast.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("🛡️", tamperToast.IconEmoji);
            Assert.Equal("#F59E0B", tamperToast.AccentColorHex);

            // 6. Unknown / Unrelated Log
            var genericLog = new SystemLog
            {
                EventType = "Internal Debug",
                Description = "Routine heartbeat check."
            };
            var nullToast = NotificationService.ParseLogToNotification(genericLog);
            Assert.Null(nullToast);
        }

        [Fact]
        public void NotificationService_DeduplicationAndThrottling_SuppressesImmediateDuplicates()
        {
            var service = new NotificationService
            {
                IsToastEnabled = false, // Unit test mode (don't show WPF windows)
                IsSoundEnabled = false,
                IsBalloonEnabled = false
            };

            var alert = ToastNotificationModel.CreateAppBlocked("steam", "Always", 555);

            // First attempt should succeed
            bool first = service.ShowNotification(alert);
            Assert.True(first);

            // Immediate duplicate of the exact same alert should be throttled
            bool duplicate = service.ShowNotification(alert);
            Assert.False(duplicate);

            // Different alert should NOT be blocked by deduplication
            var differentAlert = ToastNotificationModel.CreateWebBlocked("tiktok.com");
            bool different = service.ShowNotification(differentAlert);
            Assert.True(different);
        }

        [Fact]
        public async Task AuditLogNotificationWatcher_PicksUpNewLogs_AndUpdatesLastSeenId()
        {
            // Seed 1 initial log into DB
            await using (var db = new AppBlockerDbContext(_testDbPath))
            {
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-5),
                    EventType = "Initial State",
                    Description = "Historical log entry that should not pop a notification."
                });
                await db.SaveChangesAsync();
            }

            var service = new NotificationService
            {
                IsToastEnabled = false,
                IsSoundEnabled = false,
                IsBalloonEnabled = false
            };

            var watcher = new AuditLogNotificationWatcher(
                service,
                () => new AppBlockerDbContext(_testDbPath),
                pollingIntervalMs: 100);

            // Start sets baseline
            await watcher.StartAsync();
            int baselineId = watcher.LastSeenLogId;
            Assert.True(baselineId > 0);

            // Add a new "Blocked App" log
            await using (var db = new AppBlockerDbContext(_testDbPath))
            {
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Blocked App",
                    Description = "Terminated restricted process 'game.exe' (PID: 999). Schedule: Always."
                });
                await db.SaveChangesAsync();
            }

            // Process new logs once
            int processed = await watcher.ProcessNewLogsOnceAsync();
            Assert.Equal(1, processed);
            Assert.True(watcher.LastSeenLogId > baselineId);

            // Calling again without new logs processes 0
            int processedSecond = await watcher.ProcessNewLogsOnceAsync();
            Assert.Equal(0, processedSecond);

            await watcher.StopAsync();
        }

        [Fact]
        public void NotificationPreferences_TogglesPropertiesCorrectly()
        {
            var service = NotificationService.Instance;
            service.IsToastEnabled = true;
            service.IsSoundEnabled = true;
            service.IsBalloonEnabled = true;

            Assert.True(service.IsToastEnabled);
            Assert.True(service.IsSoundEnabled);
            Assert.True(service.IsBalloonEnabled);

            service.IsToastEnabled = false;
            service.IsSoundEnabled = false;
            service.IsBalloonEnabled = false;

            Assert.False(service.IsToastEnabled);
            Assert.False(service.IsSoundEnabled);
            Assert.False(service.IsBalloonEnabled);

            // Restore defaults
            service.IsToastEnabled = true;
            service.IsSoundEnabled = true;
            service.IsBalloonEnabled = true;
        }
    }
}
