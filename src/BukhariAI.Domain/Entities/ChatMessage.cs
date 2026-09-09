namespace BukhariAI.Domain.Entities;

public sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid SessionId { get; set; }

    public ChatSession Session { get; set; } = null!;

    public string Role { get; set; } = "user"; // "user" or "assistant"

    public string Content { get; set; } = string.Empty;

    public string? SuggestedQuestionsJson { get; set; }

    public string? SourcesCitedJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
