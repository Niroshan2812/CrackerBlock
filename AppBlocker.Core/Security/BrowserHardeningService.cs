using System;
using Microsoft.Win32;

namespace AppBlocker.Core.Security
{
    public class BrowserHardeningService
    {
        private static readonly string[] BrowserPolicyPaths = new[]
        {
            @"Software\Policies\Google\Chrome",
            @"Software\Policies\Microsoft\Edge",
            @"Software\Policies\BraveSoftware\Brave"
        };

        private readonly Action<string>? _logger;

        public BrowserHardeningService(Action<string>? logger = null)
        {
            _logger = logger;
        }

        public bool ApplyHardening(bool disableIncognito = true, bool disableDnsOverHttps = true, bool lockFirefoxProxy = true)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            bool success = true;

            // 1. Apply Chromium policies (Chrome, Edge, Brave)
            foreach (var basePath in BrowserPolicyPaths)
            {
                bool isEdge = basePath.Contains("Edge", StringComparison.OrdinalIgnoreCase);

                // HKLM (Machine-wide policy)
                try
                {
                    using var hklmKey = Registry.LocalMachine.CreateSubKey(basePath, writable: true);
                    if (hklmKey != null)
                    {
                        ApplyPoliciesToKey(hklmKey, isEdge, disableIncognito, disableDnsOverHttps);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"[BrowserHardening] HKLM update error for {basePath}: {ex.Message}");
                    success = false;
                }

                // HKCU (Current User policy)
                try
                {
                    using var hkcuKey = Registry.CurrentUser.CreateSubKey(basePath, writable: true);
                    if (hkcuKey != null)
                    {
                        ApplyPoliciesToKey(hkcuKey, isEdge, disableIncognito, disableDnsOverHttps);
                    }
                }
                catch { }

                // Interactive user hives in HKEY_USERS
                try
                {
                    var userHives = Registry.Users.GetSubKeyNames();
                    foreach (var sid in userHives)
                    {
                        if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && 
                            !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using var userKey = Registry.Users.CreateSubKey($@"{sid}\{basePath}", writable: true);
                                if (userKey != null)
                                {
                                    ApplyPoliciesToKey(userKey, isEdge, disableIncognito, disableDnsOverHttps);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // 2. Apply Mozilla Firefox enterprise policies (Private Browsing, System Proxy, Enterprise Roots, DoH)
            try
            {
                // Firefox in HKLM
                try
                {
                    using var hklmRoot = Registry.LocalMachine;
                    ApplyFirefoxPolicies(hklmRoot, disableIncognito, disableDnsOverHttps, lockFirefoxProxy);
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"[BrowserHardening] Firefox HKLM update error: {ex.Message}");
                }

                // Firefox in HKCU
                try
                {
                    using var hkcuRoot = Registry.CurrentUser;
                    ApplyFirefoxPolicies(hkcuRoot, disableIncognito, disableDnsOverHttps, lockFirefoxProxy);
                }
                catch { }

                // Firefox in all interactive user hives
                try
                {
                    var userHives = Registry.Users.GetSubKeyNames();
                    foreach (var sid in userHives)
                    {
                        if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && 
                            !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using var sidRoot = Registry.Users.OpenSubKey(sid, writable: true);
                                if (sidRoot != null)
                                {
                                    ApplyFirefoxPolicies(sidRoot, disableIncognito, disableDnsOverHttps, lockFirefoxProxy);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"[BrowserHardening] Error applying Firefox policies: {ex.Message}");
            }

            _logger?.Invoke($"[BrowserHardening] Applied hardening across Chrome/Edge/Brave/Firefox. Incognito={disableIncognito}, DoH={disableDnsOverHttps}, FirefoxProxyLocked={lockFirefoxProxy}");
            return success;
        }

        public bool RestoreDefaults()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            bool success = true;

            // 1. Restore Chromium policies
            foreach (var basePath in BrowserPolicyPaths)
            {
                bool isEdge = basePath.Contains("Edge", StringComparison.OrdinalIgnoreCase);

                // Remove from HKLM
                try
                {
                    using var hklmKey = Registry.LocalMachine.OpenSubKey(basePath, writable: true);
                    if (hklmKey != null)
                    {
                        RemovePoliciesFromKey(hklmKey, isEdge);
                    }
                }
                catch { }

                // Remove from HKCU
                try
                {
                    using var hkcuKey = Registry.CurrentUser.OpenSubKey(basePath, writable: true);
                    if (hkcuKey != null)
                    {
                        RemovePoliciesFromKey(hkcuKey, isEdge);
                    }
                }
                catch { }

                // Remove from HKU
                try
                {
                    var userHives = Registry.Users.GetSubKeyNames();
                    foreach (var sid in userHives)
                    {
                        if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && 
                            !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                        {
                            try
                            {
                                using var userKey = Registry.Users.OpenSubKey($@"{sid}\{basePath}", writable: true);
                                if (userKey != null)
                                {
                                    RemovePoliciesFromKey(userKey, isEdge);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            // 2. Restore Mozilla Firefox policies
            try
            {
                try { using var hklmRoot = Registry.LocalMachine; RemoveFirefoxPolicies(hklmRoot); } catch { }
                try { using var hkcuRoot = Registry.CurrentUser; RemoveFirefoxPolicies(hkcuRoot); } catch { }

                var userHives = Registry.Users.GetSubKeyNames();
                foreach (var sid in userHives)
                {
                    if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && 
                        !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using var sidRoot = Registry.Users.OpenSubKey(sid, writable: true);
                            if (sidRoot != null)
                            {
                                RemoveFirefoxPolicies(sidRoot);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            _logger?.Invoke("[BrowserHardening] Restored browser default policies across all browsers.");
            return success;
        }

        public (bool IncognitoDisabled, bool DnsOverHttpsDisabled, bool FirefoxProxyLocked) GetCurrentStatus()
        {
            if (!OperatingSystem.IsWindows())
            {
                return (false, false, false);
            }

            bool incognitoDisabled = false;
            bool dohDisabled = false;
            bool firefoxProxyLocked = false;

            try
            {
                // Check Chrome or Edge key in HKLM or HKCU
                using var key = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Google\Chrome") ??
                                Registry.CurrentUser.OpenSubKey(@"Software\Policies\Google\Chrome") ??
                                Registry.LocalMachine.OpenSubKey(@"Software\Policies\Microsoft\Edge") ??
                                Registry.CurrentUser.OpenSubKey(@"Software\Policies\Microsoft\Edge");

                if (key != null)
                {
                    var incognitoVal = key.GetValue("IncognitoModeAvailability") ?? key.GetValue("InPrivateModeAvailability");
                    if (incognitoVal is int intVal && intVal == 1)
                    {
                        incognitoDisabled = true;
                    }

                    var dohVal = key.GetValue("DnsOverHttpsMode");
                    if (dohVal is string strVal && string.Equals(strVal, "off", StringComparison.OrdinalIgnoreCase))
                    {
                        dohDisabled = true;
                    }
                }

                // Check Firefox proxy and private browsing key
                using var ffProxyKey = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Mozilla\Firefox\Proxy") ??
                                      Registry.CurrentUser.OpenSubKey(@"Software\Policies\Mozilla\Firefox\Proxy");
                if (ffProxyKey != null)
                {
                    var modeVal = ffProxyKey.GetValue("Mode");
                    if (modeVal is string mStr && string.Equals(mStr, "system", StringComparison.OrdinalIgnoreCase))
                    {
                        firefoxProxyLocked = true;
                    }
                }

                if (!incognitoDisabled)
                {
                    using var ffKey = Registry.LocalMachine.OpenSubKey(@"Software\Policies\Mozilla\Firefox") ??
                                      Registry.CurrentUser.OpenSubKey(@"Software\Policies\Mozilla\Firefox");
                    if (ffKey != null)
                    {
                        var ffPrivate = ffKey.GetValue("DisablePrivateBrowsing");
                        if (ffPrivate is int pVal && pVal == 1)
                        {
                            incognitoDisabled = true;
                        }
                    }
                }
            }
            catch { }

            return (incognitoDisabled, dohDisabled, firefoxProxyLocked);
        }

        private static void ApplyFirefoxPolicies(RegistryKey rootKey, bool disableIncognito, bool disableDoh, bool lockFirefoxProxy)
        {
            using var firefoxKey = rootKey.CreateSubKey(@"Software\Policies\Mozilla\Firefox", writable: true);
            if (firefoxKey == null) return;

            if (disableIncognito)
            {
                firefoxKey.SetValue("DisablePrivateBrowsing", 1, RegistryValueKind.DWord);
            }
            else
            {
                try { firefoxKey.DeleteValue("DisablePrivateBrowsing", false); } catch { }
            }

            if (disableDoh)
            {
                using var dohKey = firefoxKey.CreateSubKey("DNSOverHTTPS", writable: true);
                if (dohKey != null)
                {
                    dohKey.SetValue("Enabled", 0, RegistryValueKind.DWord);
                    dohKey.SetValue("Locked", 1, RegistryValueKind.DWord);
                }
            }
            else
            {
                try { firefoxKey.DeleteSubKeyTree("DNSOverHTTPS", false); } catch { }
            }

            if (lockFirefoxProxy)
            {
                using var proxyKey = firefoxKey.CreateSubKey("Proxy", writable: true);
                if (proxyKey != null)
                {
                    proxyKey.SetValue("Mode", "system", RegistryValueKind.String);
                    proxyKey.SetValue("Locked", 1, RegistryValueKind.DWord);
                }

                using var certKey = firefoxKey.CreateSubKey("Certificates", writable: true);
                if (certKey != null)
                {
                    certKey.SetValue("ImportEnterpriseRoots", 1, RegistryValueKind.DWord);
                }
            }
            else
            {
                try { firefoxKey.DeleteSubKeyTree("Proxy", false); } catch { }
                try { firefoxKey.DeleteSubKeyTree("Certificates", false); } catch { }
            }
        }

        private static void RemoveFirefoxPolicies(RegistryKey rootKey)
        {
            try
            {
                using var firefoxKey = rootKey.OpenSubKey(@"Software\Policies\Mozilla\Firefox", writable: true);
                if (firefoxKey != null)
                {
                    try { firefoxKey.DeleteValue("DisablePrivateBrowsing", false); } catch { }
                    try { firefoxKey.DeleteSubKeyTree("DNSOverHTTPS", false); } catch { }
                    try { firefoxKey.DeleteSubKeyTree("Proxy", false); } catch { }
                    try { firefoxKey.DeleteSubKeyTree("Certificates", false); } catch { }
                }
            }
            catch { }
        }

        private static void ApplyPoliciesToKey(RegistryKey key, bool isEdge, bool disableIncognito, bool disableDnsOverHttps)
        {
            if (disableIncognito)
            {
                if (isEdge)
                {
                    key.SetValue("InPrivateModeAvailability", 1, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue("IncognitoModeAvailability", 1, RegistryValueKind.DWord);
                }
            }
            else
            {
                try { key.DeleteValue(isEdge ? "InPrivateModeAvailability" : "IncognitoModeAvailability", false); } catch { }
            }

            if (disableDnsOverHttps)
            {
                key.SetValue("DnsOverHttpsMode", "off", RegistryValueKind.String);
                key.SetValue("BuiltInDnsClientEnabled", 0, RegistryValueKind.DWord);
                key.SetValue("ProxyMode", "system", RegistryValueKind.String);
            }
            else
            {
                try { key.DeleteValue("DnsOverHttpsMode", false); } catch { }
                try { key.DeleteValue("BuiltInDnsClientEnabled", false); } catch { }
                try { key.DeleteValue("ProxyMode", false); } catch { }
            }
        }

        private static void RemovePoliciesFromKey(RegistryKey key, bool isEdge)
        {
            try { key.DeleteValue(isEdge ? "InPrivateModeAvailability" : "IncognitoModeAvailability", false); } catch { }
            try { key.DeleteValue("DnsOverHttpsMode", false); } catch { }
            try { key.DeleteValue("BuiltInDnsClientEnabled", false); } catch { }
            try { key.DeleteValue("ProxyMode", false); } catch { }
        }
    }
}
