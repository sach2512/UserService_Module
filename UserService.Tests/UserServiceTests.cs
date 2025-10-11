using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Moq;
using UserService.Application.DTOs;
using UserService.Application.Services;
using UserService.Domain.Entities;
using UserService.Domain.Repositories;
using Xunit;

namespace UserService.Tests
{
    public class UserServiceTests
    {
        private readonly Mock<IUserRepository> _userRepositoryMock;
        private readonly Mock<IConfiguration> _configurationMock;
        private readonly UserService.Application.Services.UserService _userService;

        public UserServiceTests()
        {
            _userRepositoryMock = new Mock<IUserRepository>();
            _configurationMock = new Mock<IConfiguration>();

            var settings = new Dictionary<string, string>
    {
        {"JwtSettings:SecretKey", "SuperSecretKeyForJwtTests_1234567890!@#"}, // >= 32 chars
        {"JwtSettings:Issuer", "TestIssuer"},
        {"JwtSettings:AccessTokenExpirationMinutes", "60"}
    };

            // ✅ Fix: Cast to nullable string to satisfy .NET 8 AddInMemoryCollection
           
            var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(kvp =>
             new KeyValuePair<string, string?>(kvp.Key, kvp.Value)))
            .Build();


            _configurationMock.Setup(x => x[It.IsAny<string>()])
                .Returns((string key) => config[key]);

            _userService = new UserService.Application.Services.UserService(
                _userRepositoryMock.Object, _configurationMock.Object);
        }


        // ================================
        // RegisterAsync
        // ================================
        [Fact]
        public async Task RegisterAsync_ShouldReturnTrue_WhenRegistrationSuccessful()
        {
            var dto = new RegisterDTO
            {
                Email = "user@example.com",
                UserName = "user1",
                Password = "Pass123!",
                PhoneNumber = "1234567890",
                FullName = "User One"
            };

            _userRepositoryMock.Setup(r => r.FindByEmailAsync(dto.Email)).ReturnsAsync((User?)null);
            _userRepositoryMock.Setup(r => r.FindByUserNameAsync(dto.UserName)).ReturnsAsync((User?)null);
            _userRepositoryMock.Setup(r => r.CreateUserAsync(It.IsAny<User>(), dto.Password)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.AddUserToRoleAsync(It.IsAny<User>(), "Customer"))
                .Returns(Task.FromResult(true));

            var result = await _userService.RegisterAsync(dto);

            Assert.True(result);
        }

        [Fact]
        public async Task RegisterAsync_ShouldReturnFalse_WhenEmailExists()
        {
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(new User());
            var result = await _userService.RegisterAsync(new RegisterDTO());
            Assert.False(result);
        }

        // ================================
        // LoginAsync
        // ================================
        [Fact]
        public async Task LoginAsync_ShouldReturnToken_WhenValidCredentials()
        {
            var dto = new LoginDTO
            {
                ClientId = "client1",
                EmailOrUserName = "user@example.com",
                Password = "Pass123!"
            };

            var user = new User
            {
                Id = Guid.NewGuid(),
                Email = dto.EmailOrUserName,
                UserName = "user1",
                IsEmailConfirmed = true
            };

            _userRepositoryMock.Setup(r => r.IsValidClientAsync(dto.ClientId)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(dto.EmailOrUserName)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.CheckPasswordAsync(user, dto.Password)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.GetUserRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });
            _userRepositoryMock.Setup(r => r.GenerateAndStoreRefreshTokenAsync(user.Id, dto.ClientId, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("new_refresh_token");

            var result = await _userService.LoginAsync(dto, "127.0.0.1", "agent");

            Assert.NotNull(result.Token);
            Assert.Equal("new_refresh_token", result.RefreshToken);
        }

        [Fact]
        public async Task LoginAsync_ShouldReturnError_WhenInvalidClient()
        {
            var dto = new LoginDTO { ClientId = "wrong", EmailOrUserName = "test", Password = "123" };
            _userRepositoryMock.Setup(r => r.IsValidClientAsync(dto.ClientId)).ReturnsAsync(false);

            var result = await _userService.LoginAsync(dto, "", "");
            Assert.Equal("Invalid client ID.", result.ErrorMessage);
        }

        // ================================
        // SendConfirmationEmailAsync
        // ================================
        [Fact]
        public async Task SendConfirmationEmailAsync_ShouldReturnToken_WhenUserExists()
        {
            var user = new User { Id = Guid.NewGuid(), Email = "user@example.com" };
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.GenerateEmailConfirmationTokenAsync(user))
                .ReturnsAsync("email_token");

            var result = await _userService.SendConfirmationEmailAsync(user.Email);

            Assert.Equal("email_token", result.Token);
        }

        // ================================
        // VerifyConfirmationEmailAsync
        // ================================
        [Fact]
        public async Task VerifyConfirmationEmailAsync_ShouldReturnTrue_WhenValidToken()
        {
            var user = new User { Id = Guid.NewGuid() };
            _userRepositoryMock.Setup(r => r.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.VerifyConfirmaionEmailAsync(user, "token")).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.UpdateUserAsync(user)).ReturnsAsync(true);

            var result = await _userService.VerifyConfirmationEmailAsync(new ConfirmEmailDTO { UserId = user.Id, Token = "token" });

            Assert.True(result);
        }

        // ================================
        // RefreshTokenAsync
        // ================================
        [Fact]
        public async Task RefreshTokenAsync_ShouldReturnNewTokens_WhenValid()
        {
            var dto = new RefreshTokenRequestDTO { ClientId = "client", RefreshToken = "old" };
            var user = new User { Id = Guid.NewGuid(), Email = "user@example.com" };

            var refresh = new RefreshToken
            {
                UserId = user.Id,
                RevokedAt = null,
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            };

            _userRepositoryMock.Setup(r => r.IsValidClientAsync(dto.ClientId)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.GetRefreshTokenAsync(dto.RefreshToken)).ReturnsAsync(refresh);
            _userRepositoryMock.Setup(r => r.GenerateAndStoreRefreshTokenAsync(user.Id, dto.ClientId, It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync("new_token");
            _userRepositoryMock.Setup(r => r.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.GetUserRolesAsync(user)).ReturnsAsync(new List<string> { "Customer" });

            var result = await _userService.RefreshTokenAsync(dto, "", "");

            Assert.NotNull(result.Token);
            Assert.Equal("new_token", result.RefreshToken);
        }

        // ================================
        // RevokeRefreshTokenAsync
        // ================================
        [Fact]
        public async Task RevokeRefreshTokenAsync_ShouldReturnTrue_WhenTokenValid()
        {
            var token = new RefreshToken
            {
                RevokedAt = null,
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            };

            _userRepositoryMock.Setup(r => r.GetRefreshTokenAsync("abc")).ReturnsAsync(token);
            _userRepositoryMock.Setup(r => r.RevokeRefreshTokenAsync(token, It.IsAny<string>())).Returns(Task.CompletedTask);

            var result = await _userService.RevokeRefreshTokenAsync("abc", "ip");

            Assert.True(result);
        }

        // ================================
        // ForgotPasswordAsync
        // ================================
        [Fact]
        public async Task ForgotPasswordAsync_ShouldReturnToken_WhenUserExists()
        {
            var user = new User { Id = Guid.NewGuid(), Email = "a@a.com" };
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(user.Email)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.GeneratePasswordResetTokenAsync(user)).ReturnsAsync("reset_token");

            var result = await _userService.ForgotPasswordAsync(user.Email);

            Assert.Equal("reset_token", result.Token);
        }

        [Fact]
        public async Task ForgotPasswordAsync_ShouldReturnNull_WhenUserNotFound()
        {
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((User?)null);

            var result = await _userService.ForgotPasswordAsync("missing@example.com");

            Assert.Null(result);
        }

        // ================================
        // ChangePasswordAsync
        // ================================
        [Fact]
        public async Task ChangePasswordAsync_ShouldReturnTrue_WhenPasswordChanged()
        {
            var user = new User { Id = Guid.NewGuid() };
            _userRepositoryMock.Setup(r => r.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.ChangePasswordAsync(user, "old", "new")).ReturnsAsync(true);

            var result = await _userService.ChangePasswordAsync(user.Id, "old", "new");

            Assert.True(result);
        }

        // ================================
        // ResetPasswordAsync
        // ================================
        [Fact]
        public async Task ResetPasswordAsync_ShouldReturnTrue_WhenValidToken()
        {
            var user = new User { Id = Guid.NewGuid() };
            _userRepositoryMock.Setup(r => r.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.ResetPasswordAsync(user, "token", "new")).ReturnsAsync(true);

            var result = await _userService.ResetPasswordAsync(user.Id, "token", "new");

            Assert.True(result);
        }

        // ================================
        // GetProfileAsync
        // ================================
        [Fact]
        public async Task GetProfileAsync_ShouldReturnProfile_WhenUserExists()
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = "User",
                Email = "u@u.com",
                UserName = "u1"
            };

            _userRepositoryMock.Setup(r => r.FindByIdAsync(user.Id)).ReturnsAsync(user);

            var result = await _userService.GetProfileAsync(user.Id);

            Assert.Equal(user.Email, result.Email);
        }

        // ================================
        // UpdateProfileAsync
        // ================================
        [Fact]
        public async Task UpdateProfileAsync_ShouldReturnTrue_WhenUpdated()
        {
            var user = new User { Id = Guid.NewGuid() };
            var dto = new UpdateProfileDTO { UserId = user.Id, FullName = "New", PhoneNumber = "123" };

            _userRepositoryMock.Setup(r => r.FindByIdAsync(user.Id)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.UpdateUserAsync(user)).ReturnsAsync(true);

            var result = await _userService.UpdateProfileAsync(dto);

            Assert.True(result);
        }

        // ================================
        // GetAddressesAsync
        // ================================
        [Fact]
        public async Task GetAddressesAsync_ShouldReturnAddresses()
        {
            var userId = Guid.NewGuid();
            var addresses = new List<Address>
            {
                new Address { Id = Guid.NewGuid(), City = "NY", Country = "USA" }
            };

            _userRepositoryMock.Setup(r => r.GetAddressesByUserIdAsync(userId)).ReturnsAsync(addresses);

            var result = await _userService.GetAddressesAsync(userId);

            Assert.Single(result);
        }

        // ================================
        // AddOrUpdateAddressAsync
        // ================================
        [Fact]
        public async Task AddOrUpdateAddressAsync_ShouldReturnAddressId()
        {
            var dto = new AddressDTO { userId = Guid.NewGuid(), City = "Paris", Country = "FR" };
            _userRepositoryMock.Setup(r => r.AddOrUpdateAddressAsync(It.IsAny<Address>()))
                .ReturnsAsync(Guid.NewGuid());

            var result = await _userService.AddOrUpdateAddressAsync(dto);

            Assert.NotEqual(Guid.Empty, result);
        }

        // ================================
        // DeleteAddressAsync
        // ================================
        [Fact]
        public async Task DeleteAddressAsync_ShouldReturnTrue_WhenDeleted()
        {
            _userRepositoryMock.Setup(r => r.DeleteAddressAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync(true);

            var result = await _userService.DeleteAddressAsync(Guid.NewGuid(), Guid.NewGuid());

            Assert.True(result);
        }

        // ================================
        // IsUserExistsAsync
        // ================================
        [Fact]
        public async Task IsUserExistsAsync_ShouldReturnTrue_WhenExists()
        {
            _userRepositoryMock.Setup(r => r.IsUserExistsAsync(It.IsAny<Guid>())).ReturnsAsync(true);

            var result = await _userService.IsUserExistsAsync(Guid.NewGuid());

            Assert.True(result);
        }

        // ================================
        // GetAddressByUserIdAndAddressIdAsync
        // ================================
        [Fact]
        public async Task GetAddressByUserIdAndAddressIdAsync_ShouldReturnAddress_WhenExists()
        {
            var address = new Address { Id = Guid.NewGuid(), City = "Berlin" };
            _userRepositoryMock.Setup(r => r.GetAddressByUserIdAndAddressIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>()))
                .ReturnsAsync(address);

            var result = await _userService.GetAddressByUserIdAndAddressIdAsync(Guid.NewGuid(), address.Id);

            Assert.Equal("Berlin", result.City);
        }

        [Fact]
        public async Task RegisterAsync_ShouldReturnFalse_WhenUsernameExists()
        {
            var dto = new RegisterDTO
            {
                Email = "unique@example.com",
                UserName = "duplicateUser",
                Password = "Pass123!"
            };

            _userRepositoryMock.Setup(r => r.FindByEmailAsync(dto.Email))
                .ReturnsAsync((User?)null);
            _userRepositoryMock.Setup(r => r.FindByUserNameAsync(dto.UserName))
                .ReturnsAsync(new User { UserName = dto.UserName });

            var result = await _userService.RegisterAsync(dto);

            Assert.False(result);
        }

        [Fact]
        public async Task LoginAsync_ShouldReturnError_WhenEmailNotConfirmed()
        {
            var dto = new LoginDTO
            {
                ClientId = "client1",
                EmailOrUserName = "user@example.com",
                Password = "Password123!"
            };

            var user = new User { Id = Guid.NewGuid(), Email = dto.EmailOrUserName, IsEmailConfirmed = false };

            _userRepositoryMock.Setup(r => r.IsValidClientAsync(dto.ClientId)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(dto.EmailOrUserName)).ReturnsAsync(user);

            var result = await _userService.LoginAsync(dto, "127.0.0.1", "Mozilla");

            Assert.Equal("Email not confirmed. Please verify your email.", result.ErrorMessage);
        }

        [Fact]
        public async Task LoginAsync_ShouldReturnError_WhenAccountLockedOut()
        {
            var dto = new LoginDTO
            {
                ClientId = "client1",
                EmailOrUserName = "locked@example.com",
                Password = "WrongPass"
            };

            var user = new User { Id = Guid.NewGuid(), Email = dto.EmailOrUserName, IsEmailConfirmed = true };

            _userRepositoryMock.Setup(r => r.IsValidClientAsync(dto.ClientId)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.FindByEmailAsync(dto.EmailOrUserName)).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.IsLockedOutAsync(user)).ReturnsAsync(true);
            _userRepositoryMock.Setup(r => r.GetLockoutEndDateAsync(user))
                .ReturnsAsync(DateTime.UtcNow.AddMinutes(5));

            var result = await _userService.LoginAsync(dto, "127.0.0.1", "Mozilla");

            Assert.Contains("Account is locked", result.ErrorMessage);
        }

        [Fact]
        public async Task RefreshTokenAsync_ShouldReturnError_WhenClientInvalid()
        {
            var dto = new RefreshTokenRequestDTO { ClientId = "badClient", RefreshToken = "abc" };

            _userRepositoryMock.Setup(r => r.IsValidClientAsync(dto.ClientId)).ReturnsAsync(false);

            var result = await _userService.RefreshTokenAsync(dto, "", "");

            Assert.Equal("Invalid client ID.", result.ErrorMessage);
        }

        [Fact]
        public async Task VerifyConfirmationEmailAsync_ShouldReturnFalse_WhenUserNotFound()
        {
            _userRepositoryMock.Setup(r => r.FindByIdAsync(It.IsAny<Guid>())).ReturnsAsync((User?)null);

            var dto = new ConfirmEmailDTO { UserId = Guid.NewGuid(), Token = "xyz" };

            var result = await _userService.VerifyConfirmationEmailAsync(dto);

            Assert.False(result);
        }
    }
}
