using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Models;
using AppBlocker.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.UI.ViewModels
{
    public partial class WebsitesViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;

        [ObservableProperty]
        private ObservableCollection<WebRule> _rules = new();

        [ObservableProperty]
        private string _newUrlOrDomain = string.Empty;

        [ObservableProperty]
        private string _newRedirectUrl = string.Empty;

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

        public WebsitesViewModel(IpcClientService ipcClient)
        {
            _ipcClient = ipcClient;
        }

        public async Task LoadRulesAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var items = await db.WebRules.OrderByDescending(r => r.CreatedAt).ToListAsync();
                Rules = new ObservableCollection<WebRule>(items);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load web rules: {ex.Message}", false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task AddRuleAsync()
        {
            if (string.IsNullOrWhiteSpace(NewUrlOrDomain))
            {
                SetStatus("Please enter a domain or URL (e.g. youtube.com or facebook.com).", false);
                return;
            }

            var cleanDomain = WebFilteringEngine.NormalizeDomain(NewUrlOrDomain);
            if (string.IsNullOrWhiteSpace(cleanDomain))
            {
                SetStatus("Invalid domain format.", false);
                return;
            }

            var redirect = string.IsNullOrWhiteSpace(NewRedirectUrl) ? null : NewRedirectUrl.Trim();
            if (!string.IsNullOrWhiteSpace(redirect))
            {
                if (!redirect.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !redirect.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    redirect = "https://" + redirect;
                }
            }

            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var existing = await db.WebRules.FirstOrDefaultAsync(r => r.UrlOrKeyword.ToLower() == cleanDomain);

                if (existing != null)
                {
                    existing.IsActive = true;
                    existing.ScheduleType = SelectedSchedule;
                    existing.RedirectUrl = redirect;
                }
                else
                {
                    db.WebRules.Add(new WebRule
                    {
                        UrlOrKeyword = cleanDomain,
                        RedirectUrl = redirect,
                        IsActive = true,
                        ScheduleType = SelectedSchedule,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Rule Created",
                    Description = $"Web Rule added: {cleanDomain} (Schedule: {SelectedSchedule}, Redirect: {redirect ?? "None"})"
                });

                await db.SaveChangesAsync();

                // Send IPC message to Worker Service to update proxy filtering engine immediately
                var ipcResp = await _ipcClient.AddWebRuleAsync(cleanDomain, SelectedSchedule, redirect);

                SetStatus(string.IsNullOrWhiteSpace(redirect)
                    ? $"Domain '{cleanDomain}' blocked! Service: {ipcResp.Message}"
                    : $"Domain '{cleanDomain}' redirected to '{redirect}'! Service: {ipcResp.Message}", true);

                NewUrlOrDomain = string.Empty;
                NewRedirectUrl = string.Empty;
                await LoadRulesAsync();
            }
            catch (Exception ex)
            {
                SetStatus($"Error adding domain: {ex.Message}", false);
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RemoveRuleAsync(WebRule? rule)
        {
            if (rule == null) return;

            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.WebRules.FindAsync(rule.Id);
                if (entity != null)
                {
                    db.WebRules.Remove(entity);
                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Rule Deleted",
                        Description = $"Web Rule deleted: {entity.UrlOrKeyword}"
                    });
                    await db.SaveChangesAsync();

                    // Send IPC update
                    await _ipcClient.RemoveWebRuleAsync(rule.Id);

                    SetStatus($"Domain '{rule.UrlOrKeyword}' unblocked.", true);
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
        private async Task ToggleRuleAsync(WebRule? rule)
        {
            if (rule == null) return;

            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.WebRules.FindAsync(rule.Id);
                if (entity != null)
                {
                    entity.IsActive = !entity.IsActive;
                    await db.SaveChangesAsync();

                    await _ipcClient.ReloadRulesAsync();
                    SetStatus($"Domain '{rule.UrlOrKeyword}' is now {(entity.IsActive ? "Active" : "Paused")}.", true);
                    await LoadRulesAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error toggling rule: {ex.Message}", false);
            }
        }

        private void SetStatus(string msg, bool success)
        {
            StatusMessage = msg;
            IsSuccessMessage = success;
        }
    }
}
