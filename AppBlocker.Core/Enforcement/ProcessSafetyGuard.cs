using System;
using System.Collections.Generic;
using System.IO;

namespace AppBlocker.Core.Enforcement
{
    public static class ProcessSafetyGuard
    {
        // Core OS and development environment processes that must never be terminated
        private static readonly HashSet<string> ProtectedProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "system",
            "idle",
            "smss",
            "csrss",
            "wininit",
            "winlogon",
            "services",
            "lsass",
            "svchost",
            "fontdrvhost",
            "dwm",
            "explorer",
            "sihost",
            "taskhostw",
            "ctfmon",
            "searchhost",
            "startmenuexperiencehost",
            "shellexperiencehost",
            "textinputhost",
            "taskmgr",
            "cmd",
            "powershell",
            "pwsh",
            "conhost",
            "code",
            "devenv",
            "dotnet",
            "antigravity",
            "appblocker",
            "appblocker.ui",
            "appblocker.service"
        };

        public static bool IsProtected(string processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return true;
            }

            var cleanName = processName.Trim();
            if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                cleanName = cleanName.Substring(0, cleanName.Length - 4);
            }

            return ProtectedProcessNames.Contains(cleanName);
        }
    }
}
