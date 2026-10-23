using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AppBlocker.UI.Services;
using AppBlocker.UI.ViewModels;
using AppBlocker.UI.Views;

namespace AppBlocker.UI
{
    public partial class MainWindow : Window
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;
        private SystemTrayService? _trayService;
        private AuditLogNotificationWatcher? _logNotificationWatcher;
        private bool _isExplicitExit = false;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;
            Closed += MainWindow_Closed;

            InitializeSystemTray();
        }

        private void InitializeSystemTray()
        {
            try
            {
                _trayService = new SystemTrayService();
                NotificationService.Instance.SetTrayService(_trayService);

                _trayService.OpenDashboardRequested += OnOpenDashboardRequested;
                _trayService.LockRequested += OnLockRequested;
                _trayService.PauseRequested += OnTrayPauseRequested;
                _trayService.ResumeRequested += OnTrayResumeRequested;
                _trayService.ExitRequested += OnTrayExitRequested;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"System tray initialization failed: {ex.Message}");
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                ViewModel.PropertyChanged += (s, args) =>
                {
                    if (args.PropertyName == nameof(MainViewModel.IsBlockingPaused))
                    {
                        _trayService?.UpdateState(ViewModel.IsBlockingPaused);
                    }
                };

                await ViewModel.InitializeAsync();
                _trayService?.UpdateState(ViewModel.IsBlockingPaused);
            }

            try
            {
                _logNotificationWatcher = new AuditLogNotificationWatcher();
                await _logNotificationWatcher.StartAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Notification watcher start error: {ex.Message}");
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isExplicitExit)
            {
                // Minimize to tray instead of quitting
                e.Cancel = true;
                Hide();
                _trayService?.ShowNotification("AppBlocker Guardian Active", "AppBlocker is running in the background. Protection remains active.");
            }
        }

        private async void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (_logNotificationWatcher != null)
            {
                await _logNotificationWatcher.StopAsync();
            }
            _trayService?.Dispose();
        }

        private void OnOpenDashboardRequested()
        {
            Dispatcher.Invoke(() =>
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }

        private void OnLockRequested()
        {
            Dispatcher.Invoke(() =>
            {
                ViewModel?.Lock();
                Show();
                WindowState = WindowState.Normal;
                Activate();
            });
        }

        private void OnTrayPauseRequested(int minutes)
        {
            Dispatcher.Invoke(async () =>
            {
                if (ViewModel == null) return;

                var prompt = minutes > 0 
                    ? $"Enter your Master PIN to pause protection for {minutes} minute(s)." 
                    : "Enter your Master PIN to pause protection indefinitely.";

                var dlg = new PinPromptDialog(ViewModel.PinService, prompt)
                {
                    Owner = IsVisible ? this : null
                };

                if (dlg.ShowDialog() == true)
                {
                    var success = await ViewModel.PauseBlockingAsync(minutes);
                    if (success)
                    {
                        _trayService?.UpdateState(true, $"Paused ({(minutes > 0 ? $"{minutes}m" : "Indefinite")})");
                        _trayService?.ShowNotification("Protection Paused", minutes > 0 ? $"Enforcement paused for {minutes}m." : "Enforcement paused indefinitely.");
                    }
                }
            });
        }

        private void OnTrayResumeRequested()
        {
            Dispatcher.Invoke(async () =>
            {
                if (ViewModel == null) return;
                var success = await ViewModel.ResumeBlockingAsync();
                if (success)
                {
                    _trayService?.UpdateState(false);
                    _trayService?.ShowNotification("Protection Resumed", "App and Web enforcement is now actively protecting you.");
                }
            });
        }

        private void OnTrayExitRequested()
        {
            Dispatcher.Invoke(() =>
            {
                if (ViewModel == null) return;

                var dlg = new PinPromptDialog(ViewModel.PinService, "Enter your Master PIN to completely exit the AppBlocker Guardian UI.")
                {
                    Owner = IsVisible ? this : null
                };

                if (dlg.ShowDialog() == true)
                {
                    _isExplicitExit = true;
                    _trayService?.Dispose();
                    System.Windows.Application.Current.Shutdown();
                }
            });
        }

        private async void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag != null && int.TryParse(elem.Tag.ToString(), out int tabIndex))
            {
                if (ViewModel != null)
                {
                    await ViewModel.OnTabChangedAsync(tabIndex);
                }
            }
        }
    }
}