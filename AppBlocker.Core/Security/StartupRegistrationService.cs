using System;
using System.IO;
using Microsoft.Win32;

namespace AppBlocker.Core.Security
{
    public static class StartupRegistrationService
    {
        public const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string AppName = "CrackerBlockGuardian";

        public static bool IsLaunchOnStartupEnabled()
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: false);
                return key?.GetValue(AppName) != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool SetLaunchOnStartup(bool enable, string? customExecutablePath = null)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, writable: true);
                if (key == null)
                {
                    return false;
                }

                if (enable)
                {
                    var exePath = customExecutablePath ?? Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exePath))
                    {
                        try
                        {
                            exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        }
                        catch { }
                    }

                    if (!string.IsNullOrEmpty(exePath))
                    {
                        key.SetValue(AppName, $"\"{exePath}\" --tray", RegistryValueKind.String);
                        return true;
                    }

                    return false;
                }
                else
                {
                    key.DeleteValue(AppName, false);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
