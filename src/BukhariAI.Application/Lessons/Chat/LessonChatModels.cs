namespace BukhariAI.Application.Lessons.Chat;

public sealed class ChatMessageDto
{
    public Guid? Id { get; set; }
    public string Role { get; set; } = "user"; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public List<string>? SuggestedQuestions { get; set; }
    public List<string>? SourcesCited { get; set; }
    public DateTime? CreatedAtUtc { get; set; }
}

public sealed class ChatSessionDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? BookId { get; set; }
    public string? BookTitle { get; set; }
    public Guid? LessonId { get; set; }
    public string? LessonTitle { get; set; }
    public int MessageCount { get; set; }
    public string? LastMessagePreview { get; set; }
    public string? ContextSummary { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class ChatSessionDetailDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public Guid? BookId { get; set; }
    public string? BookTitle { get; set; }
    public Guid? LessonId { get; set; }
    public string? LessonTitle { get; set; }
    public string? ContextSummary { get; set; }
    public List<ChatMessageDto> Messages { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class CreateChatSessionRequest
{
    public Guid? BookId { get; set; }
    public Guid? LessonId { get; set; }
    public string? Title { get; set; }
}

public sealed class LessonChatRequest
{
    public Guid? SessionId { get; set; }
    public Guid? LessonId { get; set; }
    public Guid? BookId { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<ChatMessageDto> History { get; set; } = [];
}

public sealed class LessonChatResponse
{
    public Guid SessionId { get; set; }
    public string Reply { get; set; } = string.Empty;
    public List<string> SuggestedQuestions { get; set; } = [];
    public string? LessonTitle { get; set; }
    public string? BookTitle { get; set; }
    public List<string> SourcesCited { get; set; } = [];
    public string? ContextSummary { get; set; }
}
