namespace BukhariAI.Domain.Entities;

/// <summary>
/// AI-evaluated assessment result for a student's answer, detailing score, understood concepts, and misconceptions.
/// </summary>
public sealed class AssessmentResult
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid StudentAnswerId { get; set; }

    public StudentAnswer StudentAnswer { get; set; } = null!;

    /// <summary>
    /// Evaluated score from 0.0 to 1.0.
    /// </summary>
    public double Score { get; set; }

    /// <summary>Semantic result of the answer, determined from source-bounded evidence.</summary>
    public AssessmentAnswerStatus AnswerStatus { get; set; } = AssessmentAnswerStatus.InsufficientEvidence;

    /// <summary>
    /// Demonstrated level on this specific assessment.
    /// </summary>
    public LearningLevel Level { get; set; } = LearningLevel.Unknown;

    /// <summary>
    /// JSON array of concepts demonstrated clearly by the student.
    /// </summary>
    public string UnderstoodConceptsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of expected concepts that were missing from the student answer.
    /// </summary>
    public string MissingConceptsJson { get; set; } = "[]";

    /// <summary>
    /// JSON array of identified misunderstandings or flawed deductions.
    /// </summary>
    public string MisconceptionsJson { get; set; } = "[]";

    /// <summary>
    /// Constructive pedagogical feedback for the student in Arabic.
    /// </summary>
    public string Feedback { get; set; } = string.Empty;

    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
}
