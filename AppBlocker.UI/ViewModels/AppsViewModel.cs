using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public partial class AppsViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;

        [ObservableProperty]
        private ObservableCollection<AppRule> _rules = new();

        [ObservableProperty]
        private string _newProcessName = string.Empty;

        [ObservableProperty]
        private string? _newExePath;

        [ObservableProperty]
        private string _selectedSchedule = ScheduleTypes.Always;

        [ObservableProperty]
        private string[] _scheduleOptions = ScheduleTypes.All;

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isSuccessMessage;

        [ObservableProperty]
        private bool _isBusy;

        public AppsViewModel(IpcClientService ipcClient)
        {
            _ipcClient = ipcClient;
        }

        public async Task LoadRulesAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var items = await db.AppRules.OrderByDescending(r => r.CreatedAt).ToListAsync();
                Rules = new ObservableCollection<AppRule>(items);
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
        private async Task RemoveRuleAsync(AppRule? rule)
        {
            if (rule == null) return;

            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.AppRules.FindAsync(rule.Id);
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
                    await _ipcClient.RemoveAppRuleAsync(rule.Id);

                    SetStatus($"Rule for '{rule.ProcessName}' removed.", true);
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
        private async Task ToggleRuleAsync(AppRule? rule)
        {
            if (rule == null) return;

            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.AppRules.FindAsync(rule.Id);
                if (entity != null)
                {
                    entity.IsActive = !entity.IsActive;
                    await db.SaveChangesAsync();

                    await _ipcClient.ReloadRulesAsync();
                    SetStatus($"Rule for '{rule.ProcessName}' is now {(entity.IsActive ? "Active" : "Disabled")}.", true);
                    await LoadRulesAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error toggling rule: {ex.Message}", false);
            }
        }

        [RelayCommand]
        private async Task TemporaryUnlockAsync(AppRule? rule)
        {
            if (rule == null) return;

            try
            {
                var resp = await _ipcClient.TemporaryUnlockAppAsync(rule.ProcessName, 15);
                SetStatus(resp.Message ?? $"Temporarily unlocked '{rule.ProcessName}' for 15 minutes.", resp.Success);
            }
            catch (Exception ex)
            {
                SetStatus($"Unlock error: {ex.Message}", false);
            }
        }

        private void SetStatus(string msg, bool success)
        {
            StatusMessage = msg;
            IsSuccessMessage = success;
        }
    }
}
