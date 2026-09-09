namespace BukhariAI.Domain.Entities;

public sealed class Lesson
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BookId { get; set; }

    public Book Book { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string Overview { get; set; } = string.Empty;

    public string HistoricalContext { get; set; } = string.Empty;

    public int StartPage { get; set; }

    public int EndPage { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public ICollection<LessonPage> LessonPages { get; set; } = [];

    public ICollection<LessonHadith> Hadiths { get; set; } = [];

    public ICollection<LessonConnection> Connections { get; set; } = [];

    public ICollection<ReviewQuestion> ReviewQuestions { get; set; } = [];

    public ICollection<LessonPerson> LessonPeople { get; set; } = [];

    public ICollection<LessonProgress> LessonProgresses { get; set; } = [];

    public ICollection<Assessment> Assessments { get; set; } = [];

    public ICollection<AssessmentQuestion> AssessmentQuestions { get; set; } = [];

    public ICollection<StudentConceptMastery> IntroducedMasteries { get; set; } = [];

    public ICollection<StudentConceptMastery> AssessedMasteries { get; set; } = [];

    public ICollection<ReadingSession> ReadingSessions { get; set; } = [];

    public ICollection<ReviewRecommendation> ReviewRecommendations { get; set; } = [];
}
