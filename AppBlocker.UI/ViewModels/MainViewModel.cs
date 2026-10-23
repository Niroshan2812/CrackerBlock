using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using AppBlocker.Core.Security;
using AppBlocker.UI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppBlocker.UI.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly IpcClientService _ipcClient;
        private readonly IPinSecurityService _pinService;
        private readonly DispatcherTimer _heartbeatTimer;

        [ObservableProperty]
        private bool _isAuthenticated;

        [ObservableProperty]
        private int _selectedTabIndex;

        [ObservableProperty]
        private bool _isServiceConnected;

        [ObservableProperty]
        private bool _isBlockingPaused;

        [ObservableProperty]
        private string _serviceStatusBadge = "Connecting to Service...";

        public IPinSecurityService PinService => _pinService;
        public IpcClientService IpcClient => _ipcClient;

        public LockScreenViewModel LockScreenVM { get; }
        public AppsViewModel AppsVM { get; }
        public WebsitesViewModel WebsitesVM { get; }
        public SchedulesViewModel SchedulesVM { get; }
        public LogsViewModel LogsVM { get; }
        public SettingsViewModel SettingsVM { get; }

        public MainViewModel()
        {
            _ipcClient = new IpcClientService();
            _pinService = new PinSecurityService();

            LockScreenVM = new LockScreenViewModel(_pinService, OnAuthenticated);
            AppsVM = new AppsViewModel(_ipcClient);
            WebsitesVM = new WebsitesViewModel(_ipcClient);
            SchedulesVM = new SchedulesViewModel();
            LogsVM = new LogsViewModel();
            SettingsVM = new SettingsViewModel(_ipcClient, _pinService, Lock);

            // Heartbeat to check if AppBlocker.Service daemon is online
            _heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _heartbeatTimer.Tick += async (s, e) => await CheckServiceStatusAsync();
            _heartbeatTimer.Start();
        }

        public async Task InitializeAsync()
        {
            await CheckServiceStatusAsync();
            await LockScreenVM.InitializeAsync();
        }

        private async void OnAuthenticated()
        {
            try
            {
                IsAuthenticated = true;
                SelectedTabIndex = 0;
                await AppsVM.LoadRulesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Auth transition error: {ex.Message}");
            }
        }

        partial void OnSelectedTabIndexChanged(int value)
        {
            _ = LoadTabDataAsync(value);
        }

        public async Task OnTabChangedAsync(int newIndex)
        {
            if (SelectedTabIndex != newIndex)
            {
                SelectedTabIndex = newIndex;
            }
            else
            {
                await LoadTabDataAsync(newIndex);
            }
        }

        public async Task LoadTabDataAsync(int newIndex)
        {
            switch (newIndex)
            {
                case 0:
                    await AppsVM.LoadRulesAsync();
                    break;
                case 1:
                    await WebsitesVM.LoadRulesAsync();
                    break;
                case 2:
                    // Schedules is timer-driven
                    break;
                case 3:
                    await LogsVM.LoadLogsAsync();
                    break;
                case 4:
                    await SettingsVM.RefreshStateAsync();
                    break;
            }
        }

        [RelayCommand]
        public void Lock()
        {
            IsAuthenticated = false;
            _ = LockScreenVM.InitializeAsync();
        }

        public async Task<bool> PauseBlockingAsync(int minutes)
        {
            try
            {
                var resp = await _ipcClient.PauseBlockingAsync(minutes);
                if (resp.Success)
                {
                    IsBlockingPaused = true;
                    await CheckServiceStatusAsync();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public async Task<bool> ResumeBlockingAsync()
        {
            try
            {
                var resp = await _ipcClient.ResumeBlockingAsync();
                if (resp.Success)
                {
                    IsBlockingPaused = false;
                    await CheckServiceStatusAsync();
                    return true;
                }
            }
            catch { }
            return false;
        }

        private async Task CheckServiceStatusAsync()
        {
            try
            {
                var status = await _ipcClient.GetStatusAsync();
                if (status != null)
                {
                    IsServiceConnected = true;
                    IsBlockingPaused = status.IsBlockingPaused;
                    ServiceStatusBadge = IsBlockingPaused 
                        ? "Protection Paused (Background Daemon Active)" 
                        : "Background Daemon Active (LocalSystem)";
                }
                else
                {
                    IsServiceConnected = false;
                    ServiceStatusBadge = "Daemon Offline (Start AppBlocker.Service)";
                }
            }
            catch
            {
                IsServiceConnected = false;
                ServiceStatusBadge = "Daemon Offline";
            }
        }
    }
}
