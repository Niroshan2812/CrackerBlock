using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.Core.Enforcement
{
    public class WebFilteringEngine
    {
        public const string BlockHeader = "# --- BEGIN APPBLOCKER MANAGED HOSTS ---";
        public const string BlockFooter = "# --- END APPBLOCKER MANAGED HOSTS ---";

        private readonly string _hostsFilePath;
        private readonly Action<string>? _logger;
        private readonly object _fileLock = new();

        public bool IsPaused { get; set; } = false;

        [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", SetLastError = true)]
        private static extern bool DnsFlushResolverCache();

        public WebFilteringEngine(string? customHostsFilePath = null, Action<string>? logger = null)
        {
            _logger = logger;
            if (!string.IsNullOrWhiteSpace(customHostsFilePath))
            {
                _hostsFilePath = customHostsFilePath;
            }
            else
            {
                var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
                _hostsFilePath = Path.Combine(systemPath, "drivers", "etc", "hosts");
            }
        }

        public string HostsFilePath => _hostsFilePath;

        public async Task ApplyRulesFromDatabaseAsync()
        {
            try
            {
                await using var db = AppBlockerDbContext.CreateContext();
                var rules = await db.WebRules.Where(r => r.IsActive).ToListAsync();
                await ApplyRulesAsync(rules);
            }
            catch (Exception ex)
            {
                _logger?.Invoke($"Error loading web rules from database: {ex.Message}");
            }
        }

        public Task ApplyRulesAsync(IEnumerable<WebRule> rules)
        {
            return Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        var activeDomains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (!IsPaused && rules != null)
                        {
                            var now = DateTime.Now;
                            foreach (var rule in rules.Where(r => r.IsActive))
                            {
                                if (ScheduleTypes.IsRuleCurrentlyActive(rule.ScheduleType, now))
                                {
                                    var cleaned = NormalizeDomain(rule.UrlOrKeyword);
                                    if (!string.IsNullOrEmpty(cleaned))
                                    {
                                        activeDomains.Add(cleaned);
                                        // Also add www. prefix or stripped prefix
                                        if (cleaned.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                                        {
                                            activeDomains.Add(cleaned.Substring(4));
                                        }
                                        else
                                        {
                                            activeDomains.Add("www." + cleaned);
                                        }
                                    }
                                }
                            }
                        }

                        UpdateHostsFile(activeDomains);
                        FlushDnsCacheInternal();

                        _logger?.Invoke($"WebFilteringEngine applied {activeDomains.Count} blocked domain entries.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"Error applying web filter rules: {ex.Message}");
                    }
                }
            });
        }

        public Task ClearManagedEntriesAsync()
        {
            return Task.Run(() =>
            {
                lock (_fileLock)
                {
                    try
                    {
                        UpdateHostsFile(new HashSet<string>());
                        FlushDnsCacheInternal();
                        _logger?.Invoke("WebFilteringEngine cleared all AppBlocker managed entries.");
                    }
                    catch (Exception ex)
                    {
                        _logger?.Invoke($"Error clearing hosts entries: {ex.Message}");
                    }
                }
            });
        }

        private void UpdateHostsFile(HashSet<string> blockedDomains)
        {
            if (!File.Exists(_hostsFilePath))
            {
                var dir = Path.GetDirectoryName(_hostsFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(_hostsFilePath, string.Empty, Encoding.UTF8);
            }

            // Remove ReadOnly attribute if present
            var attributes = File.GetAttributes(_hostsFilePath);
            var isReadOnly = attributes.HasFlag(FileAttributes.ReadOnly);
            if (isReadOnly)
            {
                File.SetAttributes(_hostsFilePath, attributes & ~FileAttributes.ReadOnly);
            }

            try
            {
                // Backup hosts file once
                var backupPath = _hostsFilePath + ".appblocker.bak";
                if (!File.Exists(backupPath))
                {
                    try
                    {
                        File.Copy(_hostsFilePath, backupPath, overwrite: false);
                    }
                    catch { }
                }

                var existingLines = File.ReadAllLines(_hostsFilePath, Encoding.UTF8);
                var newLines = new List<string>();
                bool inManagedBlock = false;

                foreach (var line in existingLines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.Equals(BlockHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        inManagedBlock = true;
                        continue;
                    }

                    if (trimmed.Equals(BlockFooter, StringComparison.OrdinalIgnoreCase))
                    {
                        inManagedBlock = false;
                        continue;
                    }

                    if (!inManagedBlock)
                    {
                        newLines.Add(line);
                    }
                }

                // Append new managed block if there are blocked domains
                if (blockedDomains.Count > 0)
                {
                    // Clean trailing empty lines
                    while (newLines.Count > 0 && string.IsNullOrWhiteSpace(newLines[newLines.Count - 1]))
                    {
                        newLines.RemoveAt(newLines.Count - 1);
                    }

                    newLines.Add(string.Empty);
                    newLines.Add(BlockHeader);
                    foreach (var domain in blockedDomains.OrderBy(d => d))
                    {
                        newLines.Add($"127.0.0.1 {domain}");
                        newLines.Add($"::1 {domain}");
                    }
                    newLines.Add(BlockFooter);
                    newLines.Add(string.Empty);
                }

                File.WriteAllLines(_hostsFilePath, newLines, Encoding.UTF8);
            }
            finally
            {
                // Restore ReadOnly attribute if it was originally set
                if (isReadOnly)
                {
                    File.SetAttributes(_hostsFilePath, attributes);
                }
            }
        }

        public static string NormalizeDomain(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            var clean = input.Trim().ToLowerInvariant();

            // Strip scheme if present
            if (clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(8);
            }
            else if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                clean = clean.Substring(7);
            }

            // Strip path, query, fragment
            var slashIndex = clean.IndexOfAny(new[] { '/', '?', '#' });
            if (slashIndex >= 0)
            {
                clean = clean.Substring(0, slashIndex);
            }

            // Strip port if present
            var colonIndex = clean.IndexOf(':');
            if (colonIndex >= 0)
            {
                clean = clean.Substring(0, colonIndex);
            }

            return clean.Trim('.');
        }

        public static bool FlushDnsCache()
        {
            return FlushDnsCacheInternal();
        }

        private static bool FlushDnsCacheInternal()
        {
            bool nativeSucceeded = false;
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    nativeSucceeded = DnsFlushResolverCache();
                }
            }
            catch
            {
                nativeSucceeded = false;
            }

            // Fallback or secondary enforcement via ipconfig /flushdns
            if (!nativeSucceeded && OperatingSystem.IsWindows())
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "ipconfig",
                        Arguments = "/flushdns",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit(3000);
                    return proc?.ExitCode == 0;
                }
                catch
                {
                    return false;
                }
            }

            return nativeSucceeded;
        }
    }
}
