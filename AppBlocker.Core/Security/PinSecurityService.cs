using System;
using System.Threading.Tasks;
using AppBlocker.Core.Data;
using AppBlocker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.Core.Security
{
    public class PinSecurityService : IPinSecurityService
    {
        private const int WorkFactor = 11;

        public string HashPin(string pin)
        {
            if (string.IsNullOrEmpty(pin))
            {
                throw new ArgumentException("PIN cannot be null or empty.", nameof(pin));
            }
            return BCrypt.Net.BCrypt.HashPassword(pin, WorkFactor);
        }

        public bool VerifyPin(string pin, string hash)
        {
            if (string.IsNullOrEmpty(pin) || string.IsNullOrEmpty(hash))
            {
                return false;
            }
            try
            {
                return BCrypt.Net.BCrypt.Verify(pin, hash);
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> HasMasterPinAsync()
        {
            AppBlockerDbContext.InitializeDatabase();
            await using var db = AppBlockerDbContext.CreateContext();
            return await db.Users.AnyAsync();
        }

        public async Task<bool> SetMasterPinAsync(string newPin)
        {
            if (string.IsNullOrWhiteSpace(newPin))
            {
                return false;
            }

            AppBlockerDbContext.InitializeDatabase();
            await using var db = AppBlockerDbContext.CreateContext();
            var existingUser = await db.Users.FirstOrDefaultAsync();

            var hashed = HashPin(newPin);

            if (existingUser == null)
            {
                db.Users.Add(new User
                {
                    PinHash = hashed,
                    CreatedAt = DateTime.UtcNow
                });

                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Master PIN Created",
                    Description = "Initial Master PIN has been established."
                });
            }
            else
            {
                existingUser.PinHash = hashed;
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Master PIN Overwrite",
                    Description = "Master PIN has been reset."
                });
            }

            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ValidateMasterPinAsync(string enteredPin)
        {
            AppBlockerDbContext.InitializeDatabase();
            await using var db = AppBlockerDbContext.CreateContext();
            var user = await db.Users.FirstOrDefaultAsync();

            if (user == null)
            {
                // No PIN set yet
                return false;
            }

            var isValid = VerifyPin(enteredPin, user.PinHash);

            if (isValid)
            {
                user.LastLoginAt = DateTime.UtcNow;
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Successful PIN Login",
                    Description = "Master PIN successfully validated."
                });
            }
            else
            {
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Failed PIN",
                    Description = "An invalid Master PIN was entered."
                });
            }

            try
            {
                await db.SaveChangesAsync();
            }
            catch
            {
                // Best-effort audit logging; database write issues must never crash or block authentication
            }

            return isValid;
        }

        public async Task<bool> ChangeMasterPinAsync(string currentPin, string newPin)
        {
            if (string.IsNullOrWhiteSpace(newPin))
            {
                return false;
            }

            AppBlockerDbContext.InitializeDatabase();
            await using var db = AppBlockerDbContext.CreateContext();
            var user = await db.Users.FirstOrDefaultAsync();

            if (user == null)
            {
                return await SetMasterPinAsync(newPin);
            }

            if (!VerifyPin(currentPin, user.PinHash))
            {
                db.Logs.Add(new SystemLog
                {
                    Timestamp = DateTime.UtcNow,
                    EventType = "Failed PIN",
                    Description = "Attempt to change Master PIN failed due to incorrect current PIN."
                });
                await db.SaveChangesAsync();
                return false;
            }

            user.PinHash = HashPin(newPin);
            db.Logs.Add(new SystemLog
            {
                Timestamp = DateTime.UtcNow,
                EventType = "Master PIN Changed",
                Description = "Master PIN was successfully updated."
            });

            await db.SaveChangesAsync();
            return true;
        }
    }
}
