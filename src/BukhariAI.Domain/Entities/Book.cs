namespace BukhariAI.Domain.Entities;

public sealed class Book
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid UserId { get; set; } = User.DefaultUserId;

    public User? User { get; set; }

    // Navigation properties
    public ICollection<BookPage> BookPages { get; set; } = [];

    public ICollection<Lesson> Lessons { get; set; } = [];

    public ICollection<ReadingSession> ReadingSessions { get; set; } = [];

    public ICollection<LessonLearningContext> LearningContexts { get; set; } = [];

    public ICollection<StudentConceptMastery> ConceptMasteries { get; set; } = [];

    public ICollection<Assessment> Assessments { get; set; } = [];

    public ICollection<ReviewRecommendation> ReviewRecommendations { get; set; } = [];
}
