using BukhariAI.Application.Abstractions;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Infrastructure.Auth;

public sealed class AuthService : IAuthService
{
    private readonly BukhariDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        BukhariDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        ILogger<AuthService> logger)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.Password))
        {
            return new AuthResult(false, null, "اسم المستخدم/البريد وكلمة المرور مطلوبة.", null);
        }

        string identifier = request.UsernameOrEmail.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == identifier || u.Email.ToLower() == identifier, cancellationToken);

        if (user == null || !user.IsActive)
        {
            return new AuthResult(false, null, "بيانات الاعتماد غير صحيحة.", null);
        }

        if (!_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            return new AuthResult(false, null, "بيانات الاعتماد غير صحيحة.", null);
        }

        string token = _jwtTokenService.GenerateToken(user);
        var userDto = new UserDto(user.Id, user.Username, user.Email, user.Role);

        _logger.LogInformation("User '{Username}' (Id: {UserId}) logged in successfully.", user.Username, user.Id);
        return new AuthResult(true, token, null, userDto);
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length < 3)
        {
            return new AuthResult(false, null, "اسم المستخدم يجب ألا يقل عن 3 أحرف.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains('@'))
        {
            return new AuthResult(false, null, "يرجى تزويد بريد إلكتروني صالح.", null);
        }

        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
        {
            return new AuthResult(false, null, "كلمة المرور يجب ألا تقل عن 6 خانات.", null);
        }

        string normalizedUsername = request.Username.Trim();
        string normalizedEmail = request.Email.Trim().ToLowerInvariant();

        bool exists = await _dbContext.Users.AnyAsync(
            u => u.Username.ToLower() == normalizedUsername.ToLower() || u.Email.ToLower() == normalizedEmail,
            cancellationToken);

        if (exists)
        {
            return new AuthResult(false, null, "اسم المستخدم أو البريد الإلكتروني مسجل مسبقاً.", null);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = normalizedUsername,
            Email = normalizedEmail,
            PasswordHash = _passwordHasher.HashPassword(request.Password),
            Role = request.Role,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        string token = _jwtTokenService.GenerateToken(user);
        var userDto = new UserDto(user.Id, user.Username, user.Email, user.Role);

        _logger.LogInformation("New user '{Username}' registered successfully with role {Role}.", user.Username, user.Role);
        return new AuthResult(true, token, null, userDto);
    }

    public async Task<AuthResult> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail))
        {
            return new AuthResult(false, null, "اسم المستخدم أو البريد الإلكتروني مطلوب.", null);
        }

        string identifier = request.UsernameOrEmail.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == identifier || u.Email.ToLower() == identifier, cancellationToken);

        if (user == null || !user.IsActive)
        {
            return new AuthResult(false, null, "المستخدم غير موجود.", null);
        }

        string resetCode = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        user.PasswordResetToken = resetCode;
        user.PasswordResetExpiresUtc = DateTime.UtcNow.AddMinutes(15);

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password reset code generated for user '{Username}'.", user.Username);
        return new AuthResult(true, null, null, null, resetCode);
    }

    public async Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UsernameOrEmail) || string.IsNullOrWhiteSpace(request.ResetCode))
        {
            return new AuthResult(false, null, "اسم المستخدم ورمز التحقق مطلوبان.", null);
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return new AuthResult(false, null, "كلمة المرور الجديدة يجب ألا تقل عن 6 خانات.", null);
        }

        string identifier = request.UsernameOrEmail.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == identifier || u.Email.ToLower() == identifier, cancellationToken);

        if (user == null || !user.IsActive)
        {
            return new AuthResult(false, null, "المستخدم غير موجود.", null);
        }

        if (string.IsNullOrWhiteSpace(user.PasswordResetToken) ||
            user.PasswordResetToken != request.ResetCode.Trim() ||
            !user.PasswordResetExpiresUtc.HasValue ||
            user.PasswordResetExpiresUtc.Value < DateTime.UtcNow)
        {
            return new AuthResult(false, null, "رمز استعادة كلمة المرور غير صالح أو منتهي الصلاحية.", null);
        }

        user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword);
        user.PasswordResetToken = null;
        user.PasswordResetExpiresUtc = null;

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password successfully reset for user '{Username}'.", user.Username);
        return new AuthResult(true, null, null, null);
    }

    public async Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : new UserDto(user.Id, user.Username, user.Email, user.Role);
    }
}
