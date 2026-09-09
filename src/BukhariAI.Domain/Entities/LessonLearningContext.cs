namespace BukhariAI.Domain.Entities;

public sealed class LessonLearningContext
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<KnownPerson> KnownPeople { get; set; } = [];

    public ICollection<KnownTerm> KnownTerms { get; set; } = [];

    public ICollection<KnownTopic> KnownTopics { get; set; } = [];
}
