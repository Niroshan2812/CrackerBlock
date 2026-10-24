using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using AppBlocker.Core.Diagnostics;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class SystemDiagnosticsServiceTests
    {
        [Fact]
        public async Task SystemDiagnosticsService_RunDiagnostics_ReturnsComprehensive9ItemReport()
        {
            var service = new SystemDiagnosticsService();
            var report = await service.RunDiagnosticsAsync();

            Assert.NotNull(report);
            Assert.Equal(9, report.Items.Count);

            var expectedIds = new[]
            {
                SystemDiagnosticsService.IdServiceDaemon,
                SystemDiagnosticsService.IdWatchdogGuard,
                SystemDiagnosticsService.IdProxyListener,
                SystemDiagnosticsService.IdWinInetProxy,
                SystemDiagnosticsService.IdRootCert,
                SystemDiagnosticsService.IdBrowserChromium,
                SystemDiagnosticsService.IdBrowserFirefox,
                SystemDiagnosticsService.IdWindowsStartup,
                SystemDiagnosticsService.IdDatabaseIntegrity
            };

            foreach (var id in expectedIds)
            {
                var item = report.Items.FirstOrDefault(i => i.Id == id);
                Assert.NotNull(item);
                Assert.False(string.IsNullOrWhiteSpace(item.Name));
                Assert.False(string.IsNullOrWhiteSpace(item.Category));
            }

            Assert.InRange(report.OverallHealthScore, 0, 100);
            Assert.Equal(9, report.PassedCount + report.WarningCount + report.FailedCount);
        }

        [Fact]
        public async Task SystemDiagnosticsService_AutoRepairAll_ExecutesWithoutExceptions()
        {
            var service = new SystemDiagnosticsService();
            var result = await service.AutoRepairAllAsync();

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.UpdatedReport);
            Assert.Equal(9, result.UpdatedReport.Items.Count);
            Assert.InRange(result.UpdatedReport.OverallHealthScore, 0, 100);
        }

        [Fact]
        public async Task SystemDiagnosticsService_AutoRepairSpecificItem_ExecutesCleanly()
        {
            var service = new SystemDiagnosticsService();
            var result = await service.AutoRepairAllAsync(new List<string> 
            { 
                SystemDiagnosticsService.IdWinInetProxy,
                SystemDiagnosticsService.IdBrowserChromium,
                SystemDiagnosticsService.IdBrowserFirefox
            });

            Assert.NotNull(result);
            Assert.True(result.Success);
            Assert.NotNull(result.UpdatedReport);
        }
    }
}
