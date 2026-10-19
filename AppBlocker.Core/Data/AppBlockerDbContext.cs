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
            _connectionString = $"Data Source={DefaultDbPath}";
        }

        public AppBlockerDbContext(DbContextOptions<AppBlockerDbContext> options) : base(options)
        {
            EnsureDatabaseDirectory();
            _connectionString = $"Data Source={DefaultDbPath}";
        }

        public AppBlockerDbContext(string customDbPath)
        {
            var dir = Path.GetDirectoryName(customDbPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            _connectionString = $"Data Source={customDbPath}";
        }

        public static void EnsureDatabaseDirectory()
        {
            try
            {
                if (!Directory.Exists(DefaultDbDirectory))
                {
                    Directory.CreateDirectory(DefaultDbDirectory);
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
                entity.HasIndex(e => e.UrlOrKeyword);
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
                // Enable Write-Ahead Logging for concurrency
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
            }
            catch
            {
                // WAL mode initialization best-effort
            }
        }
    }
}
