using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.Chat;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Mastery;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BukhariAI.UnitTests;

public class ResourceOwnershipSecurityTests
{
    private BukhariDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new BukhariDbContext(options);
    }

    [Fact]
    public async Task ChatSession_UserIsolation_StudentBCannotAccessStudentASession()
    {
        using var db = CreateDbContext("ChatSession_Isolation");
        var httpClient = new HttpClient();
        var options = Microsoft.Extensions.Options.Options.Create(new AiOptions());
        var mockLessonService = new Mock<ILessonPersistenceService>();
        var mockSettings = new Mock<ISettingsService>();

        var chatService = new LessonChatService(
            httpClient,
            options,
            db,
            mockLessonService.Object,
            mockSettings.Object,
            NullLogger<LessonChatService>.Instance);

        var studentAId = Guid.NewGuid();
        var studentBId = Guid.NewGuid();

        // Student A creates a chat session
        var sessionDto = await chatService.CreateChatSessionAsync(new CreateChatSessionRequest
        {
            Title = "مذاكرة الطالب الأول"
        }, studentAId);

        Assert.NotNull(sessionDto);
        var sessionId = sessionDto.Id;

        // Student A can access their session
        var sessionForA = await chatService.GetChatSessionByIdAsync(sessionId, studentAId);
        Assert.NotNull(sessionForA);
        Assert.Equal(sessionId, sessionForA.Id);

        // Student B tries to access Student A's session using sessionId -> MUST return null (isolated)
        var sessionForB = await chatService.GetChatSessionByIdAsync(sessionId, studentBId);
        Assert.Null(sessionForB);

        // Student B's session list does NOT contain Student A's session
        var listForB = await chatService.GetChatSessionsAsync(null, null, studentBId);
        Assert.DoesNotContain(listForB, s => s.Id == sessionId);

        // Student B tries to delete Student A's session -> fails
        bool deletedByB = await chatService.DeleteChatSessionAsync(sessionId, studentBId);
        Assert.False(deletedByB);

        // Verify session still exists in DB for Student A
        var sessionStillExists = await chatService.GetChatSessionByIdAsync(sessionId, studentAId);
        Assert.NotNull(sessionStillExists);
    }

    [Fact]
    public async Task LessonProgress_UserIsolation_SeparateProgressPerStudent()
    {
        using var db = CreateDbContext("LessonProgress_Isolation");
        var learningService = new StudentLearningService(db);

        var lessonId = Guid.NewGuid();
        var studentAId = Guid.NewGuid();
        var studentBId = Guid.NewGuid();

        // Student A starts the lesson
        await learningService.UpdateLessonProgressAsync(lessonId, new BukhariAI.Application.Assessments.UpdateLessonProgressRequest
        {
            Status = LessonProgressStatus.Completed
        }, studentAId);

        // Check progress for Student A
        var progressA = await learningService.GetLessonProgressAsync(lessonId, studentAId);
        Assert.NotNull(progressA);
        Assert.Equal(LessonProgressStatus.Completed, progressA.Status);

        // Student B has not started this lesson yet
        var progressB = await learningService.GetLessonProgressAsync(lessonId, studentBId);
        Assert.Null(progressB);

        // Student B marks as InProgress
        await learningService.UpdateLessonProgressAsync(lessonId, new BukhariAI.Application.Assessments.UpdateLessonProgressRequest
        {
            Status = LessonProgressStatus.InProgress
        }, studentBId);

        // Both progresses remain completely independent
        var refreshedA = await learningService.GetLessonProgressAsync(lessonId, studentAId);
        var refreshedB = await learningService.GetLessonProgressAsync(lessonId, studentBId);

        Assert.Equal(LessonProgressStatus.Completed, refreshedA!.Status);
        Assert.Equal(LessonProgressStatus.InProgress, refreshedB!.Status);
    }

    [Fact]
    public async Task ConceptMastery_UserIsolation_ScoresAreSeparatedPerStudent()
    {
        using var db = CreateDbContext("ConceptMastery_Isolation");
        var engine = new MasteryEngineService(db, NullLogger<MasteryEngineService>.Instance);

        var bookId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();
        var studentAId = Guid.NewGuid();
        var studentBId = Guid.NewGuid();

        // Record exposure for Student A
        await engine.RecordConceptExposuresAsync(bookId, lessonId, ["الدباغ", "الطهورية"], studentAId);

        // Check masteries for Student A
        var masteriesA = await engine.GetStudentMasteryByBookIdAsync(bookId, studentAId);
        Assert.Equal(2, masteriesA.Count);

        // Student B should have 0 masteries
        var masteriesB = await engine.GetStudentMasteryByBookIdAsync(bookId, studentBId);
        Assert.Empty(masteriesB);
    }

    [Fact]
    public async Task Book_UserIsolation_StudentsOwnIndependentBookCollections()
    {
        using var db = CreateDbContext("Book_UserIsolation");
        var persistenceService = new LessonPersistenceService(db, NullLogger<LessonPersistenceService>.Instance);

        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();

        // User A creates a specific book
        var bookA = await persistenceService.CreateBookAsync("كتاب الموطأ للإمام مالك", userAId);
        Assert.Equal(userAId, bookA.UserId);

        // User B creates a different book
        var bookB = await persistenceService.CreateBookAsync("سنن أبي داود", userBId);
        Assert.Equal(userBId, bookB.UserId);

        // User A gets their books
        var booksForA = await persistenceService.GetBooksAsync(userAId);
        Assert.Contains(booksForA, b => b.Id == bookA.Id);
        Assert.DoesNotContain(booksForA, b => b.Id == bookB.Id);

        // User B gets their books
        var booksForB = await persistenceService.GetBooksAsync(userBId);
        Assert.Contains(booksForB, b => b.Id == bookB.Id);
        Assert.DoesNotContain(booksForB, b => b.Id == bookA.Id);
    }
}
