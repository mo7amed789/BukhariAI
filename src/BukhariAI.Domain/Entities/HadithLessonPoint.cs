namespace BukhariAI.Domain.Entities;

public sealed class HadithLessonPoint
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HadithId { get; set; }

    public LessonHadith Hadith { get; set; } = null!;

    public string Point { get; set; } = string.Empty;
}
