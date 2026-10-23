using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.UI.ViewModels
{
    public class DistractionStatItem
    {
        public string Name { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string Subtitle { get; set; } = string.Empty;
        public string DisplayCount => $"{Count} blocked";
    }

    public partial class LogsViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SystemLog> _logs = new();

        [ObservableProperty]
        private string _searchFilter = string.Empty;

        [ObservableProperty]
        private string _selectedCategory = "All";

        [ObservableProperty]
        private string _timeRange = "AllTime"; // "Today", "Week", "AllTime"

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = string.Empty;

        // Executive KPI Metrics
        [ObservableProperty]
        private int _totalInterceptions;

        [ObservableProperty]
        private int _totalAppsBlocked;

        [ObservableProperty]
        private int _totalWebsBlocked;

        [ObservableProperty]
        private string _timeSavedDisplay = "0m";

        [ObservableProperty]
        private string _focusScoreDisplay = "100%";

        [ObservableProperty]
        private int _tamperAttemptsCount;

        [ObservableProperty]
        private int _focusSessionsCompleted;

        // Top Distractor Breakdowns
        [ObservableProperty]
        private ObservableCollection<DistractionStatItem> _topBlockedApps = new();

        [ObservableProperty]
        private ObservableCollection<DistractionStatItem> _topBlockedWebsites = new();

        private readonly Func<AppBlockerDbContext> _dbFactory;

        public LogsViewModel(Func<AppBlockerDbContext>? dbFactory = null)
        {
            _dbFactory = dbFactory ?? (() => AppBlockerDbContext.CreateContext());
        }

        public async Task LoadLogsAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = _dbFactory();
                var allLogs = await db.Logs.OrderByDescending(l => l.Timestamp).ToListAsync();

                // 1. Time range filter for analytics calculation
                var now = DateTime.UtcNow;
                var filteredForStats = TimeRange switch
                {
                    "Today" => allLogs.Where(l => l.Timestamp >= now.Date).ToList(),
                    "Week" => allLogs.Where(l => l.Timestamp >= now.AddDays(-7)).ToList(),
                    _ => allLogs
                };

                // 2. Calculate KPI Metrics
                int appBlocks = filteredForStats.Count(l => l.EventType == "Blocked App");
                int webBlocks = filteredForStats.Count(l => l.EventType == "Blocked Web");
                int interceptions = appBlocks + webBlocks;
                int tampers = filteredForStats.Count(l => 
                    l.EventType.Contains("Tamper", StringComparison.OrdinalIgnoreCase) || 
                    l.EventType.Contains("Failed PIN", StringComparison.OrdinalIgnoreCase));
                int focusCompleted = filteredForStats.Count(l => 
                    l.EventType.Contains("Focus", StringComparison.OrdinalIgnoreCase));

                TotalInterceptions = interceptions;
                TotalAppsBlocked = appBlocks;
                TotalWebsBlocked = webBlocks;
                TamperAttemptsCount = tampers;
                FocusSessionsCompleted = focusCompleted;

                // Time Saved (5 minutes per distraction interception)
                int minsSaved = interceptions * 5;
                TimeSavedDisplay = minsSaved >= 60 ? $"{minsSaved / 60}h {minsSaved % 60}m" : $"{minsSaved}m";

                // Focus Score calculation
                int score = Math.Max(60, Math.Min(100, 100 - (tampers * 5) + (focusCompleted * 2)));
                FocusScoreDisplay = $"{score}%";

                // 3. Calculate Top Blocked Applications
                var appRegex = new Regex(@"'([^']+)'", RegexOptions.Compiled);
                var appCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (var log in filteredForStats.Where(l => l.EventType == "Blocked App"))
                {
                    var match = appRegex.Match(log.Description);
                    var appName = match.Success ? match.Groups[1].Value : "Unknown App";
                    appCounts[appName] = appCounts.GetValueOrDefault(appName, 0) + 1;
                }

                var topApps = appCounts.OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => new DistractionStatItem
                    {
                        Name = kvp.Key,
                        Count = kvp.Value,
                        Percentage = appBlocks > 0 ? (double)kvp.Value / appBlocks * 100.0 : 0,
                        Subtitle = $"{Math.Round(appBlocks > 0 ? (double)kvp.Value / appBlocks * 100.0 : 0, 1)}% of app blocks"
                    }).ToList();
                TopBlockedApps = new ObservableCollection<DistractionStatItem>(topApps);

                // 4. Calculate Top Blocked Websites
                var webCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var log in filteredForStats.Where(l => l.EventType == "Blocked Web"))
                {
                    var match = appRegex.Match(log.Description);
                    var domain = match.Success ? match.Groups[1].Value : "Unknown Domain";
                    webCounts[domain] = webCounts.GetValueOrDefault(domain, 0) + 1;
                }

                var topWebs = webCounts.OrderByDescending(kvp => kvp.Value)
                    .Take(5)
                    .Select(kvp => new DistractionStatItem
                    {
                        Name = kvp.Key,
                        Count = kvp.Value,
                        Percentage = webBlocks > 0 ? (double)kvp.Value / webBlocks * 100.0 : 0,
                        Subtitle = $"{Math.Round(webBlocks > 0 ? (double)kvp.Value / webBlocks * 100.0 : 0, 1)}% of web blocks"
                    }).ToList();
                TopBlockedWebsites = new ObservableCollection<DistractionStatItem>(topWebs);

                // 5. Filter audit logs list for table view
                var tableQuery = allLogs.AsEnumerable();

                // Category filter
                if (SelectedCategory != "All")
                {
                    tableQuery = SelectedCategory switch
                    {
                        "Blocked Apps" => tableQuery.Where(l => l.EventType == "Blocked App"),
                        "Blocked Web" => tableQuery.Where(l => l.EventType == "Blocked Web"),
                        "Anti-Tamper" => tableQuery.Where(l => l.EventType.Contains("Tamper") || l.EventType.Contains("Watchdog") || l.EventType.Contains("TaskMgr")),
                        "Focus Sprints" => tableQuery.Where(l => l.EventType.Contains("Focus")),
                        "PIN Security" => tableQuery.Where(l => l.EventType.Contains("PIN")),
                        _ => tableQuery
                    };
                }

                // Search text filter
                if (!string.IsNullOrWhiteSpace(SearchFilter))
                {
                    var filter = SearchFilter.Trim().ToLower();
                    tableQuery = tableQuery.Where(l => l.EventType.ToLower().Contains(filter) || l.Description.ToLower().Contains(filter));
                }

                var tableItems = tableQuery.Take(250).ToList();
                Logs = new ObservableCollection<SystemLog>(tableItems);
                StatusText = $"Analytics updated. Displaying {tableItems.Count} audit records.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error computing analytics: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task SelectCategoryAsync(string category)
        {
            SelectedCategory = category;
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task SelectTimeRangeAsync(string range)
        {
            TimeRange = range;
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task RefreshLogsAsync()
        {
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task ExportToCsvAsync()
        {
            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"AppBlocker_AuditReport_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = ".csv",
                    Filter = "CSV Spreadsheet (*.csv)|*.csv|All Files (*.*)|*.*"
                };

                if (sfd.ShowDialog() == true)
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Id,TimestampUtc,EventType,Description");

                    foreach (var log in Logs)
                    {
                        var cleanDesc = log.Description.Replace("\"", "\"\"");
                        sb.AppendLine($"{log.Id},\"{log.Timestamp:yyyy-MM-dd HH:mm:ss}\",\"{log.EventType}\",\"{cleanDesc}\"");
                    }

                    await File.WriteAllTextAsync(sfd.FileName, sb.ToString(), Encoding.UTF8);
                    StatusText = $"Audit report successfully exported to: {Path.GetFileName(sfd.FileName)}";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"Export failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ClearLogsAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = _dbFactory();
                db.Logs.RemoveRange(db.Logs);
                await db.SaveChangesAsync();

                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Logs Cleared",
                    Description = "Audit logs purged by administrator."
                });
                await db.SaveChangesAsync();

                await LoadLogsAsync();
            }
            catch (Exception ex)
            {
                StatusText = $"Failed to clear logs: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }
}
