namespace BukhariAI.Domain.Entities;

/// <summary>
/// A student's raw submitted response to an assessment question.
/// </summary>
public sealed class StudentAnswer
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; } = User.DefaultUserId;

    public User? User { get; set; }

    public Guid AssessmentQuestionId { get; set; }

    public AssessmentQuestion AssessmentQuestion { get; set; } = null!;

    public string AnswerText { get; set; } = string.Empty;

    /// <summary>
    /// Optional client-generated idempotency key.  Replaying a request with the
    /// same key returns the original evaluation instead of recording new evidence.
    /// </summary>
    public string? SubmissionId { get; set; }

    public EvaluationLifecycleStatus LifecycleStatus { get; set; } = EvaluationLifecycleStatus.Completed;

    public string? ErrorMessage { get; set; }

    public byte[]? RowVersion { get; set; }

    public DateTime SubmittedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation property
    public AssessmentResult? AssessmentResult { get; set; }
}
