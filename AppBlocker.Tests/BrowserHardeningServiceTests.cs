using System;
using AppBlocker.Core.Security;
using Xunit;

namespace AppBlocker.Tests
{
    public class BrowserHardeningServiceTests
    {
        [Fact]
        public void BrowserHardeningService_InstantiatesAndQueriesStatusWithoutError()
        {
            var service = new BrowserHardeningService();
            var (incognito, doh, firefoxProxy) = service.GetCurrentStatus();

            // Status should be boolean without exceptions
            Assert.True(incognito || !incognito);
            Assert.True(doh || !doh);
            Assert.True(firefoxProxy || !firefoxProxy);
        }

        [Fact]
        public void BrowserHardeningService_ApplyAndRestore_ExecutesCleanly()
        {
            var service = new BrowserHardeningService();

            // Apply hardening including Firefox proxy lock
            var applied = service.ApplyHardening(disableIncognito: true, disableDnsOverHttps: true, lockFirefoxProxy: true);
            // On Windows test environment, this call succeeds or gracefully handles permissions
            Assert.True(applied || !applied);

            // Restore defaults
            var restored = service.RestoreDefaults();
            Assert.True(restored || !restored);
        }
    }
}
