using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.Assessments;

public sealed class AssessmentEvaluationInput
{
    public string OriginalSourceText { get; init; } = string.Empty;

    public string LessonExplanation { get; init; } = string.Empty;

    public string Question { get; init; } = string.Empty;

    public string QuestionType { get; init; } = string.Empty;

    public string Difficulty { get; init; } = string.Empty;

    public List<string> ExpectedConcepts { get; init; } = [];

    public string EvaluationGuidance { get; init; } = string.Empty;

    public string StudentAnswer { get; init; } = string.Empty;

    public List<StudentConceptMasteryItem> RelevantStudentMastery { get; init; } = [];
}

public sealed class AssessmentEvaluationResult
{
    public double Score { get; init; }

    public AssessmentAnswerStatus AnswerStatus { get; init; } = AssessmentAnswerStatus.InsufficientEvidence;

    public LearningLevel Level { get; init; } = LearningLevel.Unknown;

    public List<string> UnderstoodConcepts { get; init; } = [];

    public List<string> MissingConcepts { get; init; } = [];

    public List<string> Misconceptions { get; init; } = [];

    public string Feedback { get; init; } = string.Empty;
}

public sealed class SubmitStudentAnswerRequest
{
    public Guid QuestionId { get; init; }

    public string StudentAnswer { get; init; } = string.Empty;

    /// <summary>Optional client-generated idempotency key (one submission per question).</summary>
    public string? SubmissionId { get; init; }
}

public sealed class SubmitStudentAnswerResponse
{
    public Guid AnswerId { get; init; }

    public Guid ResultId { get; init; }

    public double Score { get; init; }

    public AssessmentAnswerStatus AnswerStatus { get; init; }

    public LearningLevel Level { get; init; }

    public List<string> UnderstoodConcepts { get; init; } = [];

    public List<string> MissingConcepts { get; init; } = [];

    public List<string> Misconceptions { get; init; } = [];

    public string Feedback { get; init; } = string.Empty;

    public List<StudentConceptMasteryDto> UpdatedMasteries { get; init; } = [];

    public DateTime EvaluatedAtUtc { get; init; }
}

public sealed class StudentConceptMasteryDto
{
    public Guid Id { get; init; }

    public Guid BookId { get; init; }

    public string ConceptKey { get; init; } = string.Empty;

    public LearningLevel LearningLevel { get; init; }

    public int ExposureCount { get; init; }

    public int AssessmentCount { get; init; }

    public int CorrectAnswerCount { get; init; }

    public int DemonstratedContextCount { get; init; }

    public double MasteryScore { get; init; }

    public DateTime? LastAssessedAt { get; init; }

    public Guid? FirstIntroducedLessonId { get; init; }

    public Guid? LastAssessedLessonId { get; init; }
}

public sealed class AssessmentDetailDto
{
    public Guid Id { get; init; }

    public Guid BookId { get; init; }

    public Guid? LessonId { get; init; }

    public string Title { get; init; } = string.Empty;

    public DateTime CreatedAtUtc { get; init; }

    public List<AssessmentQuestionDetailDto> Questions { get; init; } = [];
}

public sealed class AssessmentQuestionDetailDto
{
    public Guid Id { get; init; }

    public Guid AssessmentId { get; init; }

    public Guid? SourceLessonId { get; init; }

    public string Question { get; init; } = string.Empty;

    public string QuestionType { get; init; } = string.Empty;

    public string Difficulty { get; init; } = string.Empty;

    public List<string> ExpectedConcepts { get; init; } = [];

    public List<int> SourcePages { get; init; } = [];

    public string EvaluationGuidance { get; init; } = string.Empty;

    public List<StudentAnswerDetailDto> Answers { get; init; } = [];
}

public sealed class StudentAnswerDetailDto
{
    public Guid Id { get; init; }

    public string AnswerText { get; init; } = string.Empty;

    public DateTime SubmittedAtUtc { get; init; }

    public double? Score { get; init; }

    public AssessmentAnswerStatus? AnswerStatus { get; init; }

    public LearningLevel? Level { get; init; }

    public string? Feedback { get; init; }

    public List<string> UnderstoodConcepts { get; init; } = [];

    public List<string> MissingConcepts { get; init; } = [];

    public List<string> Misconceptions { get; init; } = [];
}

public sealed class LessonProgressDto
{
    public Guid LessonId { get; init; }
    public LessonProgressStatus Status { get; init; }
    public DateTime? ReadAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public DateTime? LastReviewedAtUtc { get; init; }
    public int? UnderstandingLevel { get; init; }
    public Guid? LastAssessmentId { get; init; }
}

public sealed class UpdateLessonProgressRequest
{
    public LessonProgressStatus Status { get; init; }
}

public sealed class ReviewRecommendationDto
{
    public Guid Id { get; init; }
    public Guid BookId { get; init; }
    public Guid? LessonId { get; init; }
    public string ConceptKey { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string ReasonCode { get; init; } = string.Empty;
    public LearningLevel CurrentLearningLevel { get; init; }
    public AdaptiveLearningAction RecommendedAction { get; init; }
    public string SuggestedQuestionType { get; init; } = string.Empty;
    public List<int> SourcePages { get; init; } = [];
    public int Priority { get; init; }
    public DateTime GeneratedAtUtc { get; init; }
}
