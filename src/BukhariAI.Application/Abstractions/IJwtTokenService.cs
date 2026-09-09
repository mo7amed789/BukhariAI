using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Abstractions;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
