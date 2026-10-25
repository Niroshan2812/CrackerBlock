using System;
using System.Threading.Tasks;
using AppBlocker.Core.Security;
using AppBlocker.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppBlocker.UI.ViewModels
{
    public partial class LockScreenViewModel : ObservableObject
    {
        private readonly IPinSecurityService _pinService;
        private readonly Action _onAuthenticated;

        public LocalizationManager Loc => LocalizationManager.Instance;

        [RelayCommand]
        public void ToggleLanguage()
        {
            LocalizationManager.Instance.ToggleLanguage();
        }

        [ObservableProperty]
        private bool _isSetupMode;

        [ObservableProperty]
        private string _pinInput = string.Empty;

        [ObservableProperty]
        private string _confirmPinInput = string.Empty;

        [ObservableProperty]
        private bool _isPinVisible;

        [ObservableProperty]
        private bool _isConfirmPinVisible;

        [RelayCommand]
        private void TogglePinVisibility()
        {
            IsPinVisible = !IsPinVisible;
        }

        [RelayCommand]
        private void ToggleConfirmPinVisibility()
        {
            IsConfirmPinVisible = !IsConfirmPinVisible;
        }

        [ObservableProperty]
        private string _statusMessage = string.Empty;

        [ObservableProperty]
        private bool _isError;

        [ObservableProperty]
        private bool _isBusy;

        public LockScreenViewModel(IPinSecurityService pinService, Action onAuthenticated)
        {
            _pinService = pinService;
            _onAuthenticated = onAuthenticated;
        }

        public async Task InitializeAsync()
        {
            IsBusy = true;
            try
            {
                var hasPin = await _pinService.HasMasterPinAsync();
                IsSetupMode = !hasPin;
                PinInput = string.Empty;
                ConfirmPinInput = string.Empty;
                StatusMessage = IsSetupMode 
                    ? "Welcome! Please set your Master PIN to protect this locker." 
                    : "Enter your Master PIN to unlock the control dashboard.";
                IsError = false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        private void AppendDigit(string digit)
        {
            if (PinInput.Length < 12)
            {
                PinInput += digit;
            }
        }

        [RelayCommand]
        private void Backspace()
        {
            if (PinInput.Length > 0)
            {
                PinInput = PinInput.Substring(0, PinInput.Length - 1);
            }
        }

        [RelayCommand]
        private void Clear()
        {
            PinInput = string.Empty;
            ConfirmPinInput = string.Empty;
            StatusMessage = string.Empty;
            IsError = false;
        }

        [RelayCommand]
        private async Task SubmitAsync()
        {
            if (string.IsNullOrWhiteSpace(PinInput))
            {
                SetStatus("Please enter a PIN.", true);
                return;
            }

            if (PinInput.Length < 4)
            {
                SetStatus("PIN must be at least 4 digits.", true);
                return;
            }

            IsBusy = true;
            try
            {
                if (IsSetupMode)
                {
                    if (string.IsNullOrWhiteSpace(ConfirmPinInput))
                    {
                        SetStatus("Please confirm your PIN below.", true);
                        return;
                    }

                    if (PinInput != ConfirmPinInput)
                    {
                        SetStatus("PINs do not match. Please re-enter.", true);
                        return;
                    }

                    var created = await _pinService.SetMasterPinAsync(PinInput);
                    if (created)
                    {
                        SetStatus("Master PIN established! Unlocking...", false);
                        await Task.Delay(400);
                        _onAuthenticated();
                    }
                    else
                    {
                        SetStatus("Failed to save Master PIN.", true);
                    }
                }
                else
                {
                    var isValid = await _pinService.ValidateMasterPinAsync(PinInput);
                    if (isValid)
                    {
                        SetStatus("PIN verified! Access granted.", false);
                        await Task.Delay(300);
                        PinInput = string.Empty;
                        _onAuthenticated();
                    }
                    else
                    {
                        SetStatus("Invalid Master PIN. Attempt logged in audit trail.", true);
                        PinInput = string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                SetStatus($"Authentication error: {ex.Message}", true);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void SetStatus(string message, bool isError)
        {
            StatusMessage = message;
            IsError = isError;
        }
    }
}
