using BukhariAI.Application.Abstractions;
using BukhariAI.Application.LearningSessions;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AdaptiveLearning;
using BukhariAI.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BukhariAI.UnitTests;

public sealed class LearningSessionServiceTests
{
    private static BukhariDbContext Db() => new(new DbContextOptionsBuilder<BukhariDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static LearningSessionService Service(BukhariDbContext db) => new(db,
        new AdaptiveLearningService(db, NullLogger<AdaptiveLearningService>.Instance),
        new StudentLearningService(db), NullLogger<LearningSessionService>.Instance);

    [Fact]
    public async Task NewBook_StartsFirstLessonWithoutSkippingPages()
    {
        using var db = Db(); var book = new Book { Title = "كتاب", Author = "مؤلف" }; db.Books.Add(book); await db.SaveChangesAsync();
        var next = await Service(db).GetNextAsync(book.Id);
        next.Action.Should().Be(LearningSessionAction.StartNewLesson);
        next.SourcePages.Should().Equal(1, 2);
        next.ReasonCode.Should().Be("new_book");
    }

    [Fact]
    public async Task UnfinishedLesson_TakesPriorityOverNewOrReviewWork()
    {
        using var db = Db(); var book = new Book { Title = "كتاب", Author = "مؤلف" };
        var lesson = new Lesson { Book = book, Title = "درس", StartPage = 110, EndPage = 111 };
        db.AddRange(book, lesson, new LessonProgress { Lesson = lesson, Status = LessonProgressStatus.InProgress }, new LessonPage { Lesson = lesson, BookPage = new BookPage { Book = book, PageNumber = 110, ExtractedText = "نص" }, PageNumber = 110 });
        await db.SaveChangesAsync();
        var next = await Service(db).GetNextAsync(book.Id);
        next.Action.Should().Be(LearningSessionAction.ContinueLesson);
        next.LessonId.Should().Be(lesson.Id);
    }

    [Fact]
    public async Task DashboardUsesExistingProgressAndAdaptiveEvidence()
    {
        using var db = Db(); var book = new Book { Title = "كتاب", Author = "مؤلف" };
        var lesson = new Lesson { Book = book, Title = "درس", StartPage = 1, EndPage = 2 };
        db.AddRange(book, lesson, new LessonProgress { Lesson = lesson, Status = LessonProgressStatus.Completed },
            new StudentConceptMastery { Book = book, ConceptKey = "الدباغ", LearningLevel = LearningLevel.Mastered, ExposureCount = 4, AssessmentCount = 3, CorrectAnswerCount = 3, DemonstratedContextCount = 3, MasteryScore = .9, LastAssessedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
        var dashboard = await Service(db).GetDashboardAsync(book.Id);
        dashboard.CompletedLessons.Should().Be(1);
        dashboard.MasteredConcepts.Should().Be(1);
        dashboard.Next.ReasonCode.Should().NotBeNullOrWhiteSpace();
    }
}
