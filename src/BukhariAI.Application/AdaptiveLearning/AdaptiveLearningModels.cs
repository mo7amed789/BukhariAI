using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.AdaptiveLearning;

public sealed class AdaptiveConceptState
{
    public string ConceptKey { get; init; } = string.Empty;
    public LearningLevel LearningLevel { get; init; }
    public double Confidence { get; init; }
    public int ExposureCount { get; init; }
    public int AssessmentCount { get; init; }
    public int CorrectAnswerCount { get; init; }
    public int DemonstratedContextCount { get; init; }
    public DateTime? LastAssessedAt { get; init; }
    public Guid? FirstIntroducedLessonId { get; init; }
    public Guid? LastAssessedLessonId { get; init; }
    public AdaptiveLearningAction Action { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string SuggestedQuestionType { get; init; } = string.Empty;
    public bool IsWeak { get; init; }
    public bool IsReviewDue { get; init; }
}

public sealed class AdaptiveLearningStateDto
{
    public Guid BookId { get; init; }
    public LearningIntent Intent { get; init; }
    public List<AdaptiveConceptState> Concepts { get; init; } = [];
}

public sealed class ContinueLearningRequest
{
    public LearningIntent Intent { get; init; } = LearningIntent.ContinueLearning;
}
