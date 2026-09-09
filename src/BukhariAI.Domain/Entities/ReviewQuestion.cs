namespace BukhariAI.Domain.Entities;

public sealed class ReviewQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public string Question { get; set; } = string.Empty;
}
