namespace BukhariAI.Domain.Entities;

public sealed class ChatSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; } = User.DefaultUserId;

    public User? User { get; set; }

    public string Title { get; set; } = "محادثة جديدة";

    public Guid? BookId { get; set; }

    public Book? Book { get; set; }

    public Guid? LessonId { get; set; }

    public Lesson? Lesson { get; set; }

    public string? ContextSummary { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<ChatMessage> Messages { get; set; } = [];
}
