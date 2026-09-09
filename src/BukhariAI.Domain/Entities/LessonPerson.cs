namespace BukhariAI.Domain.Entities;

public sealed class LessonPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public Guid PersonId { get; set; }

    public Person Person { get; set; } = null!;

    public string ContextDescription { get; set; } = string.Empty;
}
