using BukhariAI.Application.Lessons.Chat;

namespace BukhariAI.Application.Abstractions;

public interface ILessonChatService
{
    Task<LessonChatResponse> AskLessonQuestionAsync(
        LessonChatRequest request,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<List<ChatSessionDto>> GetChatSessionsAsync(
        Guid? bookId = null,
        Guid? lessonId = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<ChatSessionDetailDto?> GetChatSessionByIdAsync(
        Guid sessionId,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<ChatSessionDto> CreateChatSessionAsync(
        CreateChatSessionRequest request,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteChatSessionAsync(
        Guid sessionId,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<bool> ClearChatHistoryAsync(
        Guid? sessionId = null,
        Guid? lessonId = null,
        Guid? bookId = null,
        Guid? userId = null,
        CancellationToken cancellationToken = default);
}
