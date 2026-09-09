namespace BukhariAI.Domain.Entities;

/// <summary>
/// Tracks the student's demonstrated understanding of a concept over time.
/// Decoupled from KnownTerm (which represents system/book knowledge memory).
/// </summary>
public sealed class StudentConceptMastery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; } = User.DefaultUserId;

    public User? User { get; set; }

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    /// <summary>
    /// The concept or term identifier/phrase being mastered (e.g. "الدباغ", "الولاء", "مرجع الضمير").
    /// </summary>
    public string ConceptKey { get; set; } = string.Empty;

    /// <summary>
    /// Current demonstrated mastery level (Introduced, Familiar, Understood, Mastered).
    /// </summary>
    public LearningLevel LearningLevel { get; set; } = LearningLevel.Introduced;

    /// <summary>
    /// Number of times the student was exposed to this concept in lessons.
    /// </summary>
    public int ExposureCount { get; set; } = 1;

    /// <summary>
    /// Total number of assessment questions answered for this concept.
    /// </summary>
    public int AssessmentCount { get; set; } = 0;

    /// <summary>
    /// Number of assessments where the student demonstrated solid understanding.
    /// </summary>
    public int CorrectAnswerCount { get; set; } = 0;

    /// <summary>Number of distinct lesson contexts with a correct demonstrated answer.</summary>
    public int DemonstratedContextCount { get; set; } = 0;

    /// <summary>
    /// Continuous mastery score between 0.0 and 1.0 based on repeated assessment evidence.
    /// </summary>
    public double MasteryScore { get; set; } = 0.0;

    /// <summary>
    /// Timestamp when this concept was last evaluated in an assessment.
    /// </summary>
    public DateTime? LastAssessedAt { get; set; }

    /// <summary>
    /// The lesson ID where this concept was first introduced.
    /// </summary>
    public Guid? FirstIntroducedLessonId { get; set; }

    public Lesson? FirstIntroducedLesson { get; set; }

    /// <summary>
    /// The lesson ID where this concept was most recently assessed.
    /// </summary>
    public Guid? LastAssessedLessonId { get; set; }

    public Lesson? LastAssessedLesson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
