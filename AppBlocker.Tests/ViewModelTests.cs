using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using AppBlocker.Core.Security;
using AppBlocker.UI.Services;
using AppBlocker.UI.ViewModels;
using Xunit;

namespace AppBlocker.Tests
{
    public class ViewModelTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly PinSecurityService _pinService;
        private readonly IpcClientService _ipcClient;

        public ViewModelTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"vm_test_{Guid.NewGuid():N}.db");
            using var db = new AppBlockerDbContext(_testDbPath);
            db.Database.EnsureCreated();

            _pinService = new PinSecurityService();
            _ipcClient = new IpcClientService();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
            }
            catch { }
        }

        [Fact]
        public async Task LockScreenViewModel_SetupAndUnlockFlow()
        {
            bool authenticated = false;
            var vm = new LockScreenViewModel(_pinService, () => { authenticated = true; });

            // Initialize
            await vm.InitializeAsync();

            // When entering mismatch PINs
            vm.PinInput = "1234";
            vm.ConfirmPinInput = "9999";
            await vm.SubmitCommand.ExecuteAsync(null);

            Assert.True(vm.IsError);
            Assert.False(authenticated);
        }

        [Fact]
        public async Task AppsViewModel_PreventsBlockingProtectedSystemApps()
        {
            var vm = new AppsViewModel(_ipcClient);
            vm.NewProcessName = "explorer.exe";

            await vm.AddRuleCommand.ExecuteAsync(null);

            Assert.False(vm.IsSuccessMessage);
            Assert.Contains("protected Windows system component", vm.StatusMessage);
        }
    }
}
