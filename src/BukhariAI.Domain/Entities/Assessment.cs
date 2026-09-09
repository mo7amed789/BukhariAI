namespace BukhariAI.Domain.Entities;

/// <summary>
/// Represents an assessment session containing one or more questions for a lesson or book.
/// </summary>
public sealed class Assessment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public Guid? LessonId { get; set; }

    public Lesson? Lesson { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<AssessmentQuestion> Questions { get; set; } = [];
}
