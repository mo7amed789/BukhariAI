using BukhariAI.Application.Abstractions;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Auth;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BukhariAI.UnitTests;

public class AuthServiceTests
{
    private BukhariDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new BukhariDbContext(options);
    }

    private IConfiguration CreateConfiguration()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Jwt:SecretKey"] = "ThisIsAStrongSecurityKeyForUnitTestingPurposesOnly32BytesLong!",
            ["Jwt:Issuer"] = "BukhariAI_Test",
            ["Jwt:Audience"] = "BukhariAIApp_Test"
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(inMemory)
            .Build();
    }

    [Fact]
    public void PasswordHasher_HashesAndVerifiesCorrectly()
    {
        var hasher = new PasswordHasher();
        string password = "StrongStudentPassword123!";

        string hash = hasher.HashPassword(password);
        Assert.NotNull(hash);
        Assert.NotEqual(password, hash);

        bool isValid = hasher.VerifyPassword(password, hash);
        Assert.True(isValid);

        bool isWrong = hasher.VerifyPassword("WrongPassword", hash);
        Assert.False(isWrong);
    }

    [Fact]
    public async Task RegisterAsync_ValidRequest_CreatesUserAndReturnsToken()
    {
        using var db = CreateDbContext("RegisterAsync_Valid");
        var config = CreateConfiguration();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(config);
        var authService = new AuthService(db, hasher, jwt, NullLogger<AuthService>.Instance);

        var request = new RegisterRequest("ahmed_talib", "ahmed@example.com", "SecretP@ssword1", UserRole.Student);
        var result = await authService.RegisterAsync(request);

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.NotNull(result.User);
        Assert.Equal("ahmed_talib", result.User.Username);
        Assert.Equal("ahmed@example.com", result.User.Email);
        Assert.Equal(UserRole.Student, result.User.Role);

        // Verify stored in DB
        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Id == result.User.Id);
        Assert.NotNull(userInDb);
        Assert.True(hasher.VerifyPassword("SecretP@ssword1", userInDb.PasswordHash));
    }

    [Fact]
    public async Task RegisterAsync_DuplicateUsernameOrEmail_ReturnsError()
    {
        using var db = CreateDbContext("RegisterAsync_Duplicate");
        var config = CreateConfiguration();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(config);
        var authService = new AuthService(db, hasher, jwt, NullLogger<AuthService>.Instance);

        var first = await authService.RegisterAsync(new RegisterRequest("omar_talib", "omar@example.com", "Password123!"));
        Assert.True(first.Success);

        // Duplicate username
        var dupUser = await authService.RegisterAsync(new RegisterRequest("omar_talib", "other@example.com", "Password123!"));
        Assert.False(dupUser.Success);
        Assert.Contains("مسجل مسبقاً", dupUser.Error);

        // Duplicate email
        var dupEmail = await authService.RegisterAsync(new RegisterRequest("other_user", "omar@example.com", "Password123!"));
        Assert.False(dupEmail.Success);
        Assert.Contains("مسجل مسبقاً", dupEmail.Error);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsToken()
    {
        using var db = CreateDbContext("LoginAsync_Valid");
        var config = CreateConfiguration();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(config);
        var authService = new AuthService(db, hasher, jwt, NullLogger<AuthService>.Instance);

        await authService.RegisterAsync(new RegisterRequest("fatima_taliba", "fatima@example.com", "MyP@ssword456", UserRole.Student));

        var loginResult = await authService.LoginAsync(new LoginRequest("fatima_taliba", "MyP@ssword456"));
        Assert.True(loginResult.Success);
        Assert.NotNull(loginResult.Token);
        Assert.Equal("fatima_taliba", loginResult.User!.Username);

        // Can also login by email
        var loginByEmail = await authService.LoginAsync(new LoginRequest("fatima@example.com", "MyP@ssword456"));
        Assert.True(loginByEmail.Success);
        Assert.NotNull(loginByEmail.Token);
    }

    [Fact]
    public async Task LoginAsync_InvalidPassword_ReturnsUnauthorized()
    {
        using var db = CreateDbContext("LoginAsync_Invalid");
        var config = CreateConfiguration();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(config);
        var authService = new AuthService(db, hasher, jwt, NullLogger<AuthService>.Instance);

        await authService.RegisterAsync(new RegisterRequest("zayd_user", "zayd@example.com", "CorrectPassword123"));

        var result = await authService.LoginAsync(new LoginRequest("zayd_user", "WrongPassword456"));
        Assert.False(result.Success);
        Assert.Null(result.Token);
        Assert.Contains("غير صحيحة", result.Error);
    }

    [Fact]
    public async Task ForgotPassword_ValidUser_Generates6DigitCode()
    {
        using var db = CreateDbContext("ForgotPassword_Valid");
        var config = CreateConfiguration();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(config);
        var authService = new AuthService(db, hasher, jwt, NullLogger<AuthService>.Instance);

        await authService.RegisterAsync(new RegisterRequest("khalid", "khalid@example.com", "Password123!"));

        var forgotResult = await authService.ForgotPasswordAsync(new ForgotPasswordRequest("khalid"));
        Assert.True(forgotResult.Success);
        Assert.NotNull(forgotResult.ResetCode);
        Assert.Equal(6, forgotResult.ResetCode.Length);

        // Verify stored in DB with future expiry
        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Username == "khalid");
        Assert.NotNull(userInDb);
        Assert.Equal(forgotResult.ResetCode, userInDb.PasswordResetToken);
        Assert.NotNull(userInDb.PasswordResetExpiresUtc);
        Assert.True(userInDb.PasswordResetExpiresUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task ResetPassword_WithValidCode_ChangesPasswordSuccessfully()
    {
        using var db = CreateDbContext("ResetPassword_Valid");
        var config = CreateConfiguration();
        var hasher = new PasswordHasher();
        var jwt = new JwtTokenService(config);
        var authService = new AuthService(db, hasher, jwt, NullLogger<AuthService>.Instance);

        await authService.RegisterAsync(new RegisterRequest("sarah", "sarah@example.com", "OldPassword123!"));

        var forgotResult = await authService.ForgotPasswordAsync(new ForgotPasswordRequest("sarah@example.com"));
        Assert.True(forgotResult.Success);
        string code = forgotResult.ResetCode!;

        var resetResult = await authService.ResetPasswordAsync(new ResetPasswordRequest("sarah", code, "NewBrandPassword999!"));
        Assert.True(resetResult.Success);

        // Old password no longer works
        var oldLogin = await authService.LoginAsync(new LoginRequest("sarah", "OldPassword123!"));
        Assert.False(oldLogin.Success);

        // New password works
        var newLogin = await authService.LoginAsync(new LoginRequest("sarah", "NewBrandPassword999!"));
        Assert.True(newLogin.Success);
        Assert.NotNull(newLogin.Token);

        // Reset token cleared
        var userInDb = await db.Users.FirstOrDefaultAsync(u => u.Username == "sarah");
        Assert.Null(userInDb!.PasswordResetToken);
    }
}
