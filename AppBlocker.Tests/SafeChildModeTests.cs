using System;
using System.IO;
using System.Runtime.Versioning;
using AppBlocker.Core.Enforcement;
using Xunit;

namespace AppBlocker.Tests
{
    [SupportedOSPlatform("windows")]
    public class SafeChildModeTests : IDisposable
    {
        private readonly string _testSettingsPath;

        public SafeChildModeTests()
        {
            _testSettingsPath = Path.Combine(Path.GetTempPath(), $"safechild_test_{Guid.NewGuid():N}.json");
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testSettingsPath))
                {
                    File.Delete(_testSettingsPath);
                }
            }
            catch { }
        }

        [Fact]
        public void SafeChildMode_RestrictsAdultWebsitesAndKeywords()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = true,
                BlockAdultSites = true
            };

            // Curated adult domains
            Assert.True(service.IsHostRestricted("pornhub.com", out var r1));
            Assert.Contains("Adult", r1);

            Assert.True(service.IsHostRestricted("www.xvideos.com", out _));
            Assert.True(service.IsHostRestricted("m.redtube.com", out _));
            Assert.True(service.IsHostRestricted("onlyfans.com", out _));
            Assert.True(service.IsHostRestricted("bovada.lv", out var r2));
            Assert.Contains("Adult", r2);

            // Educational / normal sites must NOT be blocked
            Assert.False(service.IsHostRestricted("wikipedia.org", out _));
            Assert.False(service.IsHostRestricted("khanacademy.org", out _));
            Assert.False(service.IsHostRestricted("nationalgeographic.com", out _));
        }

        [Fact]
        public void SafeChildMode_RestrictsSocialMediaBrowsing()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = true,
                BlockSocialMedia = true
            };

            Assert.True(service.IsHostRestricted("tiktok.com", out var r1));
            Assert.Contains("Social Media", r1);

            Assert.True(service.IsHostRestricted("www.tiktok.com", out _));
            Assert.True(service.IsHostRestricted("instagram.com", out _));
            Assert.True(service.IsHostRestricted("www.instagram.com", out _));
            Assert.True(service.IsHostRestricted("facebook.com", out _));
            Assert.True(service.IsHostRestricted("reddit.com", out _));
            Assert.True(service.IsHostRestricted("x.com", out _));
            Assert.True(service.IsHostRestricted("twitter.com", out _));
            Assert.True(service.IsHostRestricted("snapchat.com", out _));
        }

        [Fact]
        public void SafeChildMode_RestrictsGamingDomainsAndPortals()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = true,
                BlockGaming = true
            };

            Assert.True(service.IsHostRestricted("roblox.com", out var r1));
            Assert.Contains("Gaming", r1);

            Assert.True(service.IsHostRestricted("www.roblox.com", out _));
            Assert.True(service.IsHostRestricted("steampowered.com", out _));
            Assert.True(service.IsHostRestricted("epicgames.com", out _));
            Assert.True(service.IsHostRestricted("twitch.tv", out _));
            Assert.True(service.IsHostRestricted("poki.com", out _));
            Assert.True(service.IsHostRestricted("crazygames.com", out _));
        }

        [Fact]
        public void SafeChildMode_RestrictsAddictiveGamingProcesses()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = true,
                BlockGaming = true
            };

            Assert.True(service.IsProcessRestricted("RobloxPlayerBeta.exe", out var r1));
            Assert.Contains("Gaming", r1);

            Assert.True(service.IsProcessRestricted("RobloxPlayerLauncher.exe", out _));
            Assert.True(service.IsProcessRestricted("steam.exe", out _));
            Assert.True(service.IsProcessRestricted("EpicGamesLauncher.exe", out _));
            Assert.True(service.IsProcessRestricted("VALORANT.exe", out _));
            Assert.True(service.IsProcessRestricted("RiotClientServices.exe", out _));
            Assert.True(service.IsProcessRestricted("Minecraft.exe", out _));
            Assert.True(service.IsProcessRestricted("Battle.net.exe", out _));

            // Standard productivity applications must NOT be blocked
            Assert.False(service.IsProcessRestricted("notepad.exe", out _));
            Assert.False(service.IsProcessRestricted("chrome.exe", out _));
            Assert.False(service.IsProcessRestricted("code.exe", out _));
        }

        [Fact]
        public void SafeChildMode_EnforcesSafeSearchOnSearchEngines()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = true,
                EnforceSafeSearch = true
            };

            // Google
            var googleUri = new Uri("https://www.google.com/search?q=minecraft");
            bool gRewritten = service.TryEnforceSafeSearch(googleUri, out var gTarget);
            Assert.True(gRewritten);
            Assert.Contains("safe=active", gTarget);

            // Bing
            var bingUri = new Uri("https://www.bing.com/search?q=cartoon");
            bool bRewritten = service.TryEnforceSafeSearch(bingUri, out var bTarget);
            Assert.True(bRewritten);
            Assert.Contains("adlt=strict", bTarget);

            // DuckDuckGo
            var ddgUri = new Uri("https://duckduckgo.com/?q=space");
            bool dRewritten = service.TryEnforceSafeSearch(ddgUri, out var dTarget);
            Assert.True(dRewritten);
            Assert.Contains("kp=1", dTarget);
        }

        [Fact]
        public void SafeChildMode_WhenDisabled_AllowsAllContent()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = false
            };

            Assert.False(service.IsHostRestricted("pornhub.com", out _));
            Assert.False(service.IsHostRestricted("tiktok.com", out _));
            Assert.False(service.IsHostRestricted("roblox.com", out _));
            Assert.False(service.IsProcessRestricted("RobloxPlayerBeta.exe", out _));

            var uri = new Uri("https://www.google.com/search?q=test");
            Assert.False(service.TryEnforceSafeSearch(uri, out _));
        }

        [Fact]
        public void SafeChildMode_SubCategoryToggles_WorkIndependently()
        {
            var service = new SafeChildModeService(_testSettingsPath)
            {
                IsEnabled = true,
                BlockAdultSites = true,
                BlockSocialMedia = false, // Allowed
                BlockGaming = true
            };

            // Social media allowed
            Assert.False(service.IsHostRestricted("tiktok.com", out _));
            Assert.False(service.IsHostRestricted("instagram.com", out _));

            // Adult and gaming still strictly restricted
            Assert.True(service.IsHostRestricted("pornhub.com", out _));
            Assert.True(service.IsHostRestricted("roblox.com", out _));
            Assert.True(service.IsProcessRestricted("steam.exe", out _));
        }

        [Fact]
        public void SafeChildMode_SettingsPersistence_SavesAndReloadsCleanly()
        {
            var service1 = new SafeChildModeService(_testSettingsPath);
            service1.SaveSettings(new SafeChildSettings
            {
                IsEnabled = true,
                BlockAdultSites = true,
                BlockSocialMedia = false,
                BlockGaming = true,
                EnforceSafeSearch = true
            });

            // Reload in new instance
            var service2 = new SafeChildModeService(_testSettingsPath);
            Assert.True(service2.IsEnabled);
            Assert.True(service2.BlockAdultSites);
            Assert.False(service2.BlockSocialMedia);
            Assert.True(service2.BlockGaming);
            Assert.True(service2.EnforceSafeSearch);
        }
    }
}
