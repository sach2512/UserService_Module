using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UserService.Application.DTOs;
using UserService.Domain.Entities;
using UserService.Infrastructure.Identity;
using UserService.Infrastructure.Persistence;
using UserService.Infrastructure.Repositories;

namespace UserServiceTests.test
{
    public class UserServiceIntegrationTests : IClassFixture<TestDatabaseFixture>
    {
        private readonly IConfiguration _config;
        private readonly UserRepository _repository;
        private readonly UserService.Application.Services.UserService _service;

        public UserServiceIntegrationTests(TestDatabaseFixture fixture)
        {
            // ✅ Reuse the DI container from the shared fixture
            var provider = fixture.ServiceProvider;

            _config = provider.GetRequiredService<IConfiguration>();

            var dbContext = provider.GetRequiredService<UserDbContext>();
            var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var signInManager = provider.GetRequiredService<SignInManager<ApplicationUser>>();

            // ✅ Ensure test user exists
            EnsureTestUserAsync(userManager, dbContext).GetAwaiter().GetResult();

            _repository = new UserRepository(userManager, signInManager, dbContext);
            _service = new UserService.Application.Services.UserService(_repository, _config);
        }

        // ✅ Helper method: creates a test user and address if missing
        private static async Task EnsureTestUserAsync(UserManager<ApplicationUser> userManager, UserDbContext dbContext)
        {
            // 🧹 Always reset for a clean test state
            var existingUser = await userManager.FindByEmailAsync("test@example.com");
            if (existingUser != null)
            {
                await userManager.DeleteAsync(existingUser);
                Console.WriteLine("🧹 Old test user deleted for clean setup.");
            }

            var user = new ApplicationUser
            {
                UserName = "testuser",
                Email = "test@example.com",
                FullName = "Integration Test User",
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user, "Password@123");
            if (!createResult.Succeeded)
            {
                throw new Exception("❌ Failed to create test user: " +
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
            }

            // ✅ Add valid address with all required fields
            await dbContext.Addresses.AddAsync(new Address
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                AddressLine1 = "123 Integration St",
                City = "Testville",
                State = "TestState",          // ✅ Required (fixes NULL 'State' error)
                Country = "Testland",
                PostalCode = "12345",
                IsDefaultBilling = true,
                IsDefaultShipping = true
            });

            await dbContext.SaveChangesAsync();
            Console.WriteLine("✅ Test user and address created successfully.");
        }


        // ✅ TEST 1: Login
        [Fact(DisplayName = "Login should return valid JWT and refresh token")]
        public async Task LoginAsync_ShouldReturnToken_WhenCredentialsAreValid()
        {
            var dto = new LoginDTO
            {
                EmailOrUserName = "test@example.com",
                Password = "Password@123",
                ClientId = "web"
            };

            var result = await _service.LoginAsync(dto, "127.0.0.1", "Mozilla/5.0");

            result.Should().NotBeNull();
            result.ErrorMessage.Should().BeNull();
            result.Token.Should().NotBeNullOrEmpty();
            result.RefreshToken.Should().NotBeNullOrEmpty();

            Console.WriteLine("✅ JWT Token: " + result.Token);
            Console.WriteLine("✅ Refresh Token: " + result.RefreshToken);
        }

        // ✅ TEST 2: Get Profile
        [Fact(DisplayName = "GetProfileAsync should return test user's profile")]
        public async Task GetProfileAsync_ShouldReturnProfile_WhenUserExists()
        {
            var userId = await GetTestUserIdAsync();

            var profile = await _service.GetProfileAsync(userId);

            profile.Should().NotBeNull();
            profile.Email.Should().Be("test@example.com");
            profile.UserName.Should().Be("testuser");
            profile.FullName.Should().Be("Integration Test User");

            Console.WriteLine($"✅ Profile retrieved for {profile.FullName} ({profile.Email})");
        }

        // ✅ TEST 3: Get Addresses
        [Fact(DisplayName = "GetAddressesAsync should return all addresses for test user")]
        public async Task GetAddressesAsync_ShouldReturnAddresses()
        {
            var userId = await GetTestUserIdAsync();

            var addresses = (await _service.GetAddressesAsync(userId)).ToList();

            addresses.Should().NotBeEmpty();
            addresses.Should().Contain(a =>
                a.AddressLine1.Contains("123 Integration St") || a.AddressLine1.Contains("123 Test Street"));

            Console.WriteLine("✅ Addresses found:");
            foreach (var addr in addresses)
                Console.WriteLine($" - {addr.AddressLine1}, {addr.City}, {addr.Country}");
        }

        // ✅ TEST 4: Refresh Token
        [Fact(DisplayName = "RefreshTokenAsync should issue new JWT and refresh token")]
        public async Task RefreshTokenAsync_ShouldReturnNewTokens_WhenTokenIsValid()
        {
            // 1️⃣ First log in to get a valid refresh token
            var loginDto = new LoginDTO
            {
                EmailOrUserName = "test@example.com",
                Password = "Password@123",
                ClientId = "web"
            };

            var loginResult = await _service.LoginAsync(loginDto, "127.0.0.1", "Mozilla/5.0");
            loginResult.Should().NotBeNull();
            loginResult.ErrorMessage.Should().BeNull("Login should succeed");
            loginResult.RefreshToken.Should().NotBeNullOrEmpty("Login should return a refresh token");

            // 2️⃣ Use the real refresh token returned by login
            var refreshDto = new RefreshTokenRequestDTO
            {
                ClientId = "web",
                RefreshToken = loginResult.RefreshToken!
            };

            var result = await _service.RefreshTokenAsync(refreshDto, "127.0.0.1", "Mozilla/5.0");

            // 3️⃣ Validate the new tokens
            result.Should().NotBeNull();
            result.ErrorMessage.Should().BeNull("Valid refresh token should produce new tokens");
            result.Token.Should().NotBeNullOrEmpty("New JWT should be issued");
            result.RefreshToken.Should().NotBeNullOrEmpty("New refresh token should be issued");

            Console.WriteLine("✅ New JWT: " + result.Token);
            Console.WriteLine("✅ New RefreshToken: " + result.RefreshToken);
        }


        // ✅ Helper to fetch test user's ID
        private async Task<Guid> GetTestUserIdAsync()
        {
            var user = await _repository.FindByEmailAsync("test@example.com");
            if (user == null)
                throw new Exception("❌ Test user not found. Ensure EnsureTestUserAsync() ran correctly.");
            return user.Id;
        }
    }
}
