using System;
using System.IO;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Enforcement;
using AppBlocker.Core.Models;
using Xunit;

namespace AppBlocker.Tests
{
    public class HttpsProxyFilteringEngineTests : IDisposable
    {
        private readonly string _testDbPath;

        public HttpsProxyFilteringEngineTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"proxy_test_{Guid.NewGuid():N}.db");
            using var db = new AppBlockerDbContext(_testDbPath);
            db.Database.EnsureCreated();
        }

        public void Dispose()
        {
            try
            {
                if (File.Exists(_testDbPath))
                {
                    File.Delete(_testDbPath);
                }
            }
            catch { }
        }

        [Theory]
        [InlineData("youtube.com", "youtube.com", true)]
        [InlineData("www.youtube.com", "youtube.com", true)]
        [InlineData("m.youtube.com", "youtube.com", true)]
        [InlineData("googlevideo.com", "youtube.com", true)]
        [InlineData("r1---sn-ab5sznzs.googlevideo.com", "youtube.com", true)]
        [InlineData("en.wikipedia.org", "wikipedia.org", true)]
        [InlineData("es.wikipedia.org", "wikipedia.org", true)]
        [InlineData("google.com", "youtube.com", false)]
        [InlineData("wikipedia.org", "youtube.com", false)]
        [InlineData("notyoutube.com", "youtube.com", false)]
        public void IsHostMatchingRule_AccuratelyIdentifiesTargetDomains(string incomingHost, string ruleTarget, bool shouldMatch)
        {
            var result = HttpsProxyFilteringEngine.IsHostMatchingRule(incomingHost, ruleTarget);
            Assert.Equal(shouldMatch, result);
        }

        [Fact]
        public async Task WebRule_WithRedirectUrl_SavesAndRetrievesCorrectly()
        {
            await using (var db = new AppBlockerDbContext(_testDbPath))
            {
                db.WebRules.Add(new WebRule
                {
                    UrlOrKeyword = "youtube.com",
                    RedirectUrl = "https://en.wikipedia.org",
                    IsActive = true,
                    ScheduleType = ScheduleTypes.Always,
                    CreatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            await using (var db = new AppBlockerDbContext(_testDbPath))
            {
                var rule = await db.WebRules.FindAsync(1);
                Assert.NotNull(rule);
                Assert.Equal("youtube.com", rule.UrlOrKeyword);
                Assert.Equal("https://en.wikipedia.org", rule.RedirectUrl);
                Assert.True(rule.IsActive);
            }
        }
    }
}
