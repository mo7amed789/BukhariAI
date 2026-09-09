namespace BukhariAI.Domain.Entities;

public sealed class LessonHadith
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LessonId { get; set; }

    public Lesson Lesson { get; set; } = null!;

    public string Reference { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Problem { get; set; } = string.Empty;

    public string Reasoning { get; set; } = string.Empty;

    public string ScholarlyDiscussion { get; set; } = string.Empty;

    public string Conclusion { get; set; } = string.Empty;

    public string EasyExplanation { get; set; } = string.Empty;

    public string HistoricalContext { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<HadithPerson> HadithPeople { get; set; } = [];

    public ICollection<HadithEvidence> Evidences { get; set; } = [];

    public ICollection<HadithLessonPoint> LessonPoints { get; set; } = [];
}
