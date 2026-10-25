using System;
using AppBlocker.Core.Security;
using Xunit;

namespace AppBlocker.Tests
{
    public class TrialLicenseServiceTests
    {
        [Fact]
        public void TrialLicense_Initialization_SetsThreeDayEvaluation()
        {
            var service = TrialLicenseService.Instance;
            Assert.NotNull(service.LicenseInfo);
            Assert.True(service.LicenseInfo.TrialDays >= 3.0);
            Assert.False(string.IsNullOrEmpty(service.MachineId));
        }

        [Fact]
        public void TrialLicense_RemainingTime_IsPositiveOnNewInstall()
        {
            var service = TrialLicenseService.Instance;
            var remaining = service.RemainingTime;
            Assert.True(remaining.TotalMinutes > 0, "Remaining trial time should be positive.");
        }

        [Fact]
        public void TrialLicense_FormattedRemainingTime_ReturnsProperStrings()
        {
            var service = TrialLicenseService.Instance;
            var enText = service.GetFormattedRemainingTime(isSinhala: false);
            var siText = service.GetFormattedRemainingTime(isSinhala: true);

            Assert.False(string.IsNullOrWhiteSpace(enText));
            Assert.False(string.IsNullOrWhiteSpace(siText));
        }

        [Fact]
        public void TrialLicense_MasterUnlockKey_ActivatesFullLicense()
        {
            var service = TrialLicenseService.Instance;
            bool success = service.TryActivate("CB-TEST-2026-UNLK", out var message);

            Assert.True(success, "Master evaluation bypass key should activate the license.");
            Assert.True(service.IsActivated);
            Assert.True(service.IsTrialActive);
            Assert.False(service.IsTrialExpired);
            Assert.Contains("activated", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TrialLicense_InvalidKey_IsRejected()
        {
            var service = TrialLicenseService.Instance;
            bool success = service.TryActivate("INVALID-KEY-1234", out var message);

            Assert.False(success);
            Assert.Contains("Invalid", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void TrialLicense_MachineKeyGeneration_MatchesFormat()
        {
            var machineId = "TEST-PC-01";
            var key = TrialLicenseService.GenerateActivationKeyForMachine(machineId);

            Assert.StartsWith("CB-", key);
            Assert.Equal(12, key.Length); // CB-XXXX-XXXX
        }
    }
}
