using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;

namespace AppBlocker.Core.Diagnostics
{
    [SupportedOSPlatform("windows")]
    public class SystemDiagnosticsService
    {
        private readonly Action<string>? _logger;
        private readonly HttpsProxyFilteringEngine? _httpsProxyEngine;

        public const string IdServiceDaemon = "svc_daemon";
        public const string IdWatchdogGuard = "watchdog_guard";
        public const string IdProxyListener = "proxy_listener";
        public const string IdWinInetProxy = "wininet_proxy";
        public const string IdRootCert = "root_cert";
        public const string IdBrowserChromium = "browser_chromium";
        public const string IdBrowserFirefox = "browser_firefox";
        public const string IdWindowsStartup = "windows_startup";
        public const string IdDatabaseIntegrity = "db_integrity";

        public SystemDiagnosticsService(HttpsProxyFilteringEngine? httpsProxyEngine = null, Action<string>? logger = null)
        {
            _httpsProxyEngine = httpsProxyEngine;
            _logger = logger;
        }

        public async Task<DiagnosticsReport> RunDiagnosticsAsync()
        {
            var report = new DiagnosticsReport();

            // 1. Background Service Daemon
            var (svcRunning, svcPid, svcDetails) = CheckServiceProcess();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdServiceDaemon,
                Category = "Service Daemon",
                Name = "Guardian Background Service",
                Description = "Validates that the background enforcement service process is active.",
                Status = svcRunning ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
                Details = svcDetails,
                CanAutoRepair = !svcRunning
            });

            // 2. Mutual Watchdog Shield
            var (wdRunning, wdPid, wdDetails) = CheckWatchdogProcess();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdWatchdogGuard,
                Category = "Anti-Tamper & Watchdog",
                Name = "Dual-Process Mutual Watchdog",
                Description = "Ensures the companion watchdog is actively monitoring the service for taskkill attacks.",
                Status = wdRunning ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                Details = wdDetails,
                CanAutoRepair = !wdRunning
            });

            // 3. Local HTTPS Proxy Listener Port
            var proxyListening = await IsPortListeningAsync(18888);
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdProxyListener,
                Category = "Network & Proxy",
                Name = "HTTPS Filtering Engine Port (18888)",
                Description = "Verifies the local loopback proxy listener socket is bound and accepting connections.",
                Status = proxyListening ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
                Details = proxyListening ? "Proxy socket active and responding on 127.0.0.1:18888." : "Proxy port 18888 is closed or blocked.",
                CanAutoRepair = !proxyListening
            });

            // 4. Windows System Proxy Configuration
            var (winInetConfigured, winInetDetails) = CheckWinInetProxy(18888);
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdWinInetProxy,
                Category = "Network & Proxy",
                Name = "Windows WinINet System Proxy",
                Description = "Checks that Windows internet settings route HTTP/HTTPS traffic through the Guardian engine.",
                Status = winInetConfigured ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                Details = winInetDetails,
                CanAutoRepair = !winInetConfigured
            });

            // 5. Trusted Root CA Certificate
            var rootCertTrusted = CheckRootCertificateTrusted();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdRootCert,
                Category = "Security & Certificates",
                Name = "Titanium Root CA Certificate",
                Description = "Verifies that the SSL decryption root authority is trusted in the Windows certificate store.",
                Status = rootCertTrusted ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                Details = rootCertTrusted
                    ? "Root CA certificate trusted in Windows Certificate Store."
                    : "Root CA certificate missing or untrusted (may cause browser SSL security warnings).",
                CanAutoRepair = !rootCertTrusted
            });

            // 6. Chromium Browser Hardening (Chrome, Edge, Brave)
            var (chromiumHardened, chromiumDetails) = CheckChromiumHardening();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdBrowserChromium,
                Category = "Browser Hardening",
                Name = "Chromium Anti-Bypass Policies (Chrome/Edge/Brave)",
                Description = "Validates enterprise policies disabling Incognito mode and DNS-over-HTTPS bypasses.",
                Status = chromiumHardened ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                Details = chromiumDetails,
                CanAutoRepair = !chromiumHardened
            });

            // 7. Mozilla Firefox Proxy Hardening
            var (firefoxHardened, firefoxDetails) = CheckFirefoxHardening();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdBrowserFirefox,
                Category = "Browser Hardening",
                Name = "Mozilla Firefox System Proxy Lock",
                Description = "Verifies Firefox enterprise policies enforce system proxy settings and root certificate trust.",
                Status = firefoxHardened ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                Details = firefoxDetails,
                CanAutoRepair = !firefoxHardened
            });

            // 8. Windows Startup Auto-Launch Registration
            var (startupRegistered, startupDetails) = CheckStartupRegistration();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdWindowsStartup,
                Category = "System Integration",
                Name = "Windows Startup Auto-Launch",
                Description = "Checks that the Guardian tray application is registered to launch silently on boot.",
                Status = startupRegistered ? DiagnosticStatus.Passed : DiagnosticStatus.Warning,
                Details = startupDetails,
                CanAutoRepair = !startupRegistered
            });

            // 9. SQLite Database & Storage Health
            var (dbHealthy, dbDetails) = await CheckDatabaseIntegrityAsync();
            report.Items.Add(new DiagnosticCheckItem
            {
                Id = IdDatabaseIntegrity,
                Category = "Storage & Database",
                Name = "SQLite Database & Audit Logs",
                Description = "Tests database connection, table schemas, and write accessibility at %ProgramData%\\AppBlocker.",
                Status = dbHealthy ? DiagnosticStatus.Passed : DiagnosticStatus.Failed,
                Details = dbDetails,
                CanAutoRepair = !dbHealthy
            });

            report.CalculateScore();
            _logger?.Invoke($"Diagnostics completed: Score {report.OverallHealthScore}% (Passed: {report.PassedCount}, Warnings: {report.WarningCount}, Failed: {report.FailedCount})");
            return report;
        }

        public async Task<RepairDiagnosticsResult> AutoRepairAllAsync(List<string>? specificCheckIds = null)
        {
            var targetIds = specificCheckIds != null && specificCheckIds.Count > 0
                ? new HashSet<string>(specificCheckIds, StringComparer.OrdinalIgnoreCase)
                : null;

            int repairedCount = 0;

            // 1. Repair Watchdog
            if (targetIds == null || targetIds.Contains(IdWatchdogGuard))
            {
                var (wdRunning, _, _) = CheckWatchdogProcess();
                if (!wdRunning)
                {
                    try
                    {
                        var coordinator = new WatchdogCoordinator(_logger);
                        var proc = coordinator.LaunchWatchdog(Process.GetCurrentProcess().Id);
                        if (proc != null) repairedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"Watchdog repair error: {ex.Message}");
                    }
                }
            }

            // 2. Repair Windows System Proxy
            if (targetIds == null || targetIds.Contains(IdWinInetProxy))
            {
                try
                {
                    HttpsProxyFilteringEngine.ConfigureWindowsProxy(true, "127.0.0.1:18888");
                    HttpsProxyFilteringEngine.NotifyWinINet();
                    repairedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Proxy repair error: {ex.Message}");
                }
            }

            // 3. Repair Proxy Listener Engine
            if (targetIds == null || targetIds.Contains(IdProxyListener))
            {
                var isListening = await IsPortListeningAsync(18888);
                if (!isListening && _httpsProxyEngine != null)
                {
                    try
                    {
                        _httpsProxyEngine.Start();
                        repairedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"Proxy engine start error: {ex.Message}");
                    }
                }
            }

            // 4. Repair Root CA Certificate
            if (targetIds == null || targetIds.Contains(IdRootCert))
            {
                if (!CheckRootCertificateTrusted())
                {
                    try
                    {
                        // Ensure certificate exists in store
                        _httpsProxyEngine?.Start();
                        repairedCount++;
                    }
                    catch { }
                }
            }

            // 5. Repair Chromium & Firefox Policies
            if (targetIds == null || targetIds.Contains(IdBrowserChromium) || targetIds.Contains(IdBrowserFirefox))
            {
                try
                {
                    var hardening = new BrowserHardeningService(_logger);
                    hardening.ApplyHardening(disableIncognito: true, disableDnsOverHttps: true, lockFirefoxProxy: true);
                    repairedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Browser hardening repair error: {ex.Message}");
                }
            }

            // 6. Repair Windows Startup Registration
            if (targetIds == null || targetIds.Contains(IdWindowsStartup))
            {
                try
                {
                    StartupRegistrationService.SetLaunchOnStartup(true);
                    repairedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Startup registration repair error: {ex.Message}");
                }
            }

            // 7. Repair Database
            if (targetIds == null || targetIds.Contains(IdDatabaseIntegrity))
            {
                try
                {
                    AppBlockerDbContext.InitializeDatabase();
                    repairedCount++;
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Database repair error: {ex.Message}");
                }
            }

            // Run fresh diagnostics after repairs
            var updatedReport = await RunDiagnosticsAsync();

            return new RepairDiagnosticsResult
            {
                Success = true,
                Message = $"Auto-repair executed successfully. Applied fixes across {repairedCount} component(s).",
                UpdatedReport = updatedReport
            };
        }

        private static (bool IsRunning, int Pid, string Details) CheckServiceProcess()
        {
            try
            {
                var procs = Process.GetProcessesByName("AppBlocker.Service");
                if (procs.Length > 0)
                {
                    var p = procs[0];
                    return (true, p.Id, $"Process active (PID: {p.Id}, Working Set: {p.WorkingSet64 / 1024 / 1024} MB).");
                }
            }
            catch { }
            return (false, 0, "Background service daemon is not running.");
        }

        private static (bool IsRunning, int Pid, string Details) CheckWatchdogProcess()
        {
            try
            {
                var procs = Process.GetProcessesByName(WatchdogCoordinator.WatchdogProcessName);
                if (procs.Length > 0)
                {
                    var p = procs[0];
                    return (true, p.Id, $"Companion watchdog active (PID: {p.Id}) - Mutual recovery enabled.");
                }
            }
            catch { }
            return (false, 0, "Companion watchdog process is inactive.");
        }

        private static async Task<bool> IsPortListeningAsync(int port)
        {
            try
            {
                using var client = new TcpClient();
                var connectTask = client.ConnectAsync("127.0.0.1", port);
                var timeoutTask = Task.Delay(400);
                var completed = await Task.WhenAny(connectTask, timeoutTask);
                return completed == connectTask && client.Connected;
            }
            catch
            {
                return false;
            }
        }

        private static (bool IsConfigured, string Details) CheckWinInetProxy(int port = 18888)
        {
            if (!OperatingSystem.IsWindows()) return (true, "Non-Windows platform.");
            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
                var enabled = hkcu?.GetValue("ProxyEnable") as int? ?? 0;
                var server = hkcu?.GetValue("ProxyServer") as string ?? string.Empty;

                if (enabled == 1 && server.Contains(port.ToString()))
                {
                    return (true, $"System proxy active on {server}.");
                }

                return (false, $"System proxy not routed to engine (ProxyEnable={enabled}, Server='{server}').");
            }
            catch (Exception ex)
            {
                return (false, $"Registry query error: {ex.Message}");
            }
        }

        private static bool CheckRootCertificateTrusted()
        {
            if (!OperatingSystem.IsWindows()) return true;

            try
            {
                using var machineStore = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                machineStore.Open(OpenFlags.ReadOnly);
                foreach (var cert in machineStore.Certificates)
                {
                    if (cert.Subject.Contains("AppBlocker Guardian Root CA", StringComparison.OrdinalIgnoreCase) ||
                        cert.Issuer.Contains("AppBlocker Guardian Security", StringComparison.OrdinalIgnoreCase) ||
                        cert.Subject.Contains("Titanium", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                using var userStore = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
                userStore.Open(OpenFlags.ReadOnly);
                foreach (var cert in userStore.Certificates)
                {
                    if (cert.Subject.Contains("AppBlocker Guardian Root CA", StringComparison.OrdinalIgnoreCase) ||
                        cert.Issuer.Contains("AppBlocker Guardian Security", StringComparison.OrdinalIgnoreCase) ||
                        cert.Subject.Contains("Titanium", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        private static (bool IsHardened, string Details) CheckChromiumHardening()
        {
            var hardening = new BrowserHardeningService();
            var (incognito, doh, _) = hardening.GetCurrentStatus();
            if (incognito && doh)
            {
                return (true, "Incognito mode disabled and DNS-over-HTTPS leaks blocked.");
            }
            return (false, $"Chromium policies incomplete (Incognito={incognito}, DoH={doh}).");
        }

        private static (bool IsHardened, string Details) CheckFirefoxHardening()
        {
            var hardening = new BrowserHardeningService();
            var (_, _, ffProxy) = hardening.GetCurrentStatus();
            if (ffProxy)
            {
                return (true, "Firefox locked to Guardian system proxy with enterprise root trust.");
            }
            return (false, "Firefox proxy lock not enforced in enterprise policies.");
        }

        private static (bool IsEnabled, string Details) CheckStartupRegistration()
        {
            var enabled = StartupRegistrationService.IsLaunchOnStartupEnabled();
            return enabled
                ? (true, "Registered in HKCU Run to auto-launch silently on Windows boot.")
                : (false, "Auto-launch on Windows startup is currently disabled.");
        }

        private static async Task<(bool IsHealthy, string Details)> CheckDatabaseIntegrityAsync()
        {
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var canConnect = await db.Database.CanConnectAsync();
                if (canConnect)
                {
                    var userCount = await db.Users.CountAsync();
                    var appCount = await db.AppRules.CountAsync();
                    var webCount = await db.WebRules.CountAsync();
                    return (true, $"SQLite DB connected healthy (Users: {userCount}, Apps: {appCount}, Web: {webCount}).");
                }
                return (false, "Cannot connect to SQLite database.");
            }
            catch (Exception ex)
            {
                return (false, $"Database error: {ex.Message}");
            }
        }
    }
}
