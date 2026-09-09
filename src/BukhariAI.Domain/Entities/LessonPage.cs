namespace BukhariAI.Domain.Entities;

public sealed class LessonPage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public Guid BookPageId { get; set; }

    public BookPage BookPage { get; set; } = null!;

    public int PageNumber { get; set; }
}
