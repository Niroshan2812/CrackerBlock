using System;
using System.Windows;
using AppBlocker.Core.Data;

namespace AppBlocker.UI
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"AppBlocker recovered from an unexpected error:\n\n{args.Exception.Message}", "AppBlocker Guardian", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };

            try
            {
                AppBlockerDbContext.InitializeDatabase();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"DB init note: {ex.Message}");
            }
        }
    }
}
