using System;
using System.Windows;
using System.Windows.Controls;
using AppBlocker.UI.ViewModels;

namespace AppBlocker.UI
{
    public partial class MainWindow : Window
    {
        private MainViewModel? ViewModel => DataContext as MainViewModel;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (ViewModel != null)
            {
                await ViewModel.InitializeAsync();
            }
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