using BukhariAI.Application.Abstractions;

namespace BukhariAI.Application.Lessons.GenerateLesson;

public sealed class GenerateLessonCommand
{
    public required Stream PdfStream { get; init; }

    public required string FileName { get; init; }

    public int StartPage { get; init; }

    public int EndPage { get; init; }

    /// <summary>
    /// Optional book identifier. If null, the default Sahih al-Bukhari book is resolved.
    /// </summary>
    public Guid? BookId { get; init; }

    /// <summary>
    /// Optional accumulative context from prior lesson sessions.
    /// If null, the active educational context is loaded from the database.
    /// </summary>
    public LessonLearningContext? PreviousContext { get; init; }
}

public sealed class GenerateLessonFromTextCommand
{
    public required string SourceText { get; init; }

    public int StartPage { get; init; } = 1;

    public int EndPage { get; init; } = 1;

    public Guid? BookId { get; init; }

    public LessonLearningContext? PreviousContext { get; init; }
}

public sealed class GenerateLessonFromImagesCommand
{
    public required IReadOnlyList<PageScreenshot> Images { get; init; }

    public int StartPage { get; init; } = 1;

    public int EndPage { get; init; } = 1;

    public Guid? BookId { get; init; }

    public LessonLearningContext? PreviousContext { get; init; }
}
