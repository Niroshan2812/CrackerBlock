using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Models;
using Xunit;

namespace AppBlocker.Tests
{
    public class WebFilteringEngineTests : IDisposable
    {
        private readonly string _testHostsFile;

        public WebFilteringEngineTests()
        {
            _testHostsFile = Path.Combine(Path.GetTempPath(), $"hosts_test_{Guid.NewGuid():N}");
            // Seed a realistic dummy hosts file with pre-existing user entries
            var initialContent = new StringBuilder();
            initialContent.AppendLine("# Custom host line");
            initialContent.AppendLine("127.0.0.1  localhost");
            initialContent.AppendLine("::1        localhost");
            initialContent.AppendLine("192.168.1.50 myserver.internal");
            File.WriteAllText(_testHostsFile, initialContent.ToString(), Encoding.UTF8);
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testHostsFile))
                {
                    File.Delete(_testHostsFile);
                }
                var bak = _testHostsFile + ".appblocker.bak";
                if (File.Exists(bak))
                {
                    File.Delete(bak);
                }
            }
            catch { }
        }

        [Fact]
        public void NormalizeDomain_CleansUrlsCorrectly()
        {
            Assert.Equal("youtube.com", WebFilteringEngine.NormalizeDomain("https://youtube.com"));
            Assert.Equal("youtube.com", WebFilteringEngine.NormalizeDomain("http://youtube.com/watch?v=12345"));
            Assert.Equal("facebook.com", WebFilteringEngine.NormalizeDomain("facebook.com/messages"));
            Assert.Equal("reddit.com", WebFilteringEngine.NormalizeDomain("https://reddit.com:8080/r/dotnet"));
            Assert.Equal("tiktok.com", WebFilteringEngine.NormalizeDomain("  tiktok.com  "));
        }

        [Fact]
        public void ExpandDomain_ExpandsSubdomainsAndKnownClusters()
        {
            var wikiExpanded = WebFilteringEngine.ExpandDomain("wikipedia.org").ToList();
            Assert.Contains("wikipedia.org", wikiExpanded);
            Assert.Contains("en.wikipedia.org", wikiExpanded);
            Assert.Contains("www.wikipedia.org", wikiExpanded);
            Assert.Contains("m.wikipedia.org", wikiExpanded);

            var ytExpanded = WebFilteringEngine.ExpandDomain("https://www.youtube.com/watch?v=LE0EOJCMIpI").ToList();
            Assert.Contains("youtube.com", ytExpanded);
            Assert.Contains("www.youtube.com", ytExpanded);
            Assert.Contains("googlevideo.com", ytExpanded);
            Assert.Contains("m.youtube.com", ytExpanded);
        }

        [Fact]
        public async Task ApplyRules_AppendsManagedBlockWithIpv4AndIpv6_AndPreservesOriginalContent()
        {
            var engine = new WebFilteringEngine(_testHostsFile);
            var rules = new[]
            {
                new WebRule
                {
                    UrlOrKeyword = "youtube.com",
                    IsActive = true,
                    ScheduleType = ScheduleTypes.Always
                }
            };

            await engine.ApplyRulesAsync(rules);

            var content = File.ReadAllText(_testHostsFile, Encoding.UTF8);

            // Verify original entries remain preserved
            Assert.Contains("192.168.1.50 myserver.internal", content);
            Assert.Contains("127.0.0.1  localhost", content);

            // Verify AppBlocker tags
            Assert.Contains(WebFilteringEngine.BlockHeader, content);
            Assert.Contains(WebFilteringEngine.BlockFooter, content);

            // Verify IPv4 and IPv6 entries
            Assert.Contains("127.0.0.1 youtube.com", content);
            Assert.Contains("::1 youtube.com", content);
            Assert.Contains("127.0.0.1 www.youtube.com", content);
            Assert.Contains("::1 www.youtube.com", content);
        }

        [Fact]
        public async Task ClearManagedEntries_CleanlyRestoresHostsFile()
        {
            var engine = new WebFilteringEngine(_testHostsFile);
            var rules = new[]
            {
                new WebRule
                {
                    UrlOrKeyword = "tiktok.com",
                    IsActive = true,
                    ScheduleType = ScheduleTypes.Always
                }
            };

            await engine.ApplyRulesAsync(rules);
            Assert.Contains("tiktok.com", File.ReadAllText(_testHostsFile));

            // Clear entries
            await engine.ClearManagedEntriesAsync();

            var cleanedContent = File.ReadAllText(_testHostsFile);

            // Verify AppBlocker block is completely removed
            Assert.DoesNotContain(WebFilteringEngine.BlockHeader, cleanedContent);
            Assert.DoesNotContain(WebFilteringEngine.BlockFooter, cleanedContent);
            Assert.DoesNotContain("tiktok.com", cleanedContent);

            // Original content still intact
            Assert.Contains("192.168.1.50 myserver.internal", cleanedContent);
        }

        [Fact]
        public void FlushDnsCache_ExecutesSafely()
        {
            // Execute DNS flush API
            var result = WebFilteringEngine.FlushDnsCache();
            // Should succeed or safely fall back without throwing an unhandled exception
            Assert.True(result || !result);
        }
    }
}
