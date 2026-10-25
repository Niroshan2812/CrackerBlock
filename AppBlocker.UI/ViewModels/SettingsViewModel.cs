using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using AppBlocker.Core.Diagnostics;
using AppBlocker.Core.Security;
using AppBlocker.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppBlocker.UI.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;
        private readonly IPinSecurityService _pinService;
        private readonly Action _onLockDashboard;
        private CancellationTokenSource? _generalStatusCts;

        [ObservableProperty]
        private bool _isServiceConnected;

        [ObservableProperty]
        private string _serviceStatusText = "Checking service...";

        [ObservableProperty]
        private bool _isBlockingPaused;

        [ObservableProperty]
        private bool _isTaskManagerDisabled;

        [ObservableProperty]
        private bool _isIncognitoDisabled = true;

        [ObservableProperty]
        private bool _isDnsOverHttpsDisabled = true;

        [ObservableProperty]
        private bool _isFirefoxProxyLocked = true;

        [ObservableProperty]
        private bool _isLaunchOnStartupEnabled;

        [ObservableProperty]
        private bool _isWatchdogActive = true;

        [ObservableProperty]
        private string _watchdogStatusText = "Dual-Process Guard Active (Immune to taskkill)";

        [ObservableProperty]
        private string _currentPinInput = string.Empty;

        [ObservableProperty]
        private string _newPinInput = string.Empty;

        [ObservableProperty]
        private string _confirmPinInput = string.Empty;

        [ObservableProperty]
        private string _pinStatusMessage = string.Empty;

        [ObservableProperty]
        private bool _isPinSuccess;

        [ObservableProperty]
        private string _generalStatusMessage = string.Empty;

        partial void OnGeneralStatusMessageChanged(string value)
        {
            _generalStatusCts?.Cancel();
            if (!string.IsNullOrWhiteSpace(value))
            {
                _generalStatusCts = new CancellationTokenSource();
                var token = _generalStatusCts.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(30000, token);
                        if (!token.IsCancellationRequested)
                        {
                            GeneralStatusMessage = string.Empty;
                        }
                    }
                    catch { }
                });
            }
        }

        [RelayCommand]
        private void DismissGeneralStatus()
        {
            _generalStatusCts?.Cancel();
            GeneralStatusMessage = string.Empty;
        }

        // Safe Child Mode (Parental Shield)
        [ObservableProperty]
        private bool _isSafeChildModeEnabled;

        [ObservableProperty]
        private bool _safeChildBlockAdultSites = true;

        [ObservableProperty]
        private bool _safeChildBlockSocialMedia = true;

        [ObservableProperty]
        private bool _safeChildBlockGaming = true;

        [ObservableProperty]
        private bool _safeChildEnforceSafeSearch = true;

        [ObservableProperty]
        private bool _safeChildBlockVpnAndBypass = true;

        [ObservableProperty]
        private string _safeChildStatusText = "Shield disabled. Protection inactive.";

        // Desktop Notifications & Real-Time Alerts
        [ObservableProperty]
        private bool _isToastNotificationsEnabled = true;

        [ObservableProperty]
        private bool _isNotificationSoundEnabled = true;

        [ObservableProperty]
        private bool _isBalloonAlertsEnabled = true;

        // 1-Click Diagnostics & Self-Healing Wizard
        [ObservableProperty]
        private bool _isDiagnosticsRunning;

        [ObservableProperty]
        private bool _isDiagnosticsCompleted;

        [ObservableProperty]
        private bool _isDiagnosticsRepaired;

        [ObservableProperty]
        private int _diagnosticsHealthScore = 100;

        [ObservableProperty]
        private string _diagnosticsSummaryText = "Run a full scan to verify Guardian services, proxy certificates, and anti-bypass shields.";

        [ObservableProperty]
        private string _diagnosticsBadgeText = "UNTESTED";

        [ObservableProperty]
        private ObservableCollection<DiagnosticCheckItem> _diagnosticItems = new();

        private int _testNotificationIndex = 0;

        public SettingsViewModel(IpcClientService ipcClient, IPinSecurityService pinService, Action onLockDashboard)
        {
            _ipcClient = ipcClient;
            _pinService = pinService;
            _onLockDashboard = onLockDashboard;

            _isToastNotificationsEnabled = NotificationService.Instance.IsToastEnabled;
            _isNotificationSoundEnabled = NotificationService.Instance.IsSoundEnabled;
            _isBalloonAlertsEnabled = NotificationService.Instance.IsBalloonEnabled;

            try
            {
                _isLaunchOnStartupEnabled = StartupRegistrationService.IsLaunchOnStartupEnabled();
            }
            catch { }
        }

        partial void OnIsLaunchOnStartupEnabledChanged(bool value)
        {
            try
            {
                StartupRegistrationService.SetLaunchOnStartup(value);
                GeneralStatusMessage = value
                    ? "🚀 CrackerBlock will now start automatically in the system tray when Windows boots."
                    : "Startup registration removed. CrackerBlock will only run when opened manually.";
            }
            catch (Exception ex)
            {
                GeneralStatusMessage = $"Failed to update startup configuration: {ex.Message}";
            }
        }

        partial void OnIsToastNotificationsEnabledChanged(bool value)
        {
            NotificationService.Instance.IsToastEnabled = value;
        }

        partial void OnIsNotificationSoundEnabledChanged(bool value)
        {
            NotificationService.Instance.IsSoundEnabled = value;
        }

        partial void OnIsBalloonAlertsEnabledChanged(bool value)
        {
            NotificationService.Instance.IsBalloonEnabled = value;
        }

        public async Task RefreshStateAsync()
        {
            try
            {
                var isAvailable = await _ipcClient.IsServiceAvailableAsync();
                IsServiceConnected = isAvailable;

                try
                {
                    IsLaunchOnStartupEnabled = StartupRegistrationService.IsLaunchOnStartupEnabled();
                }
                catch { }

                if (isAvailable)
                {
                    var status = await _ipcClient.GetStatusAsync();
                    if (status != null)
                    {
                        IsBlockingPaused = status.IsBlockingPaused;
                        ServiceStatusText = $"Running (Uptime since {status.ServiceStartTime.ToLocalTime():g}) - Apps: {status.ActiveAppRuleCount}, Domains: {status.ActiveWebRuleCount}";
                    }
                    else
                    {
                        ServiceStatusText = "Online (Ready)";
                    }

                    IsTaskManagerDisabled = await _ipcClient.IsTaskManagerDisabledAsync();

                    var browserState = await _ipcClient.GetBrowserHardeningStateAsync();
                    IsIncognitoDisabled = browserState.DisableIncognito;
                    IsDnsOverHttpsDisabled = browserState.DisableDnsOverHttps;
                    IsFirefoxProxyLocked = browserState.LockFirefoxProxy;

                    var watchdogState = await _ipcClient.GetWatchdogStatusAsync();
                    IsWatchdogActive = watchdogState.IsWatchdogActive;
                    WatchdogStatusText = watchdogState.IsWatchdogActive
                        ? $"Armed & Active (PID: {watchdogState.WatchdogPid}) - 500ms mutual recovery enabled"
                        : "Inactive - Will automatically launch when background service is active";

                    var childState = await _ipcClient.GetSafeChildModeStateAsync();
                    IsSafeChildModeEnabled = childState.IsEnabled;
                    SafeChildBlockAdultSites = childState.BlockAdultSites;
                    SafeChildBlockSocialMedia = childState.BlockSocialMedia;
                    SafeChildBlockGaming = childState.BlockGaming;
                    SafeChildEnforceSafeSearch = childState.EnforceSafeSearch;
                    SafeChildBlockVpnAndBypass = childState.BlockVpnAndBypass;
                    SafeChildStatusText = IsSafeChildModeEnabled
                        ? "Active & Protecting: Adult, Social Media, Gaming & VPNs restricted."
                        : "Shield disabled. Protection inactive.";
                }
                else
                {
                    ServiceStatusText = "Service Offline / Connecting...";
                    IsWatchdogActive = false;
                    WatchdogStatusText = "Service Offline";
                }
            }
            catch (Exception ex)
            {
                ServiceStatusText = $"Status query error: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task PauseBlockingAsync(string? minutesParam)
        {
            int.TryParse(minutesParam, out int minutes);
            var resp = await _ipcClient.PauseBlockingAsync(minutes);
            GeneralStatusMessage = resp.Message ?? "Blocking paused.";
            await RefreshStateAsync();
        }

        [RelayCommand]
        private async Task ResumeBlockingAsync()
        {
            var resp = await _ipcClient.ResumeBlockingAsync();
            GeneralStatusMessage = resp.Message ?? "Blocking resumed.";
            await RefreshStateAsync();
        }

        [RelayCommand]
        private async Task ToggleTaskManagerAsync()
        {
            var newState = !IsTaskManagerDisabled;
            var resp = await _ipcClient.SetTaskManagerStateAsync(newState);
            GeneralStatusMessage = resp.Message ?? "Task Manager policy updated.";
            IsTaskManagerDisabled = newState;
        }

        [RelayCommand]
        private async Task ToggleIncognitoAsync()
        {
            var newState = !IsIncognitoDisabled;
            var resp = await _ipcClient.SetBrowserHardeningStateAsync(newState, IsDnsOverHttpsDisabled, IsFirefoxProxyLocked);
            IsIncognitoDisabled = newState;
            GeneralStatusMessage = resp.Message ?? $"Incognito / InPrivate browsing {(newState ? "disabled" : "enabled")}.";
        }

        [RelayCommand]
        private async Task ToggleDnsOverHttpsAsync()
        {
            var newState = !IsDnsOverHttpsDisabled;
            var resp = await _ipcClient.SetBrowserHardeningStateAsync(IsIncognitoDisabled, newState, IsFirefoxProxyLocked);
            IsDnsOverHttpsDisabled = newState;
            GeneralStatusMessage = resp.Message ?? $"DNS-over-HTTPS leak protection {(newState ? "enforced" : "disabled")}.";
        }

        [RelayCommand]
        private async Task ToggleFirefoxProxyLockAsync()
        {
            var newState = !IsFirefoxProxyLocked;
            var resp = await _ipcClient.SetBrowserHardeningStateAsync(IsIncognitoDisabled, IsDnsOverHttpsDisabled, newState);
            IsFirefoxProxyLocked = newState;
            GeneralStatusMessage = resp.Message ?? $"Mozilla Firefox system proxy lock {(newState ? "enforced" : "disabled")}.";
        }

        [RelayCommand]
        private async Task FlushDnsAsync()
        {
            var resp = await _ipcClient.FlushDnsAsync();
            GeneralStatusMessage = resp.Message ?? "DNS resolver cache flushed.";
        }

        [RelayCommand]
        private async Task ChangePinAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPinInput) || NewPinInput.Length < 4)
            {
                SetPinStatus("New PIN must be at least 4 digits.", false);
                return;
            }

            if (NewPinInput != ConfirmPinInput)
            {
                SetPinStatus("New PIN and confirmation do not match.", false);
                return;
            }

            var success = await _pinService.ChangeMasterPinAsync(CurrentPinInput, NewPinInput);
            if (success)
            {
                SetPinStatus("Master PIN successfully updated!", true);
                CurrentPinInput = string.Empty;
                NewPinInput = string.Empty;
                ConfirmPinInput = string.Empty;
            }
            else
            {
                SetPinStatus("Incorrect current PIN.", false);
            }
        }

        [RelayCommand]
        private void LockDashboard()
        {
            _onLockDashboard();
        }

        [RelayCommand]
        private void SendTestNotification()
        {
            NotificationService.Instance.ShowTestNotification(_testNotificationIndex++);
            GeneralStatusMessage = "🔔 Test alert sent! Check the bottom-right corner of your desktop.";
        }

        [RelayCommand]
        private async Task ToggleSafeChildModeAsync()
        {
            if (IsSafeChildModeEnabled)
            {
                var promptDlg = new Views.PinPromptDialog(_pinService, "Enter Master PIN to disable Safe Child Protection Shield:");
                if (promptDlg.ShowDialog() != true)
                {
                    return;
                }
            }

            IsSafeChildModeEnabled = !IsSafeChildModeEnabled;
            await SaveSafeChildSettingsAsync();
        }

        [RelayCommand]
        private async Task SaveSafeChildSettingsAsync()
        {
            var dto = new Core.IPC.SafeChildModeDto
            {
                IsEnabled = IsSafeChildModeEnabled,
                BlockAdultSites = SafeChildBlockAdultSites,
                BlockSocialMedia = SafeChildBlockSocialMedia,
                BlockGaming = SafeChildBlockGaming,
                EnforceSafeSearch = SafeChildEnforceSafeSearch,
                BlockVpnAndBypass = SafeChildBlockVpnAndBypass
            };

            await _ipcClient.SetSafeChildModeStateAsync(dto);
            SafeChildStatusText = IsSafeChildModeEnabled
                ? "Active & Protecting: Adult, Social Media, Gaming & VPNs restricted."
                : "Shield disabled. Protection inactive.";
            GeneralStatusMessage = IsSafeChildModeEnabled
                ? "👶 Safe Child Shield activated! Restrictions are now actively enforced."
                : "Safe Child Shield disabled.";
        }

        private void SetPinStatus(string msg, bool success)
        {
            PinStatusMessage = msg;
            IsPinSuccess = success;
        }

        [RelayCommand]
        public async Task RunDiagnosticsAsync()
        {
            IsDiagnosticsRunning = true;
            DiagnosticsSummaryText = "Running 9-point system health & security inspection...";
            try
            {
                var report = await _ipcClient.RunDiagnosticsAsync();
                DiagnosticItems.Clear();
                foreach (var item in report.Items)
                {
                    DiagnosticItems.Add(item);
                }
                DiagnosticsHealthScore = report.OverallHealthScore;
                IsDiagnosticsCompleted = true;

                if (report.FailedCount == 0 && report.WarningCount == 0)
                {
                    DiagnosticsBadgeText = "100% HEALTHY";
                    DiagnosticsSummaryText = $"All {report.TotalCount} systems operational. Defense-in-depth shield fully engaged.";
                }
                else
                {
                    DiagnosticsBadgeText = $"{report.OverallHealthScore}% HEALTH";
                    DiagnosticsSummaryText = $"{report.FailedCount} critical issue(s), {report.WarningCount} warning(s) detected. Click 'Auto-Repair All Issues' to resolve.";
                }
            }
            catch (Exception ex)
            {
                DiagnosticsSummaryText = $"Diagnostic scan error: {ex.Message}";
            }
            finally
            {
                IsDiagnosticsRunning = false;
            }
        }

        [RelayCommand]
        public async Task AutoRepairAllAsync()
        {
            IsDiagnosticsRunning = true;
            DiagnosticsSummaryText = "Applying automated self-healing routines across services, proxy, and certificates...";
            try
            {
                var result = await _ipcClient.RepairDiagnosticsAsync();
                DiagnosticItems.Clear();
                foreach (var item in result.UpdatedReport.Items)
                {
                    DiagnosticItems.Add(item);
                }
                DiagnosticsHealthScore = result.UpdatedReport.OverallHealthScore;
                IsDiagnosticsCompleted = true;
                IsDiagnosticsRepaired = true;

                if (result.UpdatedReport.FailedCount == 0 && result.UpdatedReport.WarningCount == 0)
                {
                    DiagnosticsBadgeText = "100% HEALTHY";
                    DiagnosticsSummaryText = $"✅ Self-healing complete! All {result.UpdatedReport.TotalCount} components successfully verified & repaired.";
                }
                else
                {
                    DiagnosticsBadgeText = $"{result.UpdatedReport.OverallHealthScore}% HEALTH";
                    DiagnosticsSummaryText = result.Message;
                }
                GeneralStatusMessage = "🔧 Self-healing routines executed successfully!";
            }
            catch (Exception ex)
            {
                DiagnosticsSummaryText = $"Self-healing error: {ex.Message}";
            }
            finally
            {
                IsDiagnosticsRunning = false;
            }
        }

        [RelayCommand]
        public async Task RepairSpecificItemAsync(string? checkId)
        {
            if (string.IsNullOrWhiteSpace(checkId)) return;

            IsDiagnosticsRunning = true;
            try
            {
                var result = await _ipcClient.RepairDiagnosticsAsync(new List<string> { checkId });
                DiagnosticItems.Clear();
                foreach (var item in result.UpdatedReport.Items)
                {
                    DiagnosticItems.Add(item);
                }
                DiagnosticsHealthScore = result.UpdatedReport.OverallHealthScore;
                GeneralStatusMessage = $"Repaired check '{checkId}'.";
            }
            catch (Exception ex)
            {
                GeneralStatusMessage = $"Repair error: {ex.Message}";
            }
            finally
            {
                IsDiagnosticsRunning = false;
            }
        }
    }
}
