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

        public SettingsViewModel(IpcClientService ipcClient, IPinSecurityService pinService, Action onLockDashboard)
        {
            _ipcClient = ipcClient;
            _pinService = pinService;
            _onLockDashboard = onLockDashboard;
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

        private void SetPinStatus(string msg, bool success)
        {
            PinStatusMessage = msg;
            IsPinSuccess = success;
        }
    }
}
