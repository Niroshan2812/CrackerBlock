using System;
using System.IO;
using System.Security.Principal;
using Microsoft.Win32;

namespace AppBlocker.Core.Security
{
    public class TaskManagerLockdownService
    {
        public const string DefaultPolicySubKey = @"Software\Microsoft\Windows\CurrentVersion\Policies\System";
        public const string ValueName = "DisableTaskMgr";

        private readonly string _subKeyPath;
        private readonly Action<string>? _logger;

        public TaskManagerLockdownService(string subKeyPath = DefaultPolicySubKey, Action<string>? logger = null)
        {
            _subKeyPath = subKeyPath;
            _logger = logger;
        }

        public bool SetTaskMgrDisabled(bool disable)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            bool success = true;

            // 1. Apply to HKEY_CURRENT_USER (works directly in UI or interactive session)
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(_subKeyPath, writable: true);
                if (key != null)
                {
                    if (disable)
                    {
                        key.SetValue(ValueName, 1, RegistryValueKind.DWord);
                        _logger?.Invoke($"[Lockdown] DisableTaskMgr set to 1 in HKCU\\{_subKeyPath}");
                    }
                    else
                    {
                        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
                        // Also try to remove value for cleanest system restore
                        try { key.DeleteValue(ValueName, false); } catch { }
                        _logger?.Invoke($"[Lockdown] DisableTaskMgr restored to 0/deleted in HKCU\\{_subKeyPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Warning updating HKCU policy: {ex.Message}");
                success = false;
            }

            // 2. If running as LocalSystem, also apply to all interactive user hives under HKEY_USERS
            try
            {
                var userHives = Registry.Users.GetSubKeyNames();
                foreach (var sid in userHives)
                {
                    // Filter out system accounts and Classes hives
                    if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && !sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase))
                    {
                        var userPolicyPath = $@"{sid}\{_subKeyPath}";
                        try
                        {
                            using var userKey = Registry.Users.CreateSubKey(userPolicyPath, writable: true);
                            if (userKey != null)
                            {
                                if (disable)
                                {
                                    userKey.SetValue(ValueName, 1, RegistryValueKind.DWord);
                                    _logger?.Invoke($"[Lockdown] DisableTaskMgr set to 1 in HKU\\{userPolicyPath}");
                                }
                                else
                                {
                                    userKey.SetValue(ValueName, 0, RegistryValueKind.DWord);
                                    try { userKey.DeleteValue(ValueName, false); } catch { }
                                    _logger?.Invoke($"[Lockdown] DisableTaskMgr restored in HKU\\{userPolicyPath}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger?.Invoke($"Warning updating user hive {sid}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error querying HKEY_USERS hives: {ex.Message}");
            }

            return success;
        }

        public bool IsTaskMgrDisabled()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_subKeyPath, writable: false);
                if (key != null)
                {
                    var val = key.GetValue(ValueName);
                    if (val is int intVal && intVal == 1)
                    {
                        return true;
                    }
                }
            }
            catch { }

            return false;
        }

        public bool RestoreDefaults()
        {
            return SetTaskMgrDisabled(false);
        }
    }
}
