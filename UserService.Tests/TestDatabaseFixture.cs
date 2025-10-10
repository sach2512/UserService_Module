using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using UserService.Infrastructure.Identity;
using UserService.Infrastructure.Persistence;

namespace UserService.Tests
{
    public class TestDatabaseFixture : IDisposable
    {
        public IServiceProvider ServiceProvider { get; }
        public UserDbContext DbContext { get; }

        public TestDatabaseFixture()
        {
            // 🧭 STEP 1: Locate appsettings.json
            string appSettingsPath = null!;
            var baseDir = AppContext.BaseDirectory;
            var candidate = Path.Combine(baseDir, "appsettings.json");

            if (File.Exists(candidate))
            {
                appSettingsPath = candidate;
            }
            else
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
                    dir = dir.Parent;

                if (dir == null)
                    throw new FileNotFoundException("❌ Could not locate appsettings.json in any parent directory.");

                appSettingsPath = Path.Combine(dir.FullName, "appsettings.json");
            }

            Console.WriteLine($"✅ Using appsettings.json from: {appSettingsPath}");

            // 🧩 STEP 2: Load configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
                .Build();

            var connectionString = configuration.GetConnectionString("MyConnectionString")
                ?? throw new InvalidOperationException("Missing 'MyConnectionString' in appsettings.json");

            Console.WriteLine($"✅ Connection String loaded: {connectionString}");

            // 🧱 STEP 3: Configure DI
            var services = new ServiceCollection();

            // Add logging (fix for UserManager logger dependency)
            services.AddLogging();

            // Add DbContext
            services.AddDbContext<UserDbContext>(options =>
                options.UseSqlServer(connectionString));

            // Add Identity (ApplicationUser + ApplicationRole)
            services.AddIdentity<ApplicationUser, ApplicationRole>()
                    .AddEntityFrameworkStores<UserDbContext>()
                    .AddDefaultTokenProviders();

            // Add configuration
            services.AddSingleton<IConfiguration>(configuration);

            // Build provider
            ServiceProvider = services.BuildServiceProvider();

            // 🛠️ STEP 4: Apply migrations automatically
            DbContext = ServiceProvider.GetRequiredService<UserDbContext>();
            Console.WriteLine("🚀 Applying migrations...");
            DbContext.Database.Migrate();
            Console.WriteLine("✅ Migrations applied successfully, DB ready for integration tests.");
        }

        public void Dispose()
        {
            DbContext?.Dispose();
        }
    }
}
