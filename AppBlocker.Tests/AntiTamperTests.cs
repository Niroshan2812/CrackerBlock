using System.Runtime.Versioning;
using AppBlocker.Core.Security;
using Microsoft.Win32;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class AntiTamperTests : IDisposable
    {
        private const string TestPolicySubKey = @"Software\AppBlocker_Test_Sandbox\Policies\System";

        public AntiTamperTests()
        {
            CleanTestRegistry();
        }

        public void Dispose()
        {
            CleanTestRegistry();
        }

        private static void CleanTestRegistry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\AppBlocker_Test_Sandbox", throwOnMissingSubKey: false);
            }
            catch { }
        }

        [Fact]
        public void TaskManagerLockdown_TogglesDisableTaskMgr_InIsolatedTestKey()
        {
            var lockdown = new TaskManagerLockdownService(TestPolicySubKey);

            // 1. Initially false
            Assert.False(lockdown.IsTaskMgrDisabled());

            // 2. Enable lockdown (DisableTaskMgr = 1)
            var setResult = lockdown.SetTaskMgrDisabled(true);
            Assert.True(setResult);
            Assert.True(lockdown.IsTaskMgrDisabled());

            // Verify registry value directly
            using (var key = Registry.CurrentUser.OpenSubKey(TestPolicySubKey))
            {
                Assert.NotNull(key);
                var val = key.GetValue(TaskManagerLockdownService.ValueName);
                Assert.Equal(1, val);
            }

            // 3. Restore defaults (DisableTaskMgr = 0 / deleted)
            var restoreResult = lockdown.RestoreDefaults();
            Assert.True(restoreResult);
            Assert.False(lockdown.IsTaskMgrDisabled());
        }

        [Fact]
        public void ServiceRecoveryManager_GeneratesWatchdogAndRecoveryScripts()
        {
            var installScript = ServiceRecoveryManager.GenerateInstallScript(@"C:\Program Files\AppBlocker\AppBlocker.Service.exe", "AppBlockerService");
            Assert.Contains("sc.exe failure \"AppBlockerService\" reset= 86400 actions= restart/5000/restart/5000/restart/5000", installScript);
            Assert.Contains("sc.exe failureflag \"AppBlockerService\" 1", installScript);
            Assert.Contains("sc.exe create \"AppBlockerService\"", installScript);
            Assert.Contains("binPath= \"C:\\Program Files\\AppBlocker\\AppBlocker.Service.exe\"", installScript);

            var uninstallScript = ServiceRecoveryManager.GenerateUninstallScript("AppBlockerService");
            Assert.Contains("sc.exe stop \"AppBlockerService\"", uninstallScript);
            Assert.Contains("sc.exe delete \"AppBlockerService\"", uninstallScript);
        }
    }
}
