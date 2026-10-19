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
