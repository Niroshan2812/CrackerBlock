using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace AppBlocker.Core.Security
{
    public enum TrialStatus
    {
        Active = 0,
        Expired = 1,
        ClockTampered = 2,
        Activated = 3
    }

    public class TrialLicenseInfo
    {
        public DateTime InstallDateUtc { get; set; }
        public double TrialDays { get; set; } = 3.0;
        public DateTime ExpirationDateUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool IsActivated { get; set; }
        public string? ActivationKey { get; set; }
        public string MachineId { get; set; } = string.Empty;
        public string Signature { get; set; } = string.Empty;
    }

    public class TrialLicenseService
    {
        private static readonly Lazy<TrialLicenseService> _lazyInstance = new(() => new TrialLicenseService());
        public static TrialLicenseService Instance => _lazyInstance.Value;

        private const double DefaultTrialDays = 3.0; // 3 Days evaluation period
        private const string HmacKey = "CrackerBlock_Trial_HMAC_Key_99182";
        private const string SecretSalt = "CrackerBlock_Trial_HMAC_Secret_2026_x89f!";
        private const string ActivationSalt = "CB_SECRET_ACTIVATION_SALT_2026";
        private const string RegistrySubKey = @"Software\AppBlocker";

        // Master bypass unlock keys for evaluation administration
        private static readonly string[] MasterKeys = new[]
        {
            "CB-TEST-2026-UNLK",
            "CB-EVAL-3DAYS-PASS",
            "CB-PRO-LIFETIME-KEY"
        };

        private readonly string _licenseFilePath;
        private readonly object _lock = new();
        private TrialLicenseInfo _info;

        public TrialLicenseInfo LicenseInfo
        {
            get
            {
                lock (_lock)
                {
                    return _info;
                }
            }
        }

        public TrialLicenseService()
        {
            var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var dir = Path.Combine(programData, "AppBlocker");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }

            _licenseFilePath = Path.Combine(dir, "license.dat");
            _info = LoadOrCreateLicense();
        }

        public TrialStatus Status => GetCurrentStatus();

        public bool IsTrialActive => Status == TrialStatus.Active || Status == TrialStatus.Activated;
        public bool IsTrialExpired => Status == TrialStatus.Expired || Status == TrialStatus.ClockTampered;
        public bool IsActivated => _info.IsActivated;

        public TimeSpan RemainingTime
        {
            get
            {
                lock (_lock)
                {
                    if (_info.IsActivated) return TimeSpan.FromDays(3650); // Effectively lifetime
                    var now = DateTime.UtcNow;
                    return _info.ExpirationDateUtc > now ? _info.ExpirationDateUtc - now : TimeSpan.Zero;
                }
            }
        }

        public string MachineId => _info.MachineId;

        public TrialStatus GetCurrentStatus()
        {
            lock (_lock)
            {
                if (_info.IsActivated)
                {
                    return TrialStatus.Activated;
                }

                var now = DateTime.UtcNow;

                // Anti-tamper 1: Clock rollback check (more than 2 hours in the past)
                if (now < _info.LastSeenUtc - TimeSpan.FromHours(2))
                {
                    return TrialStatus.ClockTampered;
                }

                // Anti-tamper 2: Expiration check (3 days passed)
                if (now >= _info.ExpirationDateUtc)
                {
                    return TrialStatus.Expired;
                }

                // Update last seen heartbeat if valid
                if (now > _info.LastSeenUtc)
                {
                    _info.LastSeenUtc = now;
                    SaveLicenseToDiskAndRegistry();
                }

                return TrialStatus.Active;
            }
        }

        public string GetFormattedRemainingTime(bool isSinhala)
        {
            var status = Status;

            if (status == TrialStatus.Activated)
            {
                return isSinhala ? "පූර්ණ බලපත්‍රය සක්‍රීයයි" : "Full License Active";
            }

            if (status == TrialStatus.ClockTampered)
            {
                return isSinhala ? "ඔරලෝසුව වෙනස් කර ඇත (අවලංගුයි)" : "Clock Tampered - Expired";
            }

            if (status == TrialStatus.Expired)
            {
                return isSinhala ? "පරීක්ෂණ කාලය අවසන්" : "Testing Period Expired";
            }

            var rem = RemainingTime;
            if (rem.TotalDays >= 1.0)
            {
                int days = (int)rem.TotalDays;
                int hours = rem.Hours;
                return isSinhala 
                    ? $"දින {days}, පැය {hours} ඉතිරිව ඇත"
                    : $"{days}d {hours}h remaining";
            }
            else
            {
                int hours = rem.Hours;
                int minutes = rem.Minutes;
                return isSinhala
                    ? $"පැය {hours}, මිනිත්තු {minutes} ඉතිරිව ඇත"
                    : $"{hours}h {minutes}m remaining";
            }
        }

        public bool TryActivate(string activationKey, out string message)
        {
            if (string.IsNullOrWhiteSpace(activationKey))
            {
                message = "Activation key cannot be empty.";
                return false;
            }

            var cleanKey = activationKey.Trim().ToUpperInvariant();

            lock (_lock)
            {
                // 1. Check Master Unlock Keys
                foreach (var master in MasterKeys)
                {
                    if (string.Equals(cleanKey, master, StringComparison.OrdinalIgnoreCase))
                    {
                        ApplyActivation(cleanKey);
                        message = "Full license activated successfully with Master Evaluation Key!";
                        return true;
                    }
                }

                // 2. Check Algorithmic Machine-bound Key: CB-XXXX-XXXX
                var expectedMachineKey = GenerateActivationKeyForMachine(_info.MachineId);
                if (string.Equals(cleanKey, expectedMachineKey, StringComparison.OrdinalIgnoreCase))
                {
                    ApplyActivation(cleanKey);
                    message = "Full license activated successfully for this device!";
                    return true;
                }

                message = "Invalid activation key. Please check the key and try again.";
                return false;
            }
        }

        public static string GenerateActivationKeyForMachine(string machineId)
        {
            var raw = $"{machineId.Trim().ToUpperInvariant()}_{ActivationSalt}";
            using var sha = SHA256.Create();
            var hash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(raw)));
            // Format: CB-1234-5678
            return $"CB-{hash[..4]}-{hash.Substring(4, 4)}";
        }

        public static string GetCurrentMachineId()
        {
            try
            {
                var raw = $"{Environment.MachineName}_{Environment.OSVersion.VersionString}";
                using var sha = SHA256.Create();
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));
                var hex = Convert.ToHexString(bytes);
                return $"{hex[..4]}-{hex.Substring(4, 4)}";
            }
            catch
            {
                return "CB-DEV-0001";
            }
        }

        private void ApplyActivation(string key)
        {
            _info.IsActivated = true;
            _info.ActivationKey = key;
            _info.Signature = ComputeSignature(_info.InstallDateUtc, _info.ExpirationDateUtc, _info.TrialDays, _info.IsActivated, _info.MachineId);
            SaveLicenseToDiskAndRegistry();
        }

        private TrialLicenseInfo LoadOrCreateLicense()
        {
            lock (_lock)
            {
                var machineId = GetCurrentMachineId();

                // 1. Try load from file
                if (File.Exists(_licenseFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_licenseFilePath);
                        var loaded = JsonSerializer.Deserialize<TrialLicenseInfo>(json);
                        if (loaded != null && ValidateSignature(loaded))
                        {
                            return loaded;
                        }
                    }
                    catch
                    {
                        // File corrupted or tampered
                    }
                }

                // 2. Try load from Registry Backup if file was deleted to prevent trial reset
                var regInstallDate = ReadRegistryInstallDate();
                if (regInstallDate.HasValue)
                {
                    var recovered = new TrialLicenseInfo
                    {
                        InstallDateUtc = regInstallDate.Value,
                        TrialDays = DefaultTrialDays,
                        ExpirationDateUtc = regInstallDate.Value.AddDays(DefaultTrialDays),
                        LastSeenUtc = DateTime.UtcNow,
                        IsActivated = false,
                        MachineId = machineId
                    };
                    recovered.Signature = ComputeSignature(recovered.InstallDateUtc, recovered.ExpirationDateUtc, recovered.TrialDays, recovered.IsActivated, recovered.MachineId);
                    SaveLicense(recovered);
                    return recovered;
                }

                // 3. New Installation: Initialize 3-Day Evaluation
                var now = DateTime.UtcNow;
                var newLicense = new TrialLicenseInfo
                {
                    InstallDateUtc = now,
                    TrialDays = DefaultTrialDays,
                    ExpirationDateUtc = now.AddDays(DefaultTrialDays),
                    LastSeenUtc = now,
                    IsActivated = false,
                    MachineId = machineId
                };
                newLicense.Signature = ComputeSignature(newLicense.InstallDateUtc, newLicense.ExpirationDateUtc, newLicense.TrialDays, newLicense.IsActivated, newLicense.MachineId);

                SaveLicense(newLicense);
                WriteRegistryInstallDate(newLicense.InstallDateUtc);

                return newLicense;
            }
        }

        private void SaveLicenseToDiskAndRegistry()
        {
            SaveLicense(_info);
            WriteRegistryInstallDate(_info.InstallDateUtc);
        }

        private void SaveLicense(TrialLicenseInfo info)
        {
            try
            {
                var dir = Path.GetDirectoryName(_licenseFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_licenseFilePath, json);
            }
            catch
            {
                // Silently ignore disk write issues
            }
        }

        private static bool ValidateSignature(TrialLicenseInfo info)
        {
            var expected = ComputeSignature(info.InstallDateUtc, info.ExpirationDateUtc, info.TrialDays, info.IsActivated, info.MachineId);
            return string.Equals(expected, info.Signature, StringComparison.OrdinalIgnoreCase);
        }

        private static string ComputeSignature(DateTime installDate, DateTime expireDate, double days, bool activated, string machineId)
        {
            var raw = $"{installDate:O}|{expireDate:O}|{days}|{activated}|{machineId}|{SecretSalt}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacKey));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(raw)));
        }

        private static DateTime? ReadRegistryInstallDate()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Registry.CurrentUser.OpenSubKey(RegistrySubKey, false);
                    var val = key?.GetValue("InstallDateUtc") as string;
                    if (!string.IsNullOrEmpty(val) && DateTime.TryParse(val, out var parsed))
                    {
                        return parsed.ToUniversalTime();
                    }
                }
            }
            catch { }
            return null;
        }

        private static void WriteRegistryInstallDate(DateTime installDate)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RegistrySubKey, true);
                    key?.SetValue("InstallDateUtc", installDate.ToString("O"));
                }
            }
            catch { }
        }
    }
}
