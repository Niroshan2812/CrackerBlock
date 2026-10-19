using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.UI.ViewModels
{
    public partial class LogsViewModel : ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<SystemLog> _logs = new();

        [ObservableProperty]
        private string _searchFilter = string.Empty;

        [ObservableProperty]
        private bool _isBusy;

        [ObservableProperty]
        private string _statusText = string.Empty;

        public async Task LoadLogsAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                IQueryable<SystemLog> query = db.Logs.OrderByDescending(l => l.Timestamp);

                if (!string.IsNullOrWhiteSpace(SearchFilter))
                {
                    var filter = SearchFilter.Trim().ToLower();
                    query = query.Where(l => l.EventType.ToLower().Contains(filter) || l.Description.ToLower().Contains(filter));
                }

                var items = await query.Take(200).ToListAsync();
                Logs = new ObservableCollection<SystemLog>(items);
                StatusText = $"Loaded {items.Count} log entries.";
            }
            catch (Exception ex)
            {
                StatusText = $"Error loading logs: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private async Task RefreshLogsAsync()
        {
            await LoadLogsAsync();
        }

        [RelayCommand]
        private async Task ClearLogsAsync()
        {
            IsBusy = true;
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                db.Logs.RemoveRange(db.Logs);
                await db.SaveChangesAsync();

                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Logs Cleared",
                    Description = "Audit logs were manually purged by administrator."
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
