using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.IPC;
using AppBlocker.Core.Models;
using AppBlocker.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AppBlocker.Service
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly AppLockingEngine _appLockingEngine;
        private readonly WebFilteringEngine _webFilteringEngine;
        private readonly TaskManagerLockdownService _taskManagerLockdownService;
        private NamedPipeIpcServer? _ipcServer;
        private readonly DateTime _serviceStartTime = DateTime.UtcNow;
        private volatile bool _isBlockingPaused = false;
        private DateTime? _pauseUntil = null;
        private DateTime _lastWebScheduleCheck = DateTime.MinValue;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
            _appLockingEngine = new AppLockingEngine(msg => _logger.LogInformation("{Message}", msg));
            _webFilteringEngine = new WebFilteringEngine(null, msg => _logger.LogInformation("{Message}", msg));
            _taskManagerLockdownService = new TaskManagerLockdownService(TaskManagerLockdownService.DefaultPolicySubKey, msg => _logger.LogInformation("{Message}", msg));
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AppBlocker Background Service starting...");

            // Load initial app blocking rules
            await _appLockingEngine.LoadRulesAsync();

            // Apply initial web filtering rules to hosts file
            try
            {
                await _webFilteringEngine.ApplyRulesFromDatabaseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to apply initial web filtering rules.");
            }

            // Initialize IPC Server
            _ipcServer = new NamedPipeIpcServer(
                IpcConstants.PipeName,
                HandleIpcRequestAsync,
                msg => _logger.LogInformation("[IPC] {Message}", msg));

            _ipcServer.Start();
            _logger.LogInformation("Named Pipe IPC Server active on: {PipeName}", IpcConstants.PipeName);

            // Log service start to database
            try
            {
                using var db = AppBlockerDbContext.CreateContext();
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Service Started",
                    Description = "AppBlocker Background Worker Service started successfully."
                });
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write service start log to database.");
            }

            await base.StartAsync(cancellationToken);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AppBlocker Service Worker loop is running with 1.5s process polling.");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Check if timed pause expired
                if (_isBlockingPaused && _pauseUntil.HasValue && DateTime.UtcNow >= _pauseUntil.Value)
                {
                    _isBlockingPaused = false;
                    _pauseUntil = null;
                    _appLockingEngine.IsPaused = false;
                    _webFilteringEngine.IsPaused = false;
                    _logger.LogInformation("Blocking pause expired. Resumed enforcement.");

                    await _webFilteringEngine.ApplyRulesFromDatabaseAsync();

                    try
                    {
                        await using var db = AppBlockerDbContext.CreateContext();
                        db.Logs.Add(new SystemLog
                        {
                            Timestamp = DateTime.UtcNow,
                            EventType = "Blocking Resumed",
                            Description = "Timed pause period ended. Blocking resumed."
                        });
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    catch { }
                }

                _appLockingEngine.IsPaused = _isBlockingPaused;
                _webFilteringEngine.IsPaused = _isBlockingPaused;

                // Milestone 2 Enforcement: Process polling every 1.5 seconds
                if (!_isBlockingPaused)
                {
                    try
                    {
                        await _appLockingEngine.RunScanCycleAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error during process scan cycle.");
                    }

                    // Milestone 3: Periodic check for scheduled web rules (every 30 seconds)
                    if (DateTime.UtcNow - _lastWebScheduleCheck >= TimeSpan.FromSeconds(30))
                    {
                        _lastWebScheduleCheck = DateTime.UtcNow;
                        try
                        {
                            await _webFilteringEngine.ApplyRulesFromDatabaseAsync();
                        }
                        catch { }
                    }
                }

                await Task.Delay(1500, stoppingToken);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("AppBlocker Service stopping...");

            if (_ipcServer != null)
            {
                await _ipcServer.StopAsync();
            }

            // Restore hosts file cleanly when service stops
            try
            {
                await _webFilteringEngine.ClearManagedEntriesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear web filter entries during shutdown.");
            }

            // Restore Task Manager policy on service shutdown
            try
            {
                _taskManagerLockdownService.RestoreDefaults();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore Task Manager policy during shutdown.");
            }

            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Service Stopped",
                    Description = "AppBlocker Background Worker Service stopped cleanly."
                });
                await db.SaveChangesAsync(cancellationToken);
            }
            catch { }

            await base.StopAsync(cancellationToken);
        }

        private async Task<IpcResponse> HandleIpcRequestAsync(IpcRequest request)
        {
            _logger.LogInformation("Received IPC command: {Command}", request.Command);

            switch (request.Command)
            {
                case IpcConstants.Commands.Ping:
                    return IpcResponse.Ok(request.Id, "Pong", new { Timestamp = DateTime.UtcNow });

                case IpcConstants.Commands.GetStatus:
                {
                    int appRulesCount = 0;
                    int webRulesCount = 0;
                    try
                    {
                        await using var db = AppBlockerDbContext.CreateContext();
                        appRulesCount = await db.AppRules.CountAsync(r => r.IsActive);
                        webRulesCount = await db.WebRules.CountAsync(r => r.IsActive);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error querying rules count for status.");
                    }

                    var status = new ServiceStatusDto
                    {
                        IsRunning = true,
                        IsBlockingPaused = _isBlockingPaused,
                        ActiveAppRuleCount = appRulesCount,
                        ActiveWebRuleCount = webRulesCount,
                        ServiceStartTime = _serviceStartTime,
                        ProcessName = "AppBlocker.Service"
                    };
                    return IpcResponse.Ok(request.Id, "Service status retrieved", status);
                }

                case IpcConstants.Commands.ReloadRules:
                {
                    await _appLockingEngine.LoadRulesAsync();
                    await _webFilteringEngine.ApplyRulesFromDatabaseAsync();

                    await using var db = AppBlockerDbContext.CreateContext();
                    var activeApps = await db.AppRules.Where(r => r.IsActive).ToListAsync();
                    var activeWebs = await db.WebRules.Where(r => r.IsActive).ToListAsync();

                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Config Reload",
                        Description = $"Rules reloaded via IPC. Active Apps: {activeApps.Count}, Active Web: {activeWebs.Count}."
                    });
                    await db.SaveChangesAsync();

                    return IpcResponse.Ok(request.Id, "Rules successfully reloaded into service cache.", new
                    {
                        ActiveAppRules = activeApps.Count,
                        ActiveWebRules = activeWebs.Count
                    });
                }

                case IpcConstants.Commands.PauseBlocking:
                {
                    var payload = request.GetPayload<PauseBlockingDto>();
                    var duration = payload?.DurationMinutes ?? 0;

                    _isBlockingPaused = true;
                    _appLockingEngine.IsPaused = true;
                    _webFilteringEngine.IsPaused = true;

                    // Cleanly remove blocked domains while paused
                    await _webFilteringEngine.ClearManagedEntriesAsync();

                    if (duration > 0)
                    {
                        _pauseUntil = DateTime.UtcNow.AddMinutes(duration);
                    }
                    else
                    {
                        _pauseUntil = null;
                    }

                    await using var db = AppBlockerDbContext.CreateContext();
                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Blocking Paused",
                        Description = duration > 0 ? $"Blocking paused for {duration} minute(s)." : "Blocking paused indefinitely."
                    });
                    await db.SaveChangesAsync();

                    return IpcResponse.Ok(request.Id, $"Blocking paused. Duration: {(duration > 0 ? $"{duration}m" : "Indefinite")}");
                }

                case IpcConstants.Commands.ResumeBlocking:
                {
                    _isBlockingPaused = false;
                    _pauseUntil = null;
                    _appLockingEngine.IsPaused = false;
                    _webFilteringEngine.IsPaused = false;

                    // Re-apply web filtering rules immediately
                    await _webFilteringEngine.ApplyRulesFromDatabaseAsync();

                    await using var db = AppBlockerDbContext.CreateContext();
                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Blocking Resumed",
                        Description = "Blocking resumed via UI control."
                    });
                    await db.SaveChangesAsync();

                    return IpcResponse.Ok(request.Id, "Blocking resumed successfully.");
                }

                case IpcConstants.Commands.FlushDns:
                {
                    var flushed = WebFilteringEngine.FlushDnsCache();
                    return IpcResponse.Ok(request.Id, flushed ? "DNS cache successfully flushed." : "DNS cache flush completed with fallback.");
                }

                case IpcConstants.Commands.TemporaryUnlockApp:
                {
                    var payload = request.GetPayload<TemporaryUnlockDto>();
                    if (payload == null || string.IsNullOrWhiteSpace(payload.ProcessName))
                    {
                        return IpcResponse.Error(request.Id, "Invalid process name for temporary unlock.");
                    }

                    var minutes = payload.DurationMinutes > 0 ? payload.DurationMinutes : 15;
                    _appLockingEngine.TemporaryUnlockApp(payload.ProcessName, TimeSpan.FromMinutes(minutes));

                    await using var db = AppBlockerDbContext.CreateContext();
                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Temporary App Unlock",
                        Description = $"Process '{payload.ProcessName}' temporarily unlocked for {minutes} minute(s)."
                    });
                    await db.SaveChangesAsync();

                    return IpcResponse.Ok(request.Id, $"Process '{payload.ProcessName}' unlocked for {minutes} minutes.");
                }

                case IpcConstants.Commands.AddAppRule:
                {
                    var ruleDto = request.GetPayload<RuleUpdateDto>();
                    if (ruleDto == null || string.IsNullOrWhiteSpace(ruleDto.Target))
                    {
                        return IpcResponse.Error(request.Id, "Invalid App Rule payload.");
                    }

                    var processClean = Path.GetFileNameWithoutExtension(ruleDto.Target).Trim().ToLowerInvariant();

                    if (ProcessSafetyGuard.IsProtected(processClean))
                    {
                        return IpcResponse.Error(request.Id, $"Process '{processClean}' is a protected Windows system component and cannot be blocked.");
                    }

                    await using var db = AppBlockerDbContext.CreateContext();
                    var existing = await db.AppRules.FirstOrDefaultAsync(r => r.ProcessName.ToLower() == processClean);
                    if (existing != null)
                    {
                        existing.IsActive = true;
                        existing.ScheduleType = ruleDto.ScheduleType ?? ScheduleTypes.Always;
                    }
                    else
                    {
                        db.AppRules.Add(new AppRule
                        {
                            ProcessName = processClean,
                            ExePath = ruleDto.Target,
                            IsActive = true,
                            ScheduleType = ruleDto.ScheduleType ?? ScheduleTypes.Always,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Rule Created",
                        Description = $"App Rule added: {processClean} (Schedule: {ruleDto.ScheduleType ?? ScheduleTypes.Always})"
                    });
                    await db.SaveChangesAsync();

                    // Hot-reload rules cache in AppLockingEngine
                    await _appLockingEngine.LoadRulesAsync();

                    return IpcResponse.Ok(request.Id, $"App rule '{processClean}' saved and active.");
                }

                case IpcConstants.Commands.RemoveAppRule:
                {
                    var ruleDto = request.GetPayload<RuleUpdateDto>();
                    if (ruleDto == null)
                    {
                        return IpcResponse.Error(request.Id, "Invalid App Rule payload.");
                    }

                    await using var db = AppBlockerDbContext.CreateContext();
                    var rule = await db.AppRules.FindAsync(ruleDto.RuleId);
                    if (rule != null)
                    {
                        db.AppRules.Remove(rule);
                        db.Logs.Add(new SystemLog
                        {
                            Timestamp = DateTime.UtcNow,
                            EventType = "Rule Deleted",
                            Description = $"App Rule deleted: {rule.ProcessName} (ID: {rule.Id})"
                        });
                        await db.SaveChangesAsync();

                        // Hot-reload rules cache in AppLockingEngine
                        await _appLockingEngine.LoadRulesAsync();

                        return IpcResponse.Ok(request.Id, $"App rule '{rule.ProcessName}' removed.");
                    }
                    return IpcResponse.Error(request.Id, $"App rule ID {ruleDto.RuleId} not found.");
                }

                case IpcConstants.Commands.AddWebRule:
                {
                    var ruleDto = request.GetPayload<RuleUpdateDto>();
                    if (ruleDto == null || string.IsNullOrWhiteSpace(ruleDto.Target))
                    {
                        return IpcResponse.Error(request.Id, "Invalid Web Rule payload.");
                    }

                    var domainClean = WebFilteringEngine.NormalizeDomain(ruleDto.Target);
                    if (string.IsNullOrWhiteSpace(domainClean))
                    {
                        return IpcResponse.Error(request.Id, "Invalid domain name.");
                    }

                    await using var db = AppBlockerDbContext.CreateContext();
                    var existing = await db.WebRules.FirstOrDefaultAsync(r => r.UrlOrKeyword.ToLower() == domainClean);
                    if (existing != null)
                    {
                        existing.IsActive = true;
                        existing.ScheduleType = ruleDto.ScheduleType ?? ScheduleTypes.Always;
                    }
                    else
                    {
                        db.WebRules.Add(new WebRule
                        {
                            UrlOrKeyword = domainClean,
                            IsActive = true,
                            ScheduleType = ruleDto.ScheduleType ?? ScheduleTypes.Always,
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    db.Logs.Add(new SystemLog
                    {
                        Timestamp = DateTime.UtcNow,
                        EventType = "Rule Created",
                        Description = $"Web Rule added: {domainClean} (Schedule: {ruleDto.ScheduleType ?? ScheduleTypes.Always})"
                    });
                    await db.SaveChangesAsync();

                    // Apply updated rules to hosts file immediately
                    await _webFilteringEngine.ApplyRulesFromDatabaseAsync();

                    return IpcResponse.Ok(request.Id, $"Web rule '{domainClean}' saved and active.");
                }

                case IpcConstants.Commands.RemoveWebRule:
                {
                    var ruleDto = request.GetPayload<RuleUpdateDto>();
                    if (ruleDto == null)
                    {
                        return IpcResponse.Error(request.Id, "Invalid Web Rule payload.");
                    }

                    await using var db = AppBlockerDbContext.CreateContext();
                    var rule = await db.WebRules.FindAsync(ruleDto.RuleId);
                    if (rule != null)
                    {
                        db.WebRules.Remove(rule);
                        db.Logs.Add(new SystemLog
                        {
                            Timestamp = DateTime.UtcNow,
                            EventType = "Rule Deleted",
                            Description = $"Web Rule deleted: {rule.UrlOrKeyword} (ID: {rule.Id})"
                        });
                        await db.SaveChangesAsync();

                        // Apply updated rules to hosts file immediately
                        await _webFilteringEngine.ApplyRulesFromDatabaseAsync();

                        return IpcResponse.Ok(request.Id, $"Web rule '{rule.UrlOrKeyword}' removed.");
                    }
                    return IpcResponse.Error(request.Id, $"Web rule ID {ruleDto.RuleId} not found.");
                }

                case IpcConstants.Commands.SetTaskManagerState:
                {
                    var dto = request.GetPayload<TaskManagerStateDto>();
                    var disable = dto?.IsDisabled ?? false;
                    var success = _taskManagerLockdownService.SetTaskMgrDisabled(disable);

                    try
                    {
                        await using var db = AppBlockerDbContext.CreateContext();
                        db.Logs.Add(new SystemLog
                        {
                            Timestamp = DateTime.UtcNow,
                            EventType = disable ? "TaskMgr Locked" : "TaskMgr Restored",
                            Description = disable ? "Task Manager has been disabled via policy." : "Task Manager has been restored to default."
                        });
                        await db.SaveChangesAsync();
                    }
                    catch { }

                    return IpcResponse.Ok(request.Id, $"Task Manager policy updated: Disable={disable}", new { IsDisabled = disable, Success = success });
                }

                case IpcConstants.Commands.GetTaskManagerState:
                {
                    var isBlocked = _taskManagerLockdownService.IsTaskMgrDisabled();
                    return IpcResponse.Ok(request.Id, "Task Manager state retrieved", new TaskManagerStateDto { IsDisabled = isBlocked });
                }

                default:
                    return IpcResponse.Error(request.Id, $"Unknown command: '{request.Command}'");
            }
        }
    }
}
