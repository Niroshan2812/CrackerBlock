using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using AppBlocker.Core.Models;
using AppBlocker.UI.Models;
using AppBlocker.UI.Views;

namespace AppBlocker.UI.Services
{
    public class NotificationService
    {
        private static readonly Lazy<NotificationService> _instance = new(() => new NotificationService());
        public static NotificationService Instance => _instance.Value;

        private readonly ConcurrentDictionary<string, DateTime> _recentNotifications = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<ToastNotificationWindow> _activeToastWindows = new();
        private readonly object _windowsLock = new();

        private SystemTrayService? _trayService;

        public bool IsToastEnabled { get; set; } = true;
        public bool IsSoundEnabled { get; set; } = true;
        public bool IsBalloonEnabled { get; set; } = true;

        public NotificationService()
        {
        }

        public void SetTrayService(SystemTrayService trayService)
        {
            _trayService = trayService;
        }

        public bool ShowNotification(ToastNotificationModel notification)
        {
            if (notification == null)
            {
                return false;
            }

            // 1. Rate-limiting & deduplication (3.0 seconds window for identical alerts)
            var dedupeKey = $"{notification.Type}:{notification.Title}:{notification.Message}";
            var now = DateTime.UtcNow;

            if (_recentNotifications.TryGetValue(dedupeKey, out var lastTime))
            {
                if (now - lastTime < TimeSpan.FromSeconds(3.0))
                {
                    return false; // Suppressed duplicate
                }
            }

            _recentNotifications[dedupeKey] = now;

            // Prune older dedupe entries (> 15 seconds old)
            foreach (var kvp in _recentNotifications.Where(kvp => now - kvp.Value > TimeSpan.FromSeconds(15)).ToList())
            {
                _recentNotifications.TryRemove(kvp.Key, out _);
            }

            // 2. Play subtle notification sound on background task
            if (IsSoundEnabled)
            {
                Task.Run(() =>
                {
                    try
                    {
                        if (notification.Type == NotificationType.TamperAlert || notification.Type == NotificationType.AppBlocked)
                        {
                            SystemSounds.Exclamation.Play();
                        }
                        else
                        {
                            SystemSounds.Asterisk.Play();
                        }
                    }
                    catch { }
                });
            }

            // 3. Trigger Windows System Tray balloon tip
            if (IsBalloonEnabled && _trayService != null)
            {
                var icon = notification.Type switch
                {
                    NotificationType.TamperAlert => ToolTipIcon.Warning,
                    NotificationType.AppBlocked => ToolTipIcon.Error,
                    _ => ToolTipIcon.Info
                };
                _trayService.ShowNotification(notification.Title, notification.Message, icon);
            }

            // 4. Pop up non-activating modern desktop Toast HUD window
            if (IsToastEnabled)
            {
                ShowDesktopToast(notification);
            }

            return true;
        }

        private void ShowDesktopToast(ToastNotificationModel notification)
        {
            var app = System.Windows.Application.Current;
            if (app == null)
            {
                return;
            }

            app.Dispatcher.Invoke(() =>
            {
                try
                {
                    lock (_windowsLock)
                    {
                        // Limit maximum simultaneous open toasts to 3
                        while (_activeToastWindows.Count >= 3)
                        {
                            var oldest = _activeToastWindows[0];
                            _activeToastWindows.RemoveAt(0);
                            oldest.CloseWithAnimation();
                        }

                        double offset = _activeToastWindows.Count * 115.0;
                        var toast = new ToastNotificationWindow(notification, offset);

                        toast.Closed += (s, e) =>
                        {
                            lock (_windowsLock)
                            {
                                _activeToastWindows.Remove(toast);
                            }
                        };

                        _activeToastWindows.Add(toast);
                        toast.Show();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error displaying desktop toast: {ex.Message}");
                }
            });
        }

        public void ShowTestNotification(int sampleIndex = 0)
        {
            var testCases = new List<ToastNotificationModel>
            {
                ToastNotificationModel.CreateAppBlocked("discord", "Always", 1420),
                ToastNotificationModel.CreateWebBlocked("youtube.com", "https://docs.microsoft.com"),
                ToastNotificationModel.CreateFocusStarted("Pomodoro Sprint", 25, isStrict: true),
                ToastNotificationModel.CreateFocusCompleted("Pomodoro Sprint"),
                ToastNotificationModel.CreateTamperAlert("Unauthorized taskkill command attempted against guardian. Watchdog restored service.")
            };

            var selected = testCases[Math.Abs(sampleIndex) % testCases.Count];
            ShowNotification(selected);
        }

        public static ToastNotificationModel? ParseLogToNotification(SystemLog log)
        {
            if (log == null || string.IsNullOrWhiteSpace(log.EventType))
            {
                return null;
            }

            var type = log.EventType.Trim();
            var desc = log.Description ?? string.Empty;

            if (type.Equals("Blocked App", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(desc, @"'([^']+)'");
                var proc = match.Success ? match.Groups[1].Value : "Restricted App";
                var schedMatch = Regex.Match(desc, @"Schedule:\s*([^\.]+)");
                var schedule = schedMatch.Success ? schedMatch.Groups[1].Value.Trim() : "Always";

                return ToastNotificationModel.CreateAppBlocked(proc, schedule);
            }

            if (type.Equals("Blocked Web", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(desc, @"'([^']+)'");
                var domain = match.Success ? match.Groups[1].Value : "Restricted Domain";
                var redirMatch = Regex.Match(desc, @"Redirected to:\s*'([^']+)'");
                var redirect = redirMatch.Success ? redirMatch.Groups[1].Value : null;

                return ToastNotificationModel.CreateWebBlocked(domain, redirect);
            }

            if (type.Contains("Focus", StringComparison.OrdinalIgnoreCase))
            {
                if (type.Contains("Started", StringComparison.OrdinalIgnoreCase) || desc.Contains("Started", StringComparison.OrdinalIgnoreCase))
                {
                    var nameMatch = Regex.Match(desc, @"'([^']+)'");
                    var name = nameMatch.Success ? nameMatch.Groups[1].Value : "Focus Sprint";
                    var durMatch = Regex.Match(desc, @"for\s+(\d+)m");
                    int duration = durMatch.Success && int.TryParse(durMatch.Groups[1].Value, out var m) ? m : 25;
                    bool strict = desc.Contains("Strict", StringComparison.OrdinalIgnoreCase) || desc.Contains("Nuclear", StringComparison.OrdinalIgnoreCase);

                    return ToastNotificationModel.CreateFocusStarted(name, duration, strict);
                }

                if (type.Contains("Completed", StringComparison.OrdinalIgnoreCase) || desc.Contains("Completed", StringComparison.OrdinalIgnoreCase))
                {
                    var nameMatch = Regex.Match(desc, @"'([^']+)'");
                    var name = nameMatch.Success ? nameMatch.Groups[1].Value : "Focus Sprint";

                    return ToastNotificationModel.CreateFocusCompleted(name);
                }
            }

            if (type.Contains("Tamper", StringComparison.OrdinalIgnoreCase) || type.Contains("Failed PIN", StringComparison.OrdinalIgnoreCase))
            {
                return ToastNotificationModel.CreateTamperAlert(desc);
            }

            if (type.Equals("Blocking Resumed", StringComparison.OrdinalIgnoreCase))
            {
                return ToastNotificationModel.CreateInfo("Protection Resumed", "App & Web blocking enforcement is active.");
            }

            return null;
        }
    }
}
