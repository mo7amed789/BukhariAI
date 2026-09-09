namespace BukhariAI.Domain.Entities;

public sealed class Person
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string? DetailedBiographyJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<LessonPerson> LessonPeople { get; set; } = [];

    public ICollection<HadithPerson> HadithPeople { get; set; } = [];

    public ICollection<KnownPerson> KnownPersons { get; set; } = [];
}
