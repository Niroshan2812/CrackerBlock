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

        public bool ApplyHardening(bool disableIncognito = true, bool disableDnsOverHttps = true)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            bool success = true;

            foreach (var basePath in BrowserPolicyPaths)
            {
                bool isEdge = basePath.Contains("Edge", StringComparison.OrdinalIgnoreCase);

                // 1. Apply to HKLM (Machine-wide policy)
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

                // 2. Apply to HKCU (Current User policy)
                try
                {
                    using var hkcuKey = Registry.CurrentUser.CreateSubKey(basePath, writable: true);
                    if (hkcuKey != null)
                    {
                        ApplyPoliciesToKey(hkcuKey, isEdge, disableIncognito, disableDnsOverHttps);
                    }
                }
                catch { }

                // 3. Apply to all interactive user hives in HKEY_USERS
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

            _logger?.Invoke($"[BrowserHardening] Applied hardening across Chrome/Edge/Brave. IncognitoDisabled={disableIncognito}, DoHDisabled={disableDnsOverHttps}");
            return success;
        }

        public bool RestoreDefaults()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            bool success = true;

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

            _logger?.Invoke("[BrowserHardening] Restored browser default policies.");
            return success;
        }

        public (bool IncognitoDisabled, bool DnsOverHttpsDisabled) GetCurrentStatus()
        {
            if (!OperatingSystem.IsWindows())
            {
                return (false, false);
            }

            bool incognitoDisabled = false;
            bool dohDisabled = false;

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
            }
            catch { }

            return (incognitoDisabled, dohDisabled);
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
