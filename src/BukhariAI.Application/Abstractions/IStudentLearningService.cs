using BukhariAI.Application.Assessments;

namespace BukhariAI.Application.Abstractions;

/// <summary>Owns lesson-completion and evidence-backed review decisions.</summary>
public interface IStudentLearningService
{
    Task<LessonProgressDto?> GetLessonProgressAsync(Guid lessonId, CancellationToken cancellationToken = default);
    Task<LessonProgressDto?> GetLessonProgressAsync(Guid lessonId, Guid? userId, CancellationToken cancellationToken = default);

    Task<LessonProgressDto> UpdateLessonProgressAsync(Guid lessonId, UpdateLessonProgressRequest request, CancellationToken cancellationToken = default);
    Task<LessonProgressDto> UpdateLessonProgressAsync(Guid lessonId, UpdateLessonProgressRequest request, Guid? userId, CancellationToken cancellationToken = default);

    Task<List<StudentConceptMasteryDto>> GetWeakConceptsAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<List<StudentConceptMasteryDto>> GetWeakConceptsAsync(Guid bookId, Guid? userId, CancellationToken cancellationToken = default);

    Task<List<ReviewRecommendationDto>> GetReviewRecommendationsAsync(Guid bookId, CancellationToken cancellationToken = default);
    Task<List<ReviewRecommendationDto>> GetReviewRecommendationsAsync(Guid bookId, Guid? userId, CancellationToken cancellationToken = default);

    Task RefreshLessonProgressAfterAssessmentAsync(Guid lessonId, Guid assessmentId, CancellationToken cancellationToken = default);
    Task RefreshLessonProgressAfterAssessmentAsync(Guid lessonId, Guid assessmentId, Guid? userId, CancellationToken cancellationToken = default);
}
