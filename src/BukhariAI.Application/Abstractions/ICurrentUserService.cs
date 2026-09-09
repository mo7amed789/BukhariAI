using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Abstractions;

/// <summary>
/// Provides identity context of the currently executing user/student.
/// </summary>
public interface ICurrentUserService
{
    Guid UserId { get; }

    string? Username { get; }

    UserRole Role { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(UserRole role);
}
