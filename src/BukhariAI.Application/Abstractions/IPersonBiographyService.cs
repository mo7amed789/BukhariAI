using BukhariAI.Application.Lessons.Biography;

namespace BukhariAI.Application.Abstractions;

public interface IPersonBiographyService
{
    Task<PersonBiographyDto> GetPersonBiographyAsync(
        GetPersonBiographyRequest request,
        CancellationToken cancellationToken = default);
}
