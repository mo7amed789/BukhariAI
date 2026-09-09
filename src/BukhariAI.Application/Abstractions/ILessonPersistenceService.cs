using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using DomainLearningContext = BukhariAI.Domain.Entities.LessonLearningContext;
using AppLearningContext = BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext;

namespace BukhariAI.Application.Abstractions;

public interface ILessonPersistenceService
{
    /// <summary>
    /// Persists a generated lesson into the relational database:
    /// - Stores or links Book and BookPage records
    /// - Stores Lesson and LessonPage mappings
    /// - Stores Hadiths, Evidences, Points, Connections, and Review Questions
    /// - Resolves and links reusable People with LessonPerson context descriptions
    /// - Updates progressive learning context (KnownPerson, KnownTerm, KnownTopic)
    /// - Records ReadingSession and LessonProgress
    /// </summary>
    Task<Guid> PersistLessonAsync(
        GenerateLessonResponse response,
        PdfExtractionResult extractionResult,
        Guid? bookId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a complete lesson aggregate root by ID.
    /// </summary>
    Task<Lesson?> GetLessonByIdAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all lessons belonging to a book.
    /// </summary>
    Task<List<Lesson>> GetLessonsByBookIdAsync(
        Guid bookId,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the progressive learning memory context for a book.
    /// </summary>
    Task<DomainLearningContext?> GetLearningContextByBookIdAsync(
        Guid bookId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a compact educational context DTO for the AI prompt
    /// containing verified people, explained terms, studied topics, key ideas, and past discussions.
    /// </summary>
    Task<AppLearningContext> GetActiveEducationalContextAsync(
        Guid? bookId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists or updates a Quran Surah analysis as a structured Lesson in the database.
    /// If a lesson for this Surah already exists, updates it without creating duplicates.
    /// </summary>
    Task<Guid> PersistOrUpdateQuranSurahLessonAsync(
        BukhariAI.Application.Quran.QuranSurahAnalysisResponse response,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all registered books for the specified user or global.
    /// </summary>
    Task<List<Book>> GetBooksAsync(
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Creates a book collection for a user, or returns the existing collection with the same title.</summary>
    Task<Book> CreateBookAsync(
        string title,
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures that the default book "صحيح البخاري" exists in the database for the user.
    /// </summary>
    Task<Book> EnsureDefaultBookAsync(
        Guid? userId = null,
        CancellationToken cancellationToken = default);

    Task<Book> EnsureDefaultBookAsync(CancellationToken cancellationToken) => EnsureDefaultBookAsync(null, cancellationToken);

    Task<List<Book>> GetBooksAsync(CancellationToken cancellationToken) => GetBooksAsync(null, cancellationToken);

    Task<Book> CreateBookAsync(string title, CancellationToken cancellationToken) => CreateBookAsync(title, null, cancellationToken);
}
