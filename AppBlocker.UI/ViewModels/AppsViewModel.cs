using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using AppBlocker.Core.Data;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Models;
using AppBlocker.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace AppBlocker.UI.ViewModels
{
    public partial class AppRuleItem : ObservableObject
    {
        public AppRule Rule { get; }

        public int Id => Rule.Id;
        public string ProcessName => Rule.ProcessName;
        public string? ExePath => Rule.ExePath;
        public bool IsActive => Rule.IsActive;
        public string ScheduleType => Rule.ScheduleType;
        public DateTime CreatedAt => Rule.CreatedAt;

        [ObservableProperty]
        private bool _isTemporarilyUnlocked;

        [ObservableProperty]
        private string _unlockButtonText = "⏱️ Unlock 15m";

        [ObservableProperty]
        private int _customDurationMinutes = 15;

        public DateTime? UnlockExpiresAt { get; set; }

        public AppRuleItem(AppRule rule)
        {
            Rule = rule;
        }

        public void UpdateUnlockState(DateTime? expiry)
        {
            UnlockExpiresAt = expiry;
            if (expiry.HasValue && expiry.Value > DateTime.UtcNow)
            {
                IsTemporarilyUnlocked = true;
                var remaining = expiry.Value - DateTime.UtcNow;
                int mins = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                UnlockButtonText = $"🔓 Unlocked ({mins}m) • Relock";
            }
            else
            {
                IsTemporarilyUnlocked = false;
                UnlockButtonText = "⏱️ Unlock 15m";
            }
        }
    }

    public partial class AppsViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;
        private readonly Dictionary<string, DateTime> _temporaryUnlocks = new(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _timer;
        private CancellationTokenSource? _statusCts;

        [ObservableProperty]
        private ObservableCollection<AppRuleItem> _rules = new();

        [ObservableProperty]
        private string _newProcessName = string.Empty;

        [ObservableProperty]
        private string? _newExePath;

        [ObservableProperty]
        private string _selectedSchedule = ScheduleTypes.Always;

        [ObservableProperty]
        private string[] _scheduleOptions = ScheduleTypes.All;

        [ObservableProperty]
        private ObservableCollection<string> _runningProcesses = new();

        [ObservableProperty]
        private string? _selectedRunningProcess;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isSuccessMessage;

        [ObservableProperty]
        private bool _isBusy;

        public AppsViewModel(IpcClientService ipcClient)
        {
            _ipcClient = ipcClient;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _timer.Tick += (s, e) => RefreshUnlockCountdowns();
            _timer.Start();

            RefreshRunningProcesses();
        }

        public async Task LoadRulesAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var items = await db.AppRules.OrderByDescending(r => r.CreatedAt).ToListAsync();
                
                var ruleItems = new ObservableCollection<AppRuleItem>();
                foreach (var r in items)
                {
                    var item = new AppRuleItem(r);
                    if (_temporaryUnlocks.TryGetValue(r.ProcessName, out var expiry))
                    {
                        item.UpdateUnlockState(expiry);
                    }
                    ruleItems.Add(item);
                }
                Rules = ruleItems;

                // Load custom schedules dynamically into schedule selector
                var customSchedules = await db.CustomSchedules.Where(c => c.IsActive).Select(c => c.Name).ToListAsync();
                var allOptions = new List<string>(ScheduleTypes.All);
                foreach (var cs in customSchedules)
                {
                    if (!allOptions.Contains(cs, StringComparer.OrdinalIgnoreCase))
                    {
                        allOptions.Add(cs);
                    }
                }
                ScheduleOptions = allOptions.ToArray();
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load rules: {ex.Message}", false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void RefreshRunningProcesses()
        {
            try
            {
                var procs = Process.GetProcesses()
                    .Where(p => !string.IsNullOrWhiteSpace(p.MainWindowTitle) ||
                                (!ProcessSafetyGuard.IsProtected(p.ProcessName) &&
                                 !p.ProcessName.StartsWith("svchost", StringComparison.OrdinalIgnoreCase) &&
                                 !p.ProcessName.StartsWith("System", StringComparison.OrdinalIgnoreCase) &&
                                 !p.ProcessName.StartsWith("Idle", StringComparison.OrdinalIgnoreCase)))
                    .Select(p => p.ProcessName.ToLowerInvariant())
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                RunningProcesses = new ObservableCollection<string>(procs);
            }
            catch { }
        }

        [RelayCommand]
        private void SelectQuickApp(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName)) return;
            NewProcessName = processName.Trim().ToLowerInvariant();
            NewExePath = null;
        }

        [RelayCommand]
        private void BrowseExe()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Application Executable to Block",
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                NewExePath = dialog.FileName;
                NewProcessName = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
        }

        [RelayCommand]
        private async Task AddRuleAsync()
        {
            if (string.IsNullOrWhiteSpace(NewProcessName))
            {
                SetStatus("Please specify a process name or browse for an .exe file.", false);
                return;
            }

            var cleanName = Path.GetFileNameWithoutExtension(NewProcessName).Trim().ToLowerInvariant();

            if (ProcessSafetyGuard.IsProtected(cleanName))
            {
                SetStatus($"'{cleanName}' is a protected Windows system component and cannot be blocked.", false);
                return;
            }

            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var existing = await db.AppRules.FirstOrDefaultAsync(r => r.ProcessName.ToLower() == cleanName);

                if (existing != null)
                {
                    existing.IsActive = true;
                    existing.ScheduleType = SelectedSchedule;
                    existing.ExePath = NewExePath;
                }
                else
                {
                    db.AppRules.Add(new AppRule
                    {
                        ProcessName = cleanName,
                        ExePath = NewExePath,
                        IsActive = true,
                        ScheduleType = SelectedSchedule,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Rule Created",
                    Description = $"App Rule added: {cleanName} (Schedule: {SelectedSchedule})"
                });

                await db.SaveChangesAsync();

                // Send IPC message to Worker Service for immediate real-time enforcement
                var ipcResp = await _ipcClient.AddAppRuleAsync(cleanName, SelectedSchedule);

                SetStatus($"Rule for '{cleanName}' saved successfully! Service: {ipcResp.Message}", true);

                NewProcessName = string.Empty;
                NewExePath = null;
                await LoadRulesAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Error adding rule: {ex.Message}", false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RemoveRuleAsync(AppRuleItem? item)
        {
            if (item == null) return;

            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.AppRules.FindAsync(item.Id);
                if (entity != null)
                {
                    db.AppRules.Remove(entity);
                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Rule Deleted",
                        Description = $"App Rule deleted: {entity.ProcessName}"
                    });
                    await db.SaveChangesAsync();

                    // Send IPC notification
                    await _ipcClient.RemoveAppRuleAsync(item.Id);

                    SetStatus($"Rule for '{item.ProcessName}' removed.", true);
                    await LoadRulesAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error removing rule: {ex.Message}", false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task ToggleRuleAsync(AppRuleItem? item)
        {
            if (item == null) return;

            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.AppRules.FindAsync(item.Id);
                if (entity != null)
                {
                    entity.IsActive = !entity.IsActive;
                    await db.SaveChangesAsync();

                    await _ipcClient.ReloadRulesAsync();
                    SetStatus($"Rule for '{item.ProcessName}' is now {(entity.IsActive ? "Active" : "Disabled")}.", true);
                    await LoadRulesAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error toggling rule: {ex.Message}", false);
            }
        }

        [RelayCommand]
        private async Task TemporaryUnlockAsync(AppRuleItem? item)
        {
            if (item == null) return;
            await UnlockWithDurationAsync(item, 15);
        }

        [RelayCommand]
        private async Task Unlock30mAsync(AppRuleItem? item)
        {
            if (item == null) return;
            await UnlockWithDurationAsync(item, 30);
        }

        [RelayCommand]
        private async Task Unlock60mAsync(AppRuleItem? item)
        {
            if (item == null) return;
            await UnlockWithDurationAsync(item, 60);
        }

        private async Task UnlockWithDurationAsync(AppRuleItem? item, int minutes)
        {
            if (item == null) return;

            try
            {
                if (item.IsTemporarilyUnlocked)
                {
                    // Relock immediately
                    var resp = await _ipcClient.TemporaryUnlockAppAsync(item.ProcessName, 0);
                    _temporaryUnlocks.Remove(item.ProcessName);
                    item.UpdateUnlockState(null);
                    SetStatus($"'{item.ProcessName}' relocked immediately.", true);
                }
                else
                {
                    // Unlock for specified minutes
                    var resp = await _ipcClient.TemporaryUnlockAppAsync(item.ProcessName, minutes);
                    if (resp.Success)
                    {
                        var expiry = DateTime.UtcNow.AddMinutes(minutes);
                        _temporaryUnlocks[item.ProcessName] = expiry;
                        item.UpdateUnlockState(expiry);
                        SetStatus(resp.Message ?? $"Temporarily unlocked '{item.ProcessName}' for {minutes} minute(s).", true);
                    }
                    else
                    {
                        SetStatus(resp.Message ?? $"Failed to unlock '{item.ProcessName}'.", false);
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Unlock error: {ex.Message}", false);
            }
        }

        private void RefreshUnlockCountdowns()
        {
            foreach (var rule in Rules)
            {
                if (_temporaryUnlocks.TryGetValue(rule.ProcessName, out var expiry))
                {
                    rule.UpdateUnlockState(expiry);
                    if (DateTime.UtcNow >= expiry)
                    {
                        _temporaryUnlocks.Remove(rule.ProcessName);
                    }
                }
                else if (rule.IsTemporarilyUnlocked)
                {
                    rule.UpdateUnlockState(null);
                }
            }
        }

        [RelayCommand]
        private void DismissStatus()
        {
            _statusCts?.Cancel();
            StatusMessage = string.Empty;
        }

        private void SetStatus(string msg, bool success)
        {
            StatusMessage = msg;
            IsSuccessMessage = success;

            _statusCts?.Cancel();
            _statusCts = new CancellationTokenSource();
            var token = _statusCts.Token;

            // Auto-dismiss in-app notification after 30 seconds
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(30000, token);
                    if (!token.IsCancellationRequested)
                    {
                        StatusMessage = string.Empty;
                    }
                }
                catch { }
            });
        }
    }
}
