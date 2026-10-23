using System;

namespace AppBlocker.UI.Models
{
    public enum NotificationType
    {
        AppBlocked,
        WebBlocked,
        FocusStarted,
        FocusCompleted,
        TamperAlert,
        SystemInfo
    }

    public class ToastNotificationModel
    {
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string CategoryBadge { get; set; } = "GUARDIAN";
        public string IconEmoji { get; set; } = "🛡️";
        public string AccentColorHex { get; set; } = "#8B5CF6";
        public NotificationType Type { get; set; } = NotificationType.SystemInfo;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public string FormattedTime => Timestamp.ToLocalTime().ToString("HH:mm:ss");

        public static ToastNotificationModel CreateAppBlocked(string processName, string scheduleType, int pid = 0)
        {
            return new ToastNotificationModel
            {
                Title = "Restricted App Terminated",
                Message = $"'{processName}' was closed by active policy (Schedule: {scheduleType}).",
                CategoryBadge = "BLOCKED APP",
                IconEmoji = "🚫",
                AccentColorHex = "#EF4444",
                Type = NotificationType.AppBlocked,
                Timestamp = DateTime.UtcNow
            };
        }

        public static ToastNotificationModel CreateWebBlocked(string domain, string? redirectUrl = null)
        {
            var msg = string.IsNullOrWhiteSpace(redirectUrl)
                ? $"Access to '{domain}' was restricted by active web rules."
                : $"'{domain}' was blocked and rerouted to focus destination.";

            return new ToastNotificationModel
            {
                Title = "Restricted Website Intercepted",
                Message = msg,
                CategoryBadge = "BLOCKED WEB",
                IconEmoji = "🌐",
                AccentColorHex = "#06B6D4",
                Type = NotificationType.WebBlocked,
                Timestamp = DateTime.UtcNow
            };
        }

        public static ToastNotificationModel CreateFocusStarted(string sessionName, int durationMinutes, bool isStrict)
        {
            var strictLabel = isStrict ? " (Strict Nuclear Mode)" : "";
            return new ToastNotificationModel
            {
                Title = "Focus Sprint Active",
                Message = $"{sessionName} started for {durationMinutes} minutes{strictLabel}. Stay focused!",
                CategoryBadge = "FOCUS SPRINT",
                IconEmoji = "🎯",
                AccentColorHex = "#8B5CF6",
                Type = NotificationType.FocusStarted,
                Timestamp = DateTime.UtcNow
            };
        }

        public static ToastNotificationModel CreateFocusCompleted(string sessionName)
        {
            return new ToastNotificationModel
            {
                Title = "Focus Sprint Completed! 🏆",
                Message = $"{sessionName} finished! Excellent work maintaining uninterrupted focus.",
                CategoryBadge = "FOCUS COMPLETE",
                IconEmoji = "🏆",
                AccentColorHex = "#10B981",
                Type = NotificationType.FocusCompleted,
                Timestamp = DateTime.UtcNow
            };
        }

        public static ToastNotificationModel CreateTamperAlert(string detail)
        {
            return new ToastNotificationModel
            {
                Title = "Anti-Tamper Shield Alert",
                Message = detail,
                CategoryBadge = "TAMPER DETECTED",
                IconEmoji = "🛡️",
                AccentColorHex = "#F59E0B",
                Type = NotificationType.TamperAlert,
                Timestamp = DateTime.UtcNow
            };
        }

        public static ToastNotificationModel CreateInfo(string title, string message)
        {
            return new ToastNotificationModel
            {
                Title = title,
                Message = message,
                CategoryBadge = "SYSTEM",
                IconEmoji = "ℹ️",
                AccentColorHex = "#3B82F6",
                Type = NotificationType.SystemInfo,
                Timestamp = DateTime.UtcNow
            };
        }
    }
}
