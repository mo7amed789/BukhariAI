namespace BukhariAI.Domain.Entities;

public sealed class ReadingSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public Guid? LessonId { get; set; }

    public Lesson? Lesson { get; set; }

    public int StartPage { get; set; }

    public int EndPage { get; set; }

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }

    public ReadingSessionStatus Status { get; set; } = ReadingSessionStatus.InProgress;
}
