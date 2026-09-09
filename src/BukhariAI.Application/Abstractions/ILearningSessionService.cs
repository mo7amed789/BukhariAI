using BukhariAI.Application.LearningSessions;

namespace BukhariAI.Application.Abstractions;

public interface ILearningSessionService
{
    Task<LearningSessionNextDto> GetNextAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<LearningDashboardDto> GetDashboardAsync(Guid bookId, CancellationToken cancellationToken = default);
}
