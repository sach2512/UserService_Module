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
            // 🔍 Step 1: Locate appsettings.json
            string baseDir = AppContext.BaseDirectory;
            string appSettingsPath = Path.Combine(baseDir, "appsettings.json");

            if (!File.Exists(appSettingsPath))
            {
                var dir = new DirectoryInfo(baseDir);
                while (dir != null && !File.Exists(Path.Combine(dir.FullName, "appsettings.json")))
                    dir = dir.Parent;

                if (dir == null)
                    throw new FileNotFoundException("❌ Could not locate appsettings.json in any parent directory.");

                appSettingsPath = Path.Combine(dir.FullName, "appsettings.json");
            }

            Console.WriteLine($"✅ Using appsettings.json from: {appSettingsPath}");

            // 🧩 Step 2: Load configuration
            var configuration = new ConfigurationBuilder()
                .AddJsonFile(appSettingsPath, optional: false, reloadOnChange: true)
                .Build();

            // 🧠 Step 3: Prefer environment variable (for GitHub Actions)
            var connectionString =
                Environment.GetEnvironmentVariable("ConnectionStrings__MyConnectionString") ??
                configuration.GetConnectionString("MyConnectionString") ??
                throw new InvalidOperationException("Missing database connection string.");

            Console.WriteLine($"✅ Connection string used: {connectionString}");

            // 🧱 Step 4: Setup dependency injection
            var services = new ServiceCollection();

            services.AddLogging();

            services.AddDbContext<UserDbContext>(options =>
                options.UseSqlServer(connectionString));

            services.AddIdentity<ApplicationUser, ApplicationRole>()
                    .AddEntityFrameworkStores<UserDbContext>()
                    .AddDefaultTokenProviders();

            services.AddSingleton<IConfiguration>(configuration);

            ServiceProvider = services.BuildServiceProvider();

            // ⚙️ Step 5: Apply migrations automatically
            DbContext = ServiceProvider.GetRequiredService<UserDbContext>();
            Console.WriteLine("🚀 Applying migrations...");
            DbContext.Database.Migrate();
            Console.WriteLine("✅ Database ready for integration tests.");
        }

        public void Dispose()
        {
            DbContext?.Dispose();
        }
    }
}
