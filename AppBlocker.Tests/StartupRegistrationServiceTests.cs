using System;
using System.Runtime.Versioning;
using AppBlocker.Core.Security;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class StartupRegistrationServiceTests : IDisposable
    {
        public StartupRegistrationServiceTests()
        {
            // Clean up any test artifact
            StartupRegistrationService.SetLaunchOnStartup(false);
        }

        public void Dispose()
        {
            StartupRegistrationService.SetLaunchOnStartup(false);
        }

        [Fact]
        public void StartupRegistration_CanQueryStateWithoutException()
        {
            var isEnabled = StartupRegistrationService.IsLaunchOnStartupEnabled();
            Assert.True(isEnabled || !isEnabled);
        }

        [Fact]
        public void StartupRegistration_CanEnableAndDisableAutoLaunch()
        {
            // Enable with test path
            var testExe = @"C:\Mock\AppBlocker.UI.exe";
            var setSuccess = StartupRegistrationService.SetLaunchOnStartup(true, testExe);
            Assert.True(setSuccess);

            // Verify active
            var isEnabled = StartupRegistrationService.IsLaunchOnStartupEnabled();
            Assert.True(isEnabled);

            // Disable
            var disableSuccess = StartupRegistrationService.SetLaunchOnStartup(false);
            Assert.True(disableSuccess);

            // Verify disabled
            Assert.False(StartupRegistrationService.IsLaunchOnStartupEnabled());
        }
    }
}
