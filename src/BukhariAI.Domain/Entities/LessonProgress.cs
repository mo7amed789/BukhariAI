namespace BukhariAI.Domain.Entities;

public sealed class LessonProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; } = User.DefaultUserId;

    public User? User { get; set; }

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public LessonProgressStatus Status { get; set; } = LessonProgressStatus.NotStarted;

    public DateTime? ReadAtUtc { get; set; } = DateTime.UtcNow;

    public int? UnderstandingLevel { get; set; }

    public DateTime? LastReviewedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public Guid? LastAssessmentId { get; set; }

    public Assessment? LastAssessment { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
