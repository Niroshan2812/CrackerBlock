using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using AppBlocker.Core.Security;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppBlocker.Tests
{
    public class DatabaseAndSecurityTests : IDisposable
    {
        private readonly string _testDbPath;
        private readonly PinSecurityService _pinService;

        public DatabaseAndSecurityTests()
        {
            _testDbPath = Path.Combine(Path.GetTempPath(), $"appblocker_test_{Guid.NewGuid():N}.db");
            // Set up test database
            using var db = new AppBlockerDbContext(_testDbPath);
            db.Database.EnsureCreated();
            _pinService = new PinSecurityService();
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

        [Fact]
        public void Database_Tables_ExistAndAreConfigured()
        {
            using var db = new AppBlockerDbContext(_testDbPath);

            // Verify User table
            db.Users.Add(new User { PinHash = "dummyHash", CreatedAt = DateTime.UtcNow });
            db.SaveChanges();
            Assert.Equal(1, db.Users.Count());

            // Verify AppRules table
            db.AppRules.Add(new AppRule
            {
                ProcessName = "calc",
                ExePath = @"C:\Windows\System32\calc.exe",
                IsActive = true,
                ScheduleType = ScheduleTypes.Always
            });
            db.SaveChanges();
            Assert.Equal(1, db.AppRules.Count());

            // Verify WebRules table
            db.WebRules.Add(new WebRule
            {
                UrlOrKeyword = "youtube.com",
                IsActive = true,
                ScheduleType = ScheduleTypes.Always
            });
            db.SaveChanges();
            Assert.Equal(1, db.WebRules.Count());

            // Verify Logs table
            db.Logs.Add(new SystemLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = "Blocked App",
                Description = "Terminated calc.exe"
            });
            db.SaveChanges();
            Assert.Equal(1, db.Logs.Count());
        }

        [Fact]
        public void BCrypt_HashAndVerify_WorksCorrectly()
        {
            var pin = "1234";
            var hash = _pinService.HashPin(pin);

            Assert.False(string.IsNullOrEmpty(hash));
            Assert.StartsWith("$2", hash); // BCrypt signature

            Assert.True(_pinService.VerifyPin("1234", hash));
            Assert.False(_pinService.VerifyPin("9999", hash));
            Assert.False(_pinService.VerifyPin("", hash));
        }

        [Fact]
        public async Task PinSecurityService_SetAndValidateMasterPin()
        {
            // Clean test database context
            using var db = new AppBlockerDbContext(_testDbPath);
            var initialUser = await db.Users.FirstOrDefaultAsync();
            if (initialUser != null)
            {
                db.Users.Remove(initialUser);
                await db.SaveChangesAsync();
            }

            var pin = "9876";
            var hash = _pinService.HashPin(pin);
            db.Users.Add(new User { PinHash = hash });
            await db.SaveChangesAsync();

            var isValid = _pinService.VerifyPin("9876", hash);
            Assert.True(isValid);

            var isInvalid = _pinService.VerifyPin("0000", hash);
            Assert.False(isInvalid);
        }

        [Fact]
        public void ScheduleTypes_EvaluatesProperly()
        {
            var now = DateTime.Now;
            Assert.True(ScheduleTypes.IsRuleCurrentlyActive(ScheduleTypes.Always, now));
        }
    }
}
