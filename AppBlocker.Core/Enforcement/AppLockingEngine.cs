using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.Core.Enforcement
{
    public class AppLockingEngine
    {
        private readonly List<AppRule> _activeRules = new();
        private readonly List<CustomSchedule> _customSchedules = new();
        private readonly object _rulesLock = new();
        private readonly ConcurrentDictionary<string, DateTime> _temporarilyUnlockedApps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Action<string>? _logger;

        public bool IsPaused { get; set; } = false;
        public bool IsFocusModeActive { get; set; } = false;
        public SafeChildModeService? SafeChildMode { get; set; }
        public event Action<string, int, string>? ProcessTerminated;

        public AppLockingEngine(Action<string>? logger = null)
        {
            _logger = logger;
        }

        public async Task LoadRulesAsync()
        {
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var rules = await db.AppRules.Where(r => r.IsActive).ToListAsync();
                var schedules = await db.CustomSchedules.Where(s => s.IsActive).ToListAsync();

                lock (_rulesLock)
                {
                    _activeRules.Clear();
                    _activeRules.AddRange(rules);
                    _customSchedules.Clear();
                    _customSchedules.AddRange(schedules);
                }

                _logger?.Invoke($"AppLockingEngine loaded {rules.Count} active rule(s) and {schedules.Count} custom schedule(s).");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error loading app rules: {ex.Message}");
            }
        }

        public void SetRules(IEnumerable<AppRule> rules)
        {
            lock (_rulesLock)
            {
                _activeRules.Clear();
                _activeRules.AddRange(rules.Where(r => r.IsActive));
            }
        }

        public void UpdateCustomSchedules(IEnumerable<CustomSchedule> schedules)
        {
            lock (_rulesLock)
            {
                _customSchedules.Clear();
                _customSchedules.AddRange(schedules);
            }
        }

        public IReadOnlyList<AppRule> GetActiveRules()
        {
            lock (_rulesLock)
            {
                return _activeRules.ToList();
            }
        }

        public void TemporaryUnlockApp(string processName, TimeSpan duration)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return;
            }

            var cleanName = Path.GetFileNameWithoutExtension(processName).Trim().ToLowerInvariant();
            var expiry = DateTime.UtcNow.Add(duration);
            _temporarilyUnlockedApps[cleanName] = expiry;
            _logger?.Invoke($"Process '{cleanName}' temporarily unlocked until {expiry:yyyy-MM-dd HH:mm:ss} UTC.");
        }

        public bool IsTemporarilyUnlocked(string processName)
        {
            var cleanName = Path.GetFileNameWithoutExtension(processName).Trim().ToLowerInvariant();
            if (_temporarilyUnlockedApps.TryGetValue(cleanName, out var expiry))
            {
                if (DateTime.UtcNow < expiry)
                {
                    return true;
                }
                // Expired
                _temporarilyUnlockedApps.TryRemove(cleanName, out _);
            }
            return false;
        }

        public async Task<int> RunScanCycleAsync(CancellationToken cancellationToken = default)
        {
            if (IsPaused)
            {
                return 0;
            }

            List<AppRule> currentRules;
            List<CustomSchedule> currentCustomSchedules;
            lock (_rulesLock)
            {
                if (_activeRules.Count == 0 && (SafeChildMode == null || !SafeChildMode.IsEnabled))
                {
                    return 0;
                }
                currentRules = _activeRules.ToList();
                currentCustomSchedules = _customSchedules.ToList();
            }

            Process[] processes;
            try
            {
                processes = Process.GetProcesses();
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Failed to enumerate running processes: {ex.Message}");
                return 0;
            }

            int terminatedCount = 0;
            var now = DateTime.Now;

            foreach (var process in processes)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    var procName = process.ProcessName;

                    // 1. Safety Guard: Never terminate protected Windows OS and developer processes
                    if (ProcessSafetyGuard.IsProtected(procName))
                    {
                        continue;
                    }

                    // 2. Check temporary unlock state
                    if (IsTemporarilyUnlocked(procName))
                    {
                        continue;
                    }

                    // 2b. Check Safe Child Shield restriction (e.g. addictive games like Roblox, Steam, Valorant, Minecraft)
                    if (SafeChildMode != null && SafeChildMode.IsProcessRestricted(procName, out var childReason))
                    {
                        var pid = process.Id;
                        try
                        {
                            process.Kill();
                            process.WaitForExit(1000);
                            terminatedCount++;

                            _logger?.Invoke($"[SAFE CHILD] Terminated restricted game: '{procName}' (PID: {pid}). Reason: {childReason}");
                            ProcessTerminated?.Invoke(procName, pid, "Safe Child Shield");
                            await LogTerminationAsync(procName, pid, "Safe Child Shield");
                            continue;
                        }
                        catch (Exception killEx)
                        {
                            _logger?.Invoke($"Failed to terminate child-restricted process '{procName}' (PID: {pid}): {killEx.Message}");
                        }
                    }

                    // 3. Match against active rules
                    var matchingRule = currentRules.FirstOrDefault(rule => IsProcessMatchingRule(process, rule));
                    if (matchingRule != null)
                    {
                        // 4. Verify schedule
                        if (ScheduleTypes.IsRuleCurrentlyActive(matchingRule.ScheduleType, now, currentCustomSchedules, IsFocusModeActive))
                        {
                            var pid = process.Id;
                            try
                            {
                                process.Kill();
                                process.WaitForExit(1000);
                                terminatedCount++;

                                _logger?.Invoke($"[BLOCKED APP] Terminated restricted process: '{procName}' (PID: {pid}). Schedule: {matchingRule.ScheduleType}");

                                ProcessTerminated?.Invoke(procName, pid, matchingRule.ScheduleType);

                                // Log to database
                                await LogTerminationAsync(procName, pid, matchingRule.ScheduleType);
                            }
                            catch (Exception killEx)
                            {
                                _logger?.Invoke($"Failed to terminate matched process '{procName}' (PID: {pid}): {killEx.Message}");
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore processes that exited during enumeration or access denied
                }
                finally
                {
                    process.Dispose();
                }
            }

            return terminatedCount;
        }

        private static bool IsProcessMatchingRule(Process process, AppRule rule)
        {
            var ruleTargetName = Path.GetFileNameWithoutExtension(rule.ProcessName).Trim();
            if (string.Equals(process.ProcessName, ruleTargetName, StringComparison.OrdinalIgnoreCase))
            {
                // If an exact ExePath is specified in the rule, verify path match if accessible
                if (!string.IsNullOrWhiteSpace(rule.ExePath))
                {
                    try
                    {
                        var processPath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(processPath))
                        {
                            return string.Equals(processPath, rule.ExePath, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {
                        // If process path cannot be inspected (e.g. security permissions), fall back to process name match
                        return true;
                    }
                }

                return true;
            }

            return false;
        }

        private static async Task LogTerminationAsync(string processName, int pid, string scheduleType)
        {
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Blocked App",
                    Description = $"Terminated restricted process '{processName}' (PID: {pid}). Schedule: {scheduleType}."
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Best effort log
            }
        }
    }
}
