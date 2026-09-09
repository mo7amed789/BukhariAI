using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Abstractions;

public record UserDto(Guid Id, string Username, string Email, UserRole Role);

public record AuthResult(bool Success, string? Token, string? Error, UserDto? User, string? ResetCode = null);

public record LoginRequest(string UsernameOrEmail, string Password);

public record RegisterRequest(string Username, string Email, string Password, UserRole Role = UserRole.Student);

public record ForgotPasswordRequest(string UsernameOrEmail);

public record ResetPasswordRequest(string UsernameOrEmail, string ResetCode, string NewPassword);

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<AuthResult> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default);

    Task<AuthResult> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default);

    Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
