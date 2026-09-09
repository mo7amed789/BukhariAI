using BukhariAI.Application.AdaptiveLearning;
using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Abstractions;

public interface IAdaptiveLearningService
{
    Task<AdaptiveLearningStateDto> GetAdaptiveStateAsync(Guid bookId, LearningIntent intent = LearningIntent.ContinueLearning, CancellationToken cancellationToken = default);
    Task<List<AdaptiveConceptState>> GetDueReviewsAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<ConceptReinforcementSessionDto> GenerateConceptReinforcementAsync(Guid bookId, GenerateReinforcementRequest request, CancellationToken cancellationToken = default);
    Task<ReinforcementEvaluationResultDto> EvaluateReinforcementAnswerAsync(Guid bookId, SubmitReinforcementAnswerRequest request, CancellationToken cancellationToken = default);
    Task<List<ReviewFlashcardDto>> GetReviewDeckAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<FlashcardRatingResultDto> RateFlashcardAsync(Guid bookId, FlashcardRatingRequest request, CancellationToken cancellationToken = default);
    Task<List<MasteryMatrixItemDto>> GetMasteryMatrixAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<QuickReviewQuizDto> GenerateReviewQuizAsync(Guid bookId, int count = 3, CancellationToken cancellationToken = default);
}
