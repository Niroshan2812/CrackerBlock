using System;
using System.Threading.Tasks;
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
        private string _safeChildStatusText = "Shield disabled. Protection inactive.";

        // Desktop Notifications & Real-Time Alerts
        [ObservableProperty]
        private bool _isToastNotificationsEnabled = true;

        [ObservableProperty]
        private bool _isNotificationSoundEnabled = true;

        [ObservableProperty]
        private bool _isBalloonAlertsEnabled = true;

        private int _testNotificationIndex = 0;

        public SettingsViewModel(IpcClientService ipcClient, IPinSecurityService pinService, Action onLockDashboard)
        {
            _ipcClient = ipcClient;
            _pinService = pinService;
            _onLockDashboard = onLockDashboard;

            _isToastNotificationsEnabled = NotificationService.Instance.IsToastEnabled;
            _isNotificationSoundEnabled = NotificationService.Instance.IsSoundEnabled;
            _isBalloonAlertsEnabled = NotificationService.Instance.IsBalloonEnabled;
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
                    SafeChildStatusText = IsSafeChildModeEnabled
                        ? "Active & Protecting: Adult, Social Media & Gaming restricted."
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
            var resp = await _ipcClient.SetBrowserHardeningStateAsync(newState, IsDnsOverHttpsDisabled);
            IsIncognitoDisabled = newState;
            GeneralStatusMessage = resp.Message ?? $"Incognito / InPrivate browsing {(newState ? "disabled" : "enabled")}.";
        }

        [RelayCommand]
        private async Task ToggleDnsOverHttpsAsync()
        {
            var newState = !IsDnsOverHttpsDisabled;
            var resp = await _ipcClient.SetBrowserHardeningStateAsync(IsIncognitoDisabled, newState);
            IsDnsOverHttpsDisabled = newState;
            GeneralStatusMessage = resp.Message ?? $"DNS-over-HTTPS leak protection {(newState ? "enforced" : "disabled")}.";
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
                EnforceSafeSearch = SafeChildEnforceSafeSearch
            };

            await _ipcClient.SetSafeChildModeStateAsync(dto);
            SafeChildStatusText = IsSafeChildModeEnabled
                ? "Active & Protecting: Adult, Social Media & Gaming restricted."
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
    }
}
