using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
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
    public partial class WebRuleItem : ObservableObject
    {
        public WebRule Rule { get; }

        public int Id => Rule.Id;
        public string UrlOrKeyword => Rule.UrlOrKeyword;
        public string? RedirectUrl => Rule.RedirectUrl;
        public bool IsActive => Rule.IsActive;
        public string ScheduleType => Rule.ScheduleType;
        public DateTime CreatedAt => Rule.CreatedAt;

        [ObservableProperty]
        private bool _isChainedRedirect;

        [ObservableProperty]
        private string _chainVisualText = string.Empty;

        public WebRuleItem(WebRule rule)
        {
            Rule = rule;
        }

        public void CalculateChain(IEnumerable<WebRule> allRules)
        {
            if (string.IsNullOrWhiteSpace(Rule.RedirectUrl))
            {
                IsChainedRedirect = false;
                ChainVisualText = string.Empty;
                return;
            }

            var nextDomain = WebFilteringEngine.NormalizeDomain(Rule.RedirectUrl);
            var nextRule = allRules.FirstOrDefault(r => 
                r.IsActive && 
                WebFilteringEngine.NormalizeDomain(r.UrlOrKeyword).Equals(nextDomain, StringComparison.OrdinalIgnoreCase));

            if (nextRule != null && !nextRule.UrlOrKeyword.Equals(Rule.UrlOrKeyword, StringComparison.OrdinalIgnoreCase))
            {
                IsChainedRedirect = true;
                var chain = new List<string> { Rule.UrlOrKeyword, nextDomain };
                if (!string.IsNullOrWhiteSpace(nextRule.RedirectUrl))
                {
                    var finalHop = WebFilteringEngine.NormalizeDomain(nextRule.RedirectUrl);
                    if (!chain.Contains(finalHop, StringComparer.OrdinalIgnoreCase))
                    {
                        chain.Add(finalHop);
                    }
                }
                ChainVisualText = string.Join(" ➜ ", chain);
            }
            else
            {
                IsChainedRedirect = false;
                ChainVisualText = string.Empty;
            }
        }
    }

    public partial class WebsitesViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;
        private CancellationTokenSource? _statusCts;

        [ObservableProperty]
        private ObservableCollection<WebRuleItem> _rules = new();

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
                var rawItems = await db.WebRules.OrderByDescending(r => r.CreatedAt).ToListAsync();

                // Deduplicate any historical identical entries in database
                var uniqueItems = rawItems
                    .GroupBy(r => WebFilteringEngine.NormalizeDomain(r.UrlOrKeyword))
                    .Select(g => g.First())
                    .ToList();

                var items = new ObservableCollection<WebRuleItem>();
                foreach (var r in uniqueItems)
                {
                    var item = new WebRuleItem(r);
                    item.CalculateChain(uniqueItems);
                    items.Add(item);
                }
                Rules = items;

                // Load custom schedules dynamically
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
                // Strict duplicate check against normalized domain
                var allWebRules = await db.WebRules.ToListAsync();
                var existing = allWebRules.FirstOrDefault(r => 
                    WebFilteringEngine.NormalizeDomain(r.UrlOrKeyword).Equals(cleanDomain, StringComparison.OrdinalIgnoreCase));

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
                    Description = $"Web Rule saved: {cleanDomain} (Schedule: {SelectedSchedule}, Redirect: {redirect ?? "None"})"
                });

                await db.SaveChangesAsync();

                // Send IPC message to Worker Service to update proxy filtering engine immediately
                var ipcResp = await _ipcClient.AddWebRuleAsync(cleanDomain, SelectedSchedule, redirect);

                // Ensure local user WinINet proxy settings are active and refreshed
                HttpsProxyFilteringEngine.ConfigureWindowsProxy(enable: true);

                SetStatus(string.IsNullOrWhiteSpace(redirect)
                    ? $"Domain '{cleanDomain}' blocked successfully! Service: {ipcResp.Message}"
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
        private async Task RemoveRuleAsync(WebRuleItem? item)
        {
            if (item == null) return;

            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.WebRules.FindAsync(item.Id);
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
                    await _ipcClient.RemoveWebRuleAsync(item.Id);
                    await _ipcClient.ReloadRulesAsync();

                    SetStatus($"Domain '{item.UrlOrKeyword}' unblocked.", true);
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
        private async Task ToggleRuleAsync(WebRuleItem? item)
        {
            if (item == null) return;

            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var entity = await db.WebRules.FindAsync(item.Id);
                if (entity != null)
                {
                    entity.IsActive = !entity.IsActive;
                    await db.SaveChangesAsync();

                    await _ipcClient.ReloadRulesAsync();
                    SetStatus($"Domain '{item.UrlOrKeyword}' is now {(entity.IsActive ? "Active" : "Paused")}.", true);
                    await LoadRulesAsync();
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Error toggling rule: {ex.Message}", false);
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

            // 30-sec Auto-Dismiss timer
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
