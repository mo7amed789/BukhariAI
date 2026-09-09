using BukhariAI.Application.AdaptiveLearning;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;

namespace BukhariAI.Application.LearningSessions;

public enum LearningSessionAction { StartNewLesson, ContinueLesson, ReviewConcept, ReinforceConcept, TakeAssessment, VerifyMastery, ContinueToNextPages, Completed }

public sealed class LearningSessionNextDto
{
    public LearningSessionAction Action { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public LearningIntent LearningIntent { get; init; }
    public Guid? LessonId { get; init; }
    public List<int> SourcePages { get; init; } = [];
    public List<AdaptiveConceptState> Concepts { get; init; } = [];
    public LearningLevel? LearningLevel { get; init; }
    public double? Confidence { get; init; }
    public LessonProgressDto? Progress { get; init; }
    public Guid? AssessmentId { get; init; }
    public ReviewRecommendationDto? Review { get; init; }
}

public sealed class LearningDashboardDto
{
    public Guid BookId { get; init; }
    public int TotalLessons { get; init; }
    public int CompletedLessons { get; init; }
    public Guid? CurrentLessonId { get; init; }
    public List<int> CurrentPages { get; init; } = [];
    public int IntroducedConcepts { get; init; }
    public int UnderstoodConcepts { get; init; }
    public int MasteredConcepts { get; init; }
    public int WeakConcepts { get; init; }
    public int DueReviews { get; init; }
    public LearningSessionNextDto Next { get; init; } = new();
}
