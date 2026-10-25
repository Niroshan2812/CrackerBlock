using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using AppBlocker.Core.Data;
using AppBlocker.Core.IPC;
using AppBlocker.Core.Models;
using AppBlocker.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.UI.ViewModels
{
    public class ScheduleDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public string TimeWindow { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsActiveNow { get; set; }
    }

    public class CustomScheduleDisplayItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TimeWindow => $"{StartTime} - {EndTime}";
        public string StartTime { get; set; } = "09:00";
        public string EndTime { get; set; } = "17:00";
        public string DaysOfWeek { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public bool IsActiveNow { get; set; }
        public string LinkedTargetsSummary { get; set; } = string.Empty;
        public int LinkedTargetsCount { get; set; }
    }

    public partial class SchedulesViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;
        private readonly DispatcherTimer _timer;
        private int _remainingSeconds = 0;
        private int _totalSeconds = 1500;
        private int _syncCounter = 0;

        // 1. Local Clock
        [ObservableProperty]
        private string _currentTimeString = string.Empty;

        [ObservableProperty]
        private string _currentDateString = string.Empty;

        // 2. Pomodoro Focus Session State
        [ObservableProperty]
        private bool _isFocusActive;

        [ObservableProperty]
        private string _focusSessionName = "Pomodoro Sprint";

        [ObservableProperty]
        private int _focusDurationMinutes = 25;

        [ObservableProperty]
        private bool _isStrictNuclearMode;

        public bool CanEndFocusSession => !(IsFocusActive && IsStrictNuclearMode);

        public string EndFocusButtonContent => (IsFocusActive && IsStrictNuclearMode)
            ? "🔒 NUCLEAR LOCK ENGAGED (Session Cannot Be Stopped)"
            : "⏹️ End Focus Session";

        partial void OnIsFocusActiveChanged(bool value)
        {
            OnPropertyChanged(nameof(CanEndFocusSession));
            OnPropertyChanged(nameof(EndFocusButtonContent));
        }

        partial void OnIsStrictNuclearModeChanged(bool value)
        {
            OnPropertyChanged(nameof(CanEndFocusSession));
            OnPropertyChanged(nameof(EndFocusButtonContent));
        }

        [ObservableProperty]
        private string _remainingTimeDisplay = "25:00";

        [ObservableProperty]
        private double _progressPercentage = 100.0;

        [ObservableProperty]
        private string _focusStatusMessage = "Ready for your next focus sprint.";

        [ObservableProperty]
        private bool _isFocusError;

        // 3. Custom Schedule Creation State
        [ObservableProperty]
        private string _newScheduleName = string.Empty;

        [ObservableProperty]
        private string _newScheduleStartTime = "09:00";

        [ObservableProperty]
        private string _newScheduleEndTime = "17:00";

        [ObservableProperty]
        private bool _dayMon = true;

        [ObservableProperty]
        private bool _dayTue = true;

        [ObservableProperty]
        private bool _dayWed = true;

        [ObservableProperty]
        private bool _dayThu = true;

        [ObservableProperty]
        private bool _dayFri = true;

        [ObservableProperty]
        private bool _daySat = false;

        [ObservableProperty]
        private bool _daySun = false;

        [ObservableProperty]
        private string _scheduleStatusMessage = string.Empty;

        // 4. Observable Collections
        [ObservableProperty]
        private ObservableCollection<ScheduleDisplayItem> _presetScheduleItems = new();

        [ObservableProperty]
        private ObservableCollection<CustomScheduleDisplayItem> _customScheduleItems = new();

        public SchedulesViewModel(IpcClientService? ipcClient = null)
        {
            _ipcClient = ipcClient ?? new IpcClientService();

            UpdateClock();
            RefreshPresets();

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += async (s, e) => await OnTimerTickAsync();
            _timer.Start();

            // Initial load of custom schedules & focus status
            Task.Run(async () =>
            {
                await RefreshCustomSchedulesAsync();
                await SyncFocusStatusAsync();
            });
        }

        private async Task OnTimerTickAsync()
        {
            UpdateClock();

            // Local countdown interpolation
            if (IsFocusActive && _remainingSeconds > 0)
            {
                _remainingSeconds--;
                int mins = _remainingSeconds / 60;
                int secs = _remainingSeconds % 60;
                RemainingTimeDisplay = $"{mins:D2}:{secs:D2}";
                ProgressPercentage = _totalSeconds > 0 ? (double)_remainingSeconds / _totalSeconds * 100.0 : 0;

                if (_remainingSeconds <= 0)
                {
                    IsFocusActive = false;
                    RemainingTimeDisplay = "00:00";
                    ProgressPercentage = 0;
                    FocusStatusMessage = "🎉 Focus sprint completed! Fantastic work staying disciplined.";
                }
            }

            // Sync with IPC service every 5 seconds or immediately on start
            _syncCounter++;
            if (_syncCounter >= 5)
            {
                _syncCounter = 0;
                await SyncFocusStatusAsync();
                await RefreshCustomSchedulesAsync();
            }

            RefreshPresets();
        }

        public async Task SyncFocusStatusAsync()
        {
            try
            {
                var status = await _ipcClient.GetFocusSessionStatusAsync();
                if (status != null)
                {
                    IsFocusActive = status.IsActive;
                    IsStrictNuclearMode = status.IsStrictNuclearMode;
                    FocusSessionName = status.SessionName;

                    if (status.IsActive)
                    {
                        _remainingSeconds = status.RemainingSeconds;
                        _totalSeconds = status.TotalSeconds > 0 ? status.TotalSeconds : 1500;
                        int mins = _remainingSeconds / 60;
                        int secs = _remainingSeconds % 60;
                        RemainingTimeDisplay = $"{mins:D2}:{secs:D2}";
                        ProgressPercentage = _totalSeconds > 0 ? (double)_remainingSeconds / _totalSeconds * 100.0 : 0;
                        FocusStatusMessage = status.IsStrictNuclearMode 
                            ? "🔒 STRICT NUCLEAR LOCKDOWN ACTIVE — Pause & unlock controls disabled." 
                            : "⚡ Focus Sprint Active — All restrictions actively guarded.";
                    }
                }
            }
            catch { }
        }

        public async Task RefreshCustomSchedulesAsync()
        {
            try
            {
                var list = await _ipcClient.GetCustomSchedulesAsync();

                // Query database to find which Apps and Websites are linked to each schedule
                List<AppRule> appRules = new();
                List<WebRule> webRules = new();
                try
                {
                    await using var db = AppBlockerDbContext.CreateContext();
                    appRules = await db.AppRules.ToListAsync();
                    webRules = await db.WebRules.ToListAsync();
                }
                catch { }

                var items = new ObservableCollection<CustomScheduleDisplayItem>();
                foreach (var s in list)
                {
                    var linkedApps = appRules
                        .Where(a => a.ScheduleType.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(a => a.ProcessName)
                        .ToList();

                    var linkedWebs = webRules
                        .Where(w => w.ScheduleType.Equals(s.Name, StringComparison.OrdinalIgnoreCase))
                        .Select(w => w.UrlOrKeyword)
                        .ToList();

                    var totalLinked = linkedApps.Count + linkedWebs.Count;
                    string summary;
                    if (totalLinked > 0)
                    {
                        var parts = new List<string>();
                        if (linkedApps.Count > 0) parts.Add($"💻 Apps: {string.Join(", ", linkedApps)}");
                        if (linkedWebs.Count > 0) parts.Add($"🌐 Web: {string.Join(", ", linkedWebs)}");
                        summary = "🎯 " + string.Join(" | ", parts);
                    }
                    else
                    {
                        summary = "ℹ️ No rules assigned yet. Select this schedule when adding an App or Website.";
                    }

                    items.Add(new CustomScheduleDisplayItem
                    {
                        Id = s.Id,
                        Name = s.Name,
                        StartTime = s.StartTime,
                        EndTime = s.EndTime,
                        DaysOfWeek = s.DaysOfWeek,
                        IsActive = s.IsActive,
                        IsActiveNow = s.IsActiveNow,
                        LinkedTargetsSummary = summary,
                        LinkedTargetsCount = totalLinked
                    });
                }
                CustomScheduleItems = items;
            }
            catch { }
        }

        private void UpdateClock()
        {
            var now = DateTime.Now;
            CurrentTimeString = now.ToString("hh:mm:ss tt");
            CurrentDateString = now.ToString("dddd, MMMM dd, yyyy");
        }

        private void RefreshPresets()
        {
            var now = DateTime.Now;
            var items = new ObservableCollection<ScheduleDisplayItem>
            {
                new ScheduleDisplayItem
                {
                    Name = "Always",
                    TimeWindow = "24 / 7",
                    Description = "Rules are constantly enforced without interruption.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.Always, now, null, IsFocusActive)
                },
                new ScheduleDisplayItem
                {
                    Name = "Work Hours",
                    TimeWindow = "Mon - Fri, 09:00 AM - 05:00 PM",
                    Description = "Enforced during standard business work hours.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.WorkHours, now, null, IsFocusActive)
                },
                new ScheduleDisplayItem
                {
                    Name = "Weekdays Only",
                    TimeWindow = "Monday - Friday (All day)",
                    Description = "Active during weekdays, free during weekends.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.WeekdaysOnly, now, null, IsFocusActive)
                },
                new ScheduleDisplayItem
                {
                    Name = "Weekends Only",
                    TimeWindow = "Saturday & Sunday (All day)",
                    Description = "Active exclusively during the weekend.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.WeekendsOnly, now, null, IsFocusActive)
                },
                new ScheduleDisplayItem
                {
                    Name = "Evening Only",
                    TimeWindow = "Every day, 06:00 PM - 11:00 PM",
                    Description = "Enforces focus during study or evening hours.",
                    IsActiveNow = ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.EveningOnly, now, null, IsFocusActive)
                }
            };
            PresetScheduleItems = items;
        }

        [RelayCommand]
        private void SelectPreset(string minutesStr)
        {
            if (IsFocusActive) return;

            if (int.TryParse(minutesStr, out int minutes))
            {
                FocusDurationMinutes = minutes;
                _remainingSeconds = minutes * 60;
                _totalSeconds = _remainingSeconds;
                RemainingTimeDisplay = $"{minutes:D2}:00";
                ProgressPercentage = 100;
                FocusSessionName = minutes switch
                {
                    25 => "🍅 Pomodoro Sprint",
                    50 => "⚡ Deep Work Session",
                    90 => "🌊 Flow State",
                    _ => "Focus Session"
                };
            }
        }

        [RelayCommand]
        private async Task StartFocusSessionAsync()
        {
            IsFocusError = false;
            try
            {
                var resp = await _ipcClient.StartFocusSessionAsync(FocusDurationMinutes, IsStrictNuclearMode, FocusSessionName);
                if (resp.Success)
                {
                    IsFocusActive = true;
                    _remainingSeconds = FocusDurationMinutes * 60;
                    _totalSeconds = _remainingSeconds;
                    FocusStatusMessage = IsStrictNuclearMode 
                        ? "🔒 Strict Nuclear Lock engaged! Zero bypass until timer completes." 
                        : $"⚡ {FocusSessionName} started! Stay focused.";
                }
                else
                {
                    IsFocusError = true;
                    FocusStatusMessage = resp.Message ?? "Failed to start focus session.";
                }
            }
            catch (Exception ex)
            {
                IsFocusError = true;
                FocusStatusMessage = $"Error starting session: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task StopFocusSessionAsync()
        {
            if (IsStrictNuclearMode && IsFocusActive)
            {
                IsFocusError = true;
                FocusStatusMessage = "🔒 Strict Nuclear Lock is active! Sessions cannot be stopped early until the countdown completes.";
                return;
            }

            IsFocusError = false;
            try
            {
                var resp = await _ipcClient.StopFocusSessionAsync(force: false);
                if (resp.Success)
                {
                    IsFocusActive = false;
                    _remainingSeconds = 0;
                    RemainingTimeDisplay = $"{FocusDurationMinutes:D2}:00";
                    ProgressPercentage = 100;
                    FocusStatusMessage = "Focus session ended.";
                }
                else
                {
                    IsFocusError = true;
                    FocusStatusMessage = resp.Message ?? "Failed to stop session.";
                }
            }
            catch (Exception ex)
            {
                IsFocusError = true;
                FocusStatusMessage = $"Error stopping session: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task CreateCustomScheduleAsync()
        {
            ScheduleStatusMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(NewScheduleName))
            {
                ScheduleStatusMessage = "Please enter a schedule name (e.g. 'Morning Study').";
                return;
            }

            var days = new List<string>();
            if (DayMon) days.Add("Mon");
            if (DayTue) days.Add("Tue");
            if (DayWed) days.Add("Wed");
            if (DayThu) days.Add("Thu");
            if (DayFri) days.Add("Fri");
            if (DaySat) days.Add("Sat");
            if (DaySun) days.Add("Sun");

            if (days.Count == 0)
            {
                ScheduleStatusMessage = "Please select at least one day of the week.";
                return;
            }

            var dto = new CustomScheduleDto
            {
                Name = NewScheduleName.Trim(),
                StartTime = string.IsNullOrWhiteSpace(NewScheduleStartTime) ? "09:00" : NewScheduleStartTime.Trim(),
                EndTime = string.IsNullOrWhiteSpace(NewScheduleEndTime) ? "17:00" : NewScheduleEndTime.Trim(),
                DaysOfWeek = string.Join(",", days),
                IsActive = true
            };

            var resp = await _ipcClient.AddCustomScheduleAsync(dto);
            if (resp.Success)
            {
                ScheduleStatusMessage = $"Custom schedule '{dto.Name}' added successfully.";
                NewScheduleName = string.Empty;
                await RefreshCustomSchedulesAsync();
            }
            else
            {
                ScheduleStatusMessage = resp.Message ?? "Failed to add schedule.";
            }
        }

        [RelayCommand]
        private async Task DeleteCustomScheduleAsync(int id)
        {
            var resp = await _ipcClient.DeleteCustomScheduleAsync(id);
            if (resp.Success)
            {
                await RefreshCustomSchedulesAsync();
            }
        }

        [RelayCommand]
        private async Task ToggleCustomScheduleAsync(int id)
        {
            var resp = await _ipcClient.ToggleCustomScheduleAsync(id);
            if (resp.Success)
            {
                await RefreshCustomSchedulesAsync();
            }
        }
    }
}
