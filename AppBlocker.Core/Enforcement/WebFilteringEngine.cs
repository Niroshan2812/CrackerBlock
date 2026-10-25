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
                                    foreach (var domain in ExpandDomain(rule.UrlOrKeyword))
                                    {
                                        activeDomains.Add(domain);
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
                        newLines.Add($"0.0.0.0 {domain}");
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

            clean = clean.Trim('.');

            // Strip leading "www." for canonical domain matching
            if (clean.StartsWith("www.", StringComparison.OrdinalIgnoreCase) && clean.Length > 4)
            {
                clean = clean.Substring(4);
            }

            // If user entered only service name (e.g. 'facebook' or 'youtube'), normalize to .com
            if (!clean.Contains('.') && !clean.Contains(' ') && clean.Length > 0)
            {
                clean += ".com";
            }

            return clean;
        }

        public static (string sld, string tld) ExtractDomainParts(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
            {
                return (string.Empty, string.Empty);
            }

            var clean = NormalizeDomain(domain);
            var parts = clean.Split('.');
            if (parts.Length <= 1)
            {
                return (clean, string.Empty);
            }

            // Handle multi-part TLDs (e.g. .co.uk, .com.au, .org.uk, .gov.uk, .edu.au)
            if (parts.Length >= 3 && (parts[^2].Equals("co", StringComparison.OrdinalIgnoreCase) ||
                                      parts[^2].Equals("com", StringComparison.OrdinalIgnoreCase) ||
                                      parts[^2].Equals("org", StringComparison.OrdinalIgnoreCase) ||
                                      parts[^2].Equals("gov", StringComparison.OrdinalIgnoreCase) ||
                                      parts[^2].Equals("edu", StringComparison.OrdinalIgnoreCase) ||
                                      parts[^2].Equals("net", StringComparison.OrdinalIgnoreCase)))
            {
                return (parts[^3], $"{parts[^2]}.{parts[^1]}");
            }

            return (parts[^2], parts[^1]);
        }

        private static readonly Dictionary<string, string[]> KnownServiceDomainClusters = new(StringComparer.OrdinalIgnoreCase)
        {
            ["youtube.com"] = new[]
            {
                "youtube.com", "www.youtube.com", "m.youtube.com", "youtu.be",
                "youtube-nocookie.com", "www.youtube-nocookie.com",
                "youtubei.googleapis.com", "googlevideo.com", "www.googlevideo.com",
                "ytimg.com", "www.ytimg.com", "i.ytimg.com", "s.ytimg.com"
            },
            ["wikipedia.org"] = new[]
            {
                "wikipedia.org", "www.wikipedia.org", "wikipedia.com", "www.wikipedia.com", "en.wikipedia.org",
                "m.wikipedia.org", "mobile.wikipedia.org", "wikimedia.org", "www.wikimedia.org"
            },
            ["wikipedia.com"] = new[]
            {
                "wikipedia.org", "www.wikipedia.org", "wikipedia.com", "www.wikipedia.com", "en.wikipedia.org",
                "m.wikipedia.org", "mobile.wikipedia.org", "wikimedia.org", "www.wikimedia.org"
            },
            ["wikimedia.org"] = new[]
            {
                "wikipedia.org", "www.wikipedia.org", "wikipedia.com", "www.wikipedia.com", "en.wikipedia.org",
                "m.wikipedia.org", "mobile.wikipedia.org", "wikimedia.org", "www.wikimedia.org"
            },
            ["reddit.com"] = new[]
            {
                "reddit.com", "www.reddit.com", "old.reddit.com", "new.reddit.com",
                "v.redd.it", "i.redd.it", "redditmedia.com"
            },
            ["twitter.com"] = new[]
            {
                "twitter.com", "www.twitter.com", "x.com", "www.x.com",
                "mobile.twitter.com", "api.twitter.com", "t.co"
            },
            ["x.com"] = new[]
            {
                "twitter.com", "www.twitter.com", "x.com", "www.x.com",
                "mobile.twitter.com", "api.twitter.com", "t.co"
            },
            ["facebook.com"] = new[]
            {
                "facebook.com", "www.facebook.com", "m.facebook.com", "fb.com", "www.fb.com", "fbcdn.net"
            },
            ["instagram.com"] = new[]
            {
                "instagram.com", "www.instagram.com", "m.instagram.com", "cdninstagram.com"
            },
            ["tiktok.com"] = new[]
            {
                "tiktok.com", "www.tiktok.com", "m.tiktok.com", "v16-webapp.tiktok.com"
            },
            ["twitch.tv"] = new[]
            {
                "twitch.tv", "www.twitch.tv", "m.twitch.tv"
            },
            ["netflix.com"] = new[]
            {
                "netflix.com", "www.netflix.com"
            }
        };

        public static IEnumerable<string> ExpandDomain(string rawInput)
        {
            var cleaned = NormalizeDomain(rawInput);
            if (string.IsNullOrEmpty(cleaned))
            {
                yield break;
            }

            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            results.Add(cleaned);

            // Determine root/base domain (e.g. "en.wikipedia.org" -> "wikipedia.org")
            var parts = cleaned.Split('.');
            string baseDomain = cleaned;
            if (parts.Length >= 2)
            {
                int take = 2;
                if (parts.Length >= 3 && (parts[^2].Equals("co", StringComparison.OrdinalIgnoreCase) ||
                                          parts[^2].Equals("com", StringComparison.OrdinalIgnoreCase) ||
                                          parts[^2].Equals("org", StringComparison.OrdinalIgnoreCase) ||
                                          parts[^2].Equals("net", StringComparison.OrdinalIgnoreCase) ||
                                          parts[^2].Equals("gov", StringComparison.OrdinalIgnoreCase) ||
                                          parts[^2].Equals("edu", StringComparison.OrdinalIgnoreCase)))
                {
                    take = 3;
                }
                baseDomain = string.Join(".", parts.TakeLast(Math.Min(take, parts.Length)));
            }

            results.Add(baseDomain);

            if (KnownServiceDomainClusters.TryGetValue(baseDomain, out var cluster))
            {
                foreach (var d in cluster)
                {
                    results.Add(d);
                }
            }

            // Standard common subdomains
            results.Add("www." + baseDomain);
            results.Add("m." + baseDomain);
            results.Add("mobile." + baseDomain);
            results.Add("app." + baseDomain);
            results.Add("web." + baseDomain);
            results.Add("api." + baseDomain);
            results.Add("en." + baseDomain);

            foreach (var r in results)
            {
                yield return r;
            }
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
