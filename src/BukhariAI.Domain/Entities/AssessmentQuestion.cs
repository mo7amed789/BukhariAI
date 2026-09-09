namespace BukhariAI.Domain.Entities;

/// <summary>
/// A specific question generated to assess understanding of concepts taught in a lesson.
/// </summary>
public sealed class AssessmentQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AssessmentId { get; set; }

    public Assessment Assessment { get; set; } = null!;

    public Guid? SourceLessonId { get; set; }

    public Lesson? SourceLesson { get; set; }

    public string Question { get; set; } = string.Empty;

    /// <summary>
    /// Type of question (e.g. ConceptualExplanation, EvidenceAnalysis, RulingsAndApplication, ScholarlyDispute).
    /// </summary>
    public string QuestionType { get; set; } = string.Empty;

    /// <summary>
    /// Question difficulty level (Beginner, Intermediate, Advanced).
    /// </summary>
    public string Difficulty { get; set; } = "Intermediate";

    /// <summary>
    /// Comma-separated or JSON list of concepts targeted by this question.
    /// </summary>
    public string ExpectedConceptsCsv { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of source page numbers.
    /// </summary>
    public string SourcePagesCsv { get; set; } = string.Empty;

    /// <summary>
    /// Rubric and guidance for AI evaluator to score responses.
    /// </summary>
    public string EvaluationGuidance { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<StudentAnswer> StudentAnswers { get; set; } = [];
}
