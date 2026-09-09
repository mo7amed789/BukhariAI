namespace BukhariAI.Domain.Entities;

public sealed class HadithPerson
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HadithId { get; set; }

    public LessonHadith Hadith { get; set; } = null!;

    public Guid PersonId { get; set; }

    public Person Person { get; set; } = null!;
}
