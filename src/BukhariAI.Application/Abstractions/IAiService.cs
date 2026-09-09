using BukhariAI.Application.Lessons.GenerateLesson;

namespace BukhariAI.Application.Abstractions;

public interface IAiService
{
    Task<GenerateLessonResponse> GenerateLessonAsync(
        IReadOnlyList<PageScreenshot> pageScreenshots,
        LessonLearningContext? context = null,
        CancellationToken cancellationToken = default);

    Task<GenerateLessonResponse> GenerateLessonFromTextAsync(
        string sourceText,
        LessonLearningContext? context = null,
        CancellationToken cancellationToken = default);
}
