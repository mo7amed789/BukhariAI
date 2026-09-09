namespace BukhariAI.Domain.Entities;

public sealed class LessonConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public string Description { get; set; } = string.Empty;
}
