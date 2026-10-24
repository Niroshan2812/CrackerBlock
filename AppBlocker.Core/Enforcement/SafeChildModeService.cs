using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AppBlocker.Core.Data;

namespace AppBlocker.Core.Enforcement
{
    public class SafeChildSettings
    {
        public bool IsEnabled { get; set; } = false;
        public bool BlockAdultSites { get; set; } = true;
        public bool BlockSocialMedia { get; set; } = true;
        public bool BlockGaming { get; set; } = true;
        public bool EnforceSafeSearch { get; set; } = true;
        public bool BlockVpnAndBypass { get; set; } = true;
    }

    public class SafeChildModeService
    {
        private readonly Action<string>? _logger;
        private readonly string _settingsFilePath;
        private readonly object _lock = new();

        public bool IsEnabled { get; set; }
        public bool BlockAdultSites { get; set; } = true;
        public bool BlockSocialMedia { get; set; } = true;
        public bool BlockGaming { get; set; } = true;
        public bool EnforceSafeSearch { get; set; } = true;
        public bool BlockVpnAndBypass { get; set; } = true;

        public event Action<bool>? StateChanged;

        // Curated Adult / Pornography / Explicit / Gambling domains
        private static readonly HashSet<string> AdultDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "pornhub.com", "xvideos.com", "xnxx.com", "xhamster.com", "redtube.com", "youporn.com",
            "onlyfans.com", "chaturbate.com", "livejasmin.com", "stripchat.com", "spankbang.com",
            "tube8.com", "beeg.com", "brazzers.com", "tnaflix.com", "eporner.com", "hqporner.com",
            "rule34.xxx", "gelbooru.com", "danbooru.donmai.us", "e-hentai.org", "nhentai.net",
            "hentaihaven.xxx", "literotica.com", "fapello.com", "erome.com", "cam4.com", "camsoda.com",
            "bongacams.com", "myfreecams.com", "bovada.lv", "bet365.com", "draftkings.com", "fanduel.com",
            "pokerstars.com", "stake.com", "roobet.com", "rollbit.com", "betway.com", "888casino.com"
        };

        // Adult keywords matching root domain segments
        private static readonly string[] AdultKeywords = new[]
        {
            "porn", "xxx", "hentai", "nsfw", "erotic", "adultwork", "camgirl", "sexvideo",
            "casino", "gambling", "poker", "sportsbook"
        };

        // Curated Social Media domains
        private static readonly HashSet<string> SocialMediaDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "tiktok.com", "instagram.com", "facebook.com", "snapchat.com", "x.com", "twitter.com",
            "reddit.com", "tumblr.com", "pinterest.com", "bereal.com", "threads.net", "omegle.com",
            "monkey.app", "discord.com", "t.co", "fb.com", "instagram.net"
        };

        // Curated Gaming web portals
        private static readonly HashSet<string> GamingDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "roblox.com", "steampowered.com", "steamcommunity.com", "epicgames.com", "twitch.tv",
            "crazygames.com", "poki.com", "y8.com", "itch.io", "krunker.io", "slither.io",
            "agar.io", "now.gg", "miniclip.com", "armorgames.com", "kongregate.com", "riotgames.com",
            "battle.net", "minecraft.net", "ea.com", "origin.com", "ubisoft.com"
        };

        // Curated addictive gaming executable names (without .exe)
        private static readonly HashSet<string> GamingProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "robloxplayerbeta", "robloxplayerlauncher", "roblox",
            "steam", "steamwebhelper",
            "epicgameslauncher",
            "valorant", "valorant-win64-shipping",
            "riotclientservices",
            "leagueclient", "leagueclientux", "league of legends",
            "battle.net", "agent",
            "minecraft", "javaw", "minecraftlauncher",
            "eadesktop", "origin",
            "ubisoftconnect", "upc",
            "genshinimpact",
            "fortniteclient-win64-shipping"
        };

        // Curated VPN and Proxy circumvention process names
        private static readonly HashSet<string> VpnAndBypassProcessNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "protonvpn", "protonvpn.service", "protonvpn-service",
            "nordvpn", "nordvpn-service",
            "windscribe", "windscribe-service",
            "expressvpn", "expressvpn-notification-service",
            "hotspotshield", "hsscp",
            "tunnelbear",
            "mullvad-vpn", "mullvad-daemon",
            "surfshark", "surfshark-service",
            "cyberghost",
            "pia-client",
            "openvpn", "openvpn-gui",
            "wireguard",
            "cloudflare-warp", "warp-svc",
            "tor", "obfs4proxy",
            "ultrasurf", "u1903", "u2004", "u2101",
            "psiphon3", "psiphon-tunnel-core",
            "lantern",
            "shadowsocks", "shadowsocksr",
            "v2ray", "xray", "clash", "clash-verge", "nekoray"
        };

        // Curated Web Proxy and Unblocker domains
        private static readonly HashSet<string> WebProxyDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "kproxy.com", "croxyproxy.com", "proxysite.com", "hide.me", "4everproxy.com",
            "blockaway.net", "vpnbook.com", "filterbypass.me", "whoer.net", "hidester.com",
            "proxfree.com", "zalmos.com", "vpnunblocker.com", "proxyium.com", "my-proxy.com",
            "plainproxies.com", "anonymouse.org", "123proxy.net", "webproxy.to"
        };

        public SafeChildModeService(string? customSettingsPath = null, Action<string>? logger = null)
        {
            _logger = logger;
            _settingsFilePath = customSettingsPath ?? Path.Combine(AppBlockerDbContext.DefaultDbDirectory, "safechild.json");
            LoadSettings();
        }

        public void LoadSettings()
        {
            lock (_lock)
            {
                try
                {
                    if (File.Exists(_settingsFilePath))
                    {
                        var json = File.ReadAllText(_settingsFilePath);
                        var settings = JsonSerializer.Deserialize<SafeChildSettings>(json);
                        if (settings != null)
                        {
                            IsEnabled = settings.IsEnabled;
                            BlockAdultSites = settings.BlockAdultSites;
                            BlockSocialMedia = settings.BlockSocialMedia;
                            BlockGaming = settings.BlockGaming;
                            EnforceSafeSearch = settings.EnforceSafeSearch;
                            BlockVpnAndBypass = settings.BlockVpnAndBypass;
                            _logger?.Invoke($"SafeChildMode loaded: Active={IsEnabled}, Adult={BlockAdultSites}, Social={BlockSocialMedia}, Gaming={BlockGaming}, VpnBypass={BlockVpnAndBypass}");
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Error loading SafeChildMode settings: {ex.Message}");
                }
            }
        }

        public void SaveSettings(SafeChildSettings settings)
        {
            lock (_lock)
            {
                try
                {
                    IsEnabled = settings.IsEnabled;
                    BlockAdultSites = settings.BlockAdultSites;
                    BlockSocialMedia = settings.BlockSocialMedia;
                    BlockGaming = settings.BlockGaming;
                    EnforceSafeSearch = settings.EnforceSafeSearch;
                    BlockVpnAndBypass = settings.BlockVpnAndBypass;

                    var dir = Path.GetDirectoryName(_settingsFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_settingsFilePath, json);
                    _logger?.Invoke($"SafeChildMode settings saved. Active={IsEnabled}");
                    StateChanged?.Invoke(IsEnabled);
                }
                catch (Exception ex)
                {
                    _logger?.Invoke($"Error saving SafeChildMode settings: {ex.Message}");
                }
            }
        }

        public bool IsHostRestricted(string host, out string reason)
        {
            reason = string.Empty;
            if (!IsEnabled || string.IsNullOrWhiteSpace(host))
            {
                return false;
            }

            var cleanHost = WebFilteringEngine.NormalizeDomain(host);
            var (sld, _) = WebFilteringEngine.ExtractDomainParts(cleanHost);

            // 1. Adult & Explicit Content / Gambling Shield
            if (BlockAdultSites)
            {
                if (AdultDomains.Any(d => cleanHost.Equals(d, StringComparison.OrdinalIgnoreCase) || cleanHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Adult or Explicit Content";
                    return true;
                }

                if (!string.IsNullOrEmpty(sld) && AdultKeywords.Any(kw => sld.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Adult or Gambling Website";
                    return true;
                }
            }

            // 2. Social Media Restriction
            if (BlockSocialMedia)
            {
                if (SocialMediaDomains.Any(d => cleanHost.Equals(d, StringComparison.OrdinalIgnoreCase) || cleanHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Social Media Platform";
                    return true;
                }
            }

            // 3. Addictive Gaming Web Portals
            if (BlockGaming)
            {
                if (GamingDomains.Any(d => cleanHost.Equals(d, StringComparison.OrdinalIgnoreCase) || cleanHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Online Gaming Portal";
                    return true;
                }
            }

            // 4. Web Proxy & Circumvention Portals
            if (BlockVpnAndBypass)
            {
                if (WebProxyDomains.Any(d => cleanHost.Equals(d, StringComparison.OrdinalIgnoreCase) || cleanHost.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Web Proxy / Circumvention Portal";
                    return true;
                }

                if (!string.IsNullOrEmpty(sld) && (sld.Contains("unblocker", StringComparison.OrdinalIgnoreCase) || sld.Contains("webproxy", StringComparison.OrdinalIgnoreCase)))
                {
                    reason = "Web Proxy / Circumvention Portal";
                    return true;
                }
            }

            return false;
        }

        public bool IsProcessRestricted(string processName, out string reason)
        {
            reason = string.Empty;
            if (!IsEnabled || string.IsNullOrWhiteSpace(processName))
            {
                return false;
            }

            var cleanName = Path.GetFileNameWithoutExtension(processName).Trim().ToLowerInvariant();

            if (BlockGaming && GamingProcessNames.Contains(cleanName))
            {
                reason = "Addictive Gaming Application";
                return true;
            }

            if (BlockVpnAndBypass && VpnAndBypassProcessNames.Contains(cleanName))
            {
                reason = "VPN / Proxy Circumvention Tool";
                return true;
            }

            return false;
        }

        public bool TryEnforceSafeSearch(Uri requestUri, out string? rewrittenUri)
        {
            rewrittenUri = null;
            if (!IsEnabled || !EnforceSafeSearch || requestUri == null)
            {
                return false;
            }

            var host = requestUri.Host.ToLowerInvariant();
            var path = requestUri.AbsolutePath.ToLowerInvariant();
            var query = requestUri.Query;

            // Google SafeSearch enforcement
            if (host.Contains("google.") && path.StartsWith("/search"))
            {
                if (!query.Contains("safe=active", StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(requestUri);
                    builder.Query = string.IsNullOrEmpty(query) ? "safe=active" : query.TrimStart('?') + "&safe=active";
                    rewrittenUri = builder.ToString();
                    return true;
                }
            }

            // Bing SafeSearch enforcement
            if (host.Contains("bing.com") && path.StartsWith("/search"))
            {
                if (!query.Contains("adlt=strict", StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(requestUri);
                    builder.Query = string.IsNullOrEmpty(query) ? "adlt=strict" : query.TrimStart('?') + "&adlt=strict";
                    rewrittenUri = builder.ToString();
                    return true;
                }
            }

            // DuckDuckGo SafeSearch enforcement
            if (host.Contains("duckduckgo.com"))
            {
                if (!query.Contains("kp=1", StringComparison.OrdinalIgnoreCase))
                {
                    var builder = new UriBuilder(requestUri);
                    builder.Query = string.IsNullOrEmpty(query) ? "kp=1" : query.TrimStart('?') + "&kp=1";
                    rewrittenUri = builder.ToString();
                    return true;
                }
            }

            return false;
        }
    }
}
