namespace BukhariAI.Domain.Entities;

public sealed class HadithEvidence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid HadithId { get; set; }

    public LessonHadith Hadith { get; set; } = null!;

    public string Text { get; set; } = string.Empty;

    public string Role { get; set; } = string.Empty;

    public string SourcePagesCsv { get; set; } = string.Empty;
}
