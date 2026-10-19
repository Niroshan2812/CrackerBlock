using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace AppBlocker.Core.Enforcement
{
    public class HttpsProxyFilteringEngine : IDisposable
    {
        private readonly ProxyServer _proxyServer;
        private readonly ExplicitProxyEndPoint _explicitEndPoint;
        private readonly List<WebRule> _activeRules = new();
        private readonly object _rulesLock = new();
        private readonly Action<string>? _logger;
        private readonly int _proxyPort;
        private bool _isRunning = false;
        private bool _disposed = false;

        private bool _isPaused = false;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                if (_isPaused != value)
                {
                    _isPaused = value;
                    if (_isRunning)
                    {
                        ConfigureWindowsProxy(enable: !_isPaused, $"127.0.0.1:{_proxyPort}");
                    }
                }
            }
        }

        public event Action<string, string?>? WebAccessIntercepted;

        public HttpsProxyFilteringEngine(int proxyPort = 18888, Action<string>? logger = null)
        {
            _proxyPort = proxyPort;
            _logger = logger;
            _proxyServer = new ProxyServer();

            // Configure Certificate Authority for SSL Decryption & Trusted Interception
            _proxyServer.CertificateManager.RootCertificateName = "AppBlocker Guardian Root CA";
            _proxyServer.CertificateManager.RootCertificateIssuerName = "AppBlocker Guardian Security";
            _proxyServer.CertificateManager.CertificateValidDays = 365;

            _explicitEndPoint = new ExplicitProxyEndPoint(IPAddress.Loopback, _proxyPort, decryptSsl: true);
            _proxyServer.AddEndPoint(_explicitEndPoint);

            _proxyServer.BeforeRequest += OnBeforeRequestAsync;
        }

        public void LoadRules(IEnumerable<WebRule> rules)
        {
            lock (_rulesLock)
            {
                _activeRules.Clear();
                if (rules != null)
                {
                    _activeRules.AddRange(rules.Where(r => r.IsActive));
                }
            }
            _logger?.Invoke($"HttpsProxyFilteringEngine updated with {_activeRules.Count} active rule(s).");
        }

        public async Task LoadRulesFromDatabaseAsync()
        {
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var rules = await db.WebRules.Where(r => r.IsActive).ToListAsync();
                LoadRules(rules);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error loading rules into proxy engine: {ex.Message}");
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            try
            {
                // Ensure Root CA is generated and installed in Trusted Root Certification Authorities
                _proxyServer.CertificateManager.EnsureRootCertificate(
                    userTrustRootCertificate: true, 
                    machineTrustRootCertificate: true);

                _proxyServer.Start();

                // Set as Windows system HTTP & HTTPS proxy
                _proxyServer.SetAsSystemHttpProxy(_explicitEndPoint);
                _proxyServer.SetAsSystemHttpsProxy(_explicitEndPoint);

                // Configure Windows registry proxy for all user profiles & HKLM (essential when running as a SYSTEM service)
                ConfigureWindowsProxy(enable: true, $"127.0.0.1:{_proxyPort}");

                _isRunning = true;
                _logger?.Invoke($"HttpsProxyFilteringEngine active on port {_proxyPort}. System proxy configured.");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Failed to start HttpsProxyFilteringEngine: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            try
            {
                _proxyServer.DisableAllSystemProxies();
                ConfigureWindowsProxy(enable: false);
                _proxyServer.Stop();
                _isRunning = false;
                _logger?.Invoke("HttpsProxyFilteringEngine stopped and system proxy settings cleanly restored.");
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error stopping proxy: {ex.Message}");
            }
        }

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public static void NotifyWinINet()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }

        public static void ConfigureWindowsProxy(bool enable, string proxyServer = "127.0.0.1:18888")
        {
            try
            {
                using var hkcu = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                if (hkcu != null)
                {
                    hkcu.SetValue("ProxyEnable", enable ? 1 : 0, RegistryValueKind.DWord);
                    if (enable)
                    {
                        hkcu.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                    }
                }
            }
            catch { }

            try
            {
                using var hklm = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                if (hklm != null)
                {
                    hklm.SetValue("ProxyEnable", enable ? 1 : 0, RegistryValueKind.DWord);
                    if (enable)
                    {
                        hklm.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                    }
                }
            }
            catch { }

            try
            {
                using var users = Registry.Users;
                foreach (var subKeyName in users.GetSubKeyNames())
                {
                    if (subKeyName.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
                        !subKeyName.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var userKey = users.OpenSubKey($@"{subKeyName}\Software\Microsoft\Windows\CurrentVersion\Internet Settings", writable: true);
                            if (userKey != null)
                            {
                                userKey.SetValue("ProxyEnable", enable ? 1 : 0, RegistryValueKind.DWord);
                                if (enable)
                                {
                                    userKey.SetValue("ProxyServer", proxyServer, RegistryValueKind.String);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            NotifyWinINet();
        }

        private async Task OnBeforeRequestAsync(object sender, SessionEventArgs e)
        {
            await Task.CompletedTask;
            if (IsPaused)
            {
                return;
            }

            try
            {
                var uri = e.HttpClient.Request.RequestUri;
                if (uri == null || string.IsNullOrWhiteSpace(uri.Host))
                {
                    return;
                }

                var host = uri.Host;
                var now = DateTime.Now;

                WebRule? matchedRule = null;
                lock (_rulesLock)
                {
                    matchedRule = _activeRules.FirstOrDefault(r => 
                        ScheduleTypes.IsRuleCurrentlyActive(r.ScheduleType, now) &&
                        IsHostMatchingRule(host, r.UrlOrKeyword));
                }

                if (matchedRule != null)
                {
                    var redirectUrl = matchedRule.RedirectUrl?.Trim();
                    WebAccessIntercepted?.Invoke(host, redirectUrl);

                    if (!string.IsNullOrWhiteSpace(redirectUrl))
                    {
                        // Normalize target redirect URL scheme
                        if (!redirectUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                            !redirectUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            redirectUrl = "https://" + redirectUrl;
                        }

                        // Prevent infinite redirect loop if redirectUrl target matches current host
                        if (Uri.TryCreate(redirectUrl, UriKind.Absolute, out var targetUri) &&
                            string.Equals(targetUri.Host, host, StringComparison.OrdinalIgnoreCase))
                        {
                            return; // Do not redirect back to the blocked host
                        }

                        _logger?.Invoke($"[PROXY REDIRECT] {host} -> {redirectUrl}");

                        var redirectHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <meta http-equiv=""refresh"" content=""0; url={redirectUrl}"">
    <title>AppBlocker Redirecting...</title>
    <script>window.location.replace(""{redirectUrl}"");</script>
</head>
<body style=""background:#0F111A;color:#F8FAFC;font-family:Segoe UI,Roboto,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;"">
    <div style=""text-align:center;background:#1A1D2D;padding:32px;border-radius:12px;border:1px solid #282C42;"">
        <h2 style=""margin:0 0 10px 0;color:#06B6D4;"">🛡️ AppBlocker Redirect</h2>
        <p style=""color:#94A3B8;margin:0 0 16px 0;"">Navigating to designated URL:</p>
        <p><a href=""{redirectUrl}"" style=""color:#8B5CF6;font-weight:bold;text-decoration:none;"">{redirectUrl}</a></p>
    </div>
</body>
</html>";

                        var headers = new Dictionary<string, HttpHeader>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["Location"] = new HttpHeader("Location", redirectUrl),
                            ["Cache-Control"] = new HttpHeader("Cache-Control", "no-cache, no-store, must-revalidate")
                        };

                        e.GenericResponse(redirectHtml, HttpStatusCode.Found, headers);
                    }
                    else
                    {
                        // Direct block without redirect
                        _logger?.Invoke($"[PROXY BLOCK] {host} restricted.");

                        var blockHtml = $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>AppBlocker - Access Restricted</title>
    <style>
        body {{ background: #0F111A; color: #F8FAFC; font-family: Segoe UI, Roboto, sans-serif; display: flex; align-items: center; justify-content: center; height: 100vh; margin: 0; }}
        .card {{ background: #1A1D2D; border: 1px solid #282C42; border-radius: 14px; padding: 40px; text-align: center; max-width: 480px; box-shadow: 0 10px 30px rgba(0,0,0,0.5); }}
        .icon {{ font-size: 48px; margin-bottom: 16px; }}
        h1 {{ margin: 0 0 10px 0; font-size: 22px; color: #EF4444; }}
        p {{ color: #94A3B8; font-size: 14px; line-height: 1.5; margin: 0 0 20px 0; }}
        .badge {{ background: #261E40; color: #8B5CF6; padding: 6px 14px; border-radius: 20px; font-size: 12px; font-weight: bold; border: 1px solid #8B5CF6; display: inline-block; }}
    </style>
</head>
<body>
    <div class=""card"">
        <div class=""icon"">🛡️</div>
        <h1>Website Access Restricted</h1>
        <p><strong>{WebUtility.HtmlEncode(host)}</strong> has been blocked according to your AppBlocker Guardian policy.</p>
        <div class=""badge"">AppBlocker Enterprise Enforcement</div>
    </div>
</body>
</html>";

                        e.GenericResponse(blockHtml, HttpStatusCode.Forbidden, new Dictionary<string, HttpHeader>
                        {
                            ["Cache-Control"] = new HttpHeader("Cache-Control", "no-cache, no-store, must-revalidate")
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error processing proxy request: {ex.Message}");
            }
        }

        public static bool IsHostMatchingRule(string host, string ruleTarget)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(ruleTarget))
            {
                return false;
            }

            var cleanRule = WebFilteringEngine.NormalizeDomain(ruleTarget);
            var cleanHost = host.Trim().ToLowerInvariant();

            // 1. Direct match (e.g. "youtube.com" == "youtube.com")
            if (string.Equals(cleanHost, cleanRule, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 2. Subdomain match (e.g. "en.wikipedia.org" ends with ".wikipedia.org", "www.youtube.com" ends with ".youtube.com")
            if (cleanHost.EndsWith("." + cleanRule, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 3. Cluster expansion match (e.g. "googlevideo.com" or "ytimg.com" for "youtube.com")
            var expanded = WebFilteringEngine.ExpandDomain(cleanRule);
            if (expanded.Any(d => string.Equals(cleanHost, d, StringComparison.OrdinalIgnoreCase) || cleanHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Stop();
                _proxyServer?.Dispose();
            }
        }
    }
}
