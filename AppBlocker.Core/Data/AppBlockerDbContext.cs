using System;
using System.IO;
using AppBlocker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AppBlocker.Core.Data
{
    public class AppBlockerDbContext : DbContext
    {
        public DbSet<User> Users { get; set; } = null!;
        public DbSet<AppRule> AppRules { get; set; } = null!;
        public DbSet<WebRule> WebRules { get; set; } = null!;
        public DbSet<CustomSchedule> CustomSchedules { get; set; } = null!;
        public DbSet<SystemLog> Logs { get; set; } = null!;

        public static string DefaultDbDirectory
        {
            get
            {
                var commonAppData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                return Path.Combine(commonAppData, "AppBlocker");
            }
        }

        public static string DefaultDbPath => Path.Combine(DefaultDbDirectory, "appblocker.db");

        private readonly string _connectionString;

        public AppBlockerDbContext()
        {
            EnsureDatabaseDirectory();
            _connectionString = $"Data Source={DefaultDbPath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=15;";
        }

        public AppBlockerDbContext(DbContextOptions<AppBlockerDbContext> options) : base(options)
        {
            EnsureDatabaseDirectory();
            _connectionString = $"Data Source={DefaultDbPath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=15;";
        }

        public AppBlockerDbContext(string customDbPath)
        {
            var dir = Path.GetDirectoryName(customDbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _connectionString = $"Data Source={customDbPath};Mode=ReadWriteCreate;Cache=Shared;Default Timeout=15;";
        }

        public static void EnsureDatabaseDirectory()
        {
            try
            {
                if (!Directory.Exists(DefaultDbDirectory))
                {
                    Directory.CreateDirectory(DefaultDbDirectory);
                }

                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        using var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "icacls.exe",
                            Arguments = $"\"{DefaultDbDirectory}\" /grant *S-1-5-32-545:(OI)(CI)F /T /Q",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                        p?.WaitForExit(2000);
                    }
                    catch { }
                }
            }
            catch
            {
                // Fallback handled in specific environments
            }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite(_connectionString);
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // User configuration
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.PinHash).IsRequired().HasMaxLength(256);
            });

            // AppRule configuration
            modelBuilder.Entity<AppRule>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(260);
                entity.HasIndex(e => e.ProcessName);
                entity.HasIndex(e => e.IsActive);
            });

            // WebRule configuration
            modelBuilder.Entity<WebRule>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.UrlOrKeyword).IsRequired().HasMaxLength(512);
                entity.Property(e => e.RedirectUrl).HasMaxLength(1024);
                entity.HasIndex(e => e.UrlOrKeyword);
                entity.HasIndex(e => e.IsActive);
            });

            // CustomSchedule configuration
            modelBuilder.Entity<CustomSchedule>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Name).IsRequired().HasMaxLength(128);
                entity.Property(e => e.StartTime).IsRequired().HasMaxLength(10);
                entity.Property(e => e.EndTime).IsRequired().HasMaxLength(10);
                entity.Property(e => e.DaysOfWeek).IsRequired().HasMaxLength(64);
                entity.HasIndex(e => e.IsActive);
            });

            // SystemLog configuration
            modelBuilder.Entity<SystemLog>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.EventType).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Description).IsRequired().HasMaxLength(2000);
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.EventType);
            });
        }

        public static AppBlockerDbContext CreateContext()
        {
            EnsureDatabaseDirectory();
            var context = new AppBlockerDbContext();
            return context;
        }

        public static void InitializeDatabase()
        {
            EnsureDatabaseDirectory();
            using var context = CreateContext();
            context.Database.EnsureCreated();
            try
            {
                // Enable Write-Ahead Logging & busy timeout for cross-process concurrency
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
                context.Database.ExecuteSqlRaw("PRAGMA busy_timeout = 5000;");

                // Auto-migrate RedirectUrl column if existing database was created without it
                try
                {
                    context.Database.ExecuteSqlRaw("ALTER TABLE WebRulesTable ADD COLUMN RedirectUrl TEXT;");
                }
                catch { }

                // Auto-create CustomSchedules table if not exists
                try
                {
                    context.Database.ExecuteSqlRaw(@"
                        CREATE TABLE IF NOT EXISTS CustomSchedules (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Name TEXT NOT NULL,
                            StartTime TEXT NOT NULL,
                            EndTime TEXT NOT NULL,
                            DaysOfWeek TEXT NOT NULL,
                            IsActive INTEGER NOT NULL DEFAULT 1
                        );");
                }
                catch { }
            }
            catch
            {
                // Concurrency pragmas best-effort
            }
        }
    }
}
