using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Abstractions;

/// <summary>
/// Service coordinating assessment creation, student answer submission, AI evaluation, and mastery updates.
/// </summary>
public interface IAssessmentService
{
    Task<SubmitStudentAnswerResponse> SubmitAnswerAsync(
        SubmitStudentAnswerRequest request,
        CancellationToken cancellationToken = default);

    Task<SubmitStudentAnswerResponse> SubmitAnswerAsync(
        SubmitStudentAnswerRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<AssessmentDetailDto?> GetAssessmentByIdAsync(
        Guid assessmentId,
        CancellationToken cancellationToken = default);

    Task<AssessmentDetailDto?> GetAssessmentByIdAsync(
        Guid assessmentId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<List<AssessmentDetailDto>> GetAssessmentsByLessonIdAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default);

    Task<List<AssessmentDetailDto>> GetAssessmentsByLessonIdAsync(
        Guid lessonId,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMasteryDto>> GetMasteryProfileByBookIdAsync(
        Guid bookId,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMasteryDto>> GetMasteryProfileByBookIdAsync(
        Guid bookId,
        Guid? userId,
        CancellationToken cancellationToken = default);
}
