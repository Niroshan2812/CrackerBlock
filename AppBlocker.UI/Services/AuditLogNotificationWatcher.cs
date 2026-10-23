using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.UI.Services
{
    public class AuditLogNotificationWatcher : IAsyncDisposable
    {
        private readonly NotificationService _notificationService;
        private readonly Func<AppBlockerDbContext> _dbFactory;
        private readonly int _pollingIntervalMs;

        private CancellationTokenSource? _cts;
        private Task? _watcherTask;
        private int _lastSeenLogId = 0;
        private bool _isInitialized = false;

        public int LastSeenLogId => _lastSeenLogId;
        public bool IsRunning => _watcherTask != null && !_watcherTask.IsCompleted;

        public AuditLogNotificationWatcher(
            NotificationService? notificationService = null,
            Func<AppBlockerDbContext>? dbFactory = null,
            int pollingIntervalMs = 1000)
        {
            _notificationService = notificationService ?? NotificationService.Instance;
            _dbFactory = dbFactory ?? (() => AppBlockerDbContext.CreateContext());
            _pollingIntervalMs = pollingIntervalMs;
        }

        public async Task StartAsync()
        {
            if (IsRunning)
            {
                return;
            }

            // 1. Establish current baseline so historical logs aren't re-alerted
            if (!_isInitialized)
            {
                try
                {
                    await using var db = _dbFactory();
                    _lastSeenLogId = await db.Logs.OrderByDescending(l => l.Id).Select(l => l.Id).FirstOrDefaultAsync();
                    _isInitialized = true;
                }
                catch
                {
                    _lastSeenLogId = 0;
                }
            }

            _cts = new CancellationTokenSource();
            _watcherTask = Task.Run(() => WatchLoopAsync(_cts.Token));
        }

        public async Task<int> ProcessNewLogsOnceAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await using var db = _dbFactory();
                var newLogs = await db.Logs
                    .Where(l => l.Id > _lastSeenLogId)
                    .OrderBy(l => l.Id)
                    .Take(25)
                    .ToListAsync(cancellationToken);

                int processedCount = 0;
                foreach (var log in newLogs)
                {
                    if (log.Id > _lastSeenLogId)
                    {
                        _lastSeenLogId = log.Id;
                    }

                    var toast = NotificationService.ParseLogToNotification(log);
                    if (toast != null)
                    {
                        if (_notificationService.ShowNotification(toast))
                        {
                            processedCount++;
                        }
                    }
                }

                return processedCount;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning audit logs: {ex.Message}");
                return 0;
            }
        }

        private async Task WatchLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_pollingIntervalMs, token);
                    await ProcessNewLogsOnceAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"AuditLogNotificationWatcher exception: {ex.Message}");
                }
            }
        }

        public async Task StopAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }

            if (_watcherTask != null)
            {
                try
                {
                    await _watcherTask;
                }
                catch { }
                _watcherTask = null;
            }

            _cts?.Dispose();
            _cts = null;
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }
}
