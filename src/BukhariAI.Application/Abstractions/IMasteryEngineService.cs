using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Abstractions;

/// <summary>
/// Manages and calculates StudentConceptMastery based on demonstrated assessment results and exposure history.
/// Rules:
/// - One correct answer does not produce Mastered.
/// - Repeated demonstrated understanding increases mastery score and advances learning level.
/// - Incorrect answers reduce confidence without deleting historical knowledge.
/// - Distinguishes exposure from demonstrated understanding.
/// </summary>
public interface IMasteryEngineService
{
    Task<List<StudentConceptMastery>> UpdateMasteryAfterAssessmentAsync(
        Guid bookId,
        Guid? lessonId,
        List<string> expectedConcepts,
        AssessmentEvaluationResult evaluationResult,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMastery>> UpdateMasteryAfterAssessmentAsync(
        Guid bookId,
        Guid? lessonId,
        List<string> expectedConcepts,
        AssessmentEvaluationResult evaluationResult,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMastery>> RecordConceptExposuresAsync(
        Guid bookId,
        Guid lessonId,
        IEnumerable<string> conceptKeys,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMastery>> RecordConceptExposuresAsync(
        Guid bookId,
        Guid lessonId,
        IEnumerable<string> conceptKeys,
        Guid? userId,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMastery>> GetStudentMasteryByBookIdAsync(
        Guid bookId,
        CancellationToken cancellationToken = default);

    Task<List<StudentConceptMastery>> GetStudentMasteryByBookIdAsync(
        Guid bookId,
        Guid? userId,
        CancellationToken cancellationToken = default);
}
