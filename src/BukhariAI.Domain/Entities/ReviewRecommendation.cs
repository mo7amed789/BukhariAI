namespace BukhariAI.Domain.Entities;

/// <summary>
/// A stored, evidence-based recommendation to revisit one concept.  It is
/// generated from assessment/exposure records, never from an unsupported AI guess.
/// </summary>
public sealed class ReviewRecommendation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public Guid? LessonId { get; set; }

    public Lesson? Lesson { get; set; }

    public string ConceptKey { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public string ReasonCode { get; set; } = string.Empty;

    public LearningLevel CurrentLearningLevel { get; set; }

    public AdaptiveLearningAction RecommendedAction { get; set; }

    public string SuggestedQuestionType { get; set; } = string.Empty;

    public string SourcePagesCsv { get; set; } = string.Empty;

    public int Priority { get; set; }

    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAtUtc { get; set; }
}
