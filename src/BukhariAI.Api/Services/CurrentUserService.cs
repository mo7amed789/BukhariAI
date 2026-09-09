using System.Security.Claims;
using BukhariAI.Application.Abstractions;
using BukhariAI.Domain.Entities;

namespace BukhariAI.Api.Services;

public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? UserPrincipal => _httpContextAccessor.HttpContext?.User;

    public Guid UserId
    {
        get
        {
            var principal = UserPrincipal;
            if (principal?.Identity?.IsAuthenticated == true)
            {
                var idClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                    ?? principal.FindFirst("sub")?.Value;

                if (Guid.TryParse(idClaim, out var parsedGuid))
                {
                    return parsedGuid;
                }
            }

            return User.DefaultUserId;
        }
    }

    public string? Username => UserPrincipal?.Identity?.Name;

    public UserRole Role
    {
        get
        {
            var roleClaim = UserPrincipal?.FindFirst(ClaimTypes.Role)?.Value;
            if (Enum.TryParse<UserRole>(roleClaim, ignoreCase: true, out var role))
            {
                return role;
            }

            return UserRole.Student;
        }
    }

    public bool IsAuthenticated => UserPrincipal?.Identity?.IsAuthenticated ?? false;

    public bool IsInRole(UserRole role)
    {
        return UserPrincipal?.IsInRole(role.ToString()) ?? false;
    }
}
