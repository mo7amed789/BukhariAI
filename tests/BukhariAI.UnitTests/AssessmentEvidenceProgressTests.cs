using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Mastery;
using BukhariAI.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BukhariAI.UnitTests;

public sealed class AssessmentEvidenceProgressTests
{
    private static BukhariDbContext CreateDb() => new(new DbContextOptionsBuilder<BukhariDbContext>()
        .UseInMemoryDatabase($"assessment-evidence-{Guid.NewGuid()}").Options);

    [Fact]
    public async Task RepeatedCorrectAnswersInOneContext_DoNotProduceMastered()
    {
        using var db = CreateDb();
        var engine = new MasteryEngineService(db, NullLogger<MasteryEngineService>.Instance);
        var book = Guid.NewGuid();
        var lesson = Guid.NewGuid();
        var answer = new AssessmentEvaluationResult { Score = 1, UnderstoodConcepts = ["الدباغ"] };

        for (var i = 0; i < 4; i++) await engine.UpdateMasteryAfterAssessmentAsync(book, lesson, ["الدباغ"], answer);

        var mastery = (await engine.GetStudentMasteryByBookIdAsync(book)).Single();
        mastery.CorrectAnswerCount.Should().Be(4);
        mastery.DemonstratedContextCount.Should().Be(1);
        mastery.LearningLevel.Should().NotBe(LearningLevel.Mastered);
    }

    [Fact]
    public async Task WeakEvidence_CreatesEvidenceBasedReviewRecommendation()
    {
        using var db = CreateDb();
        var bookId = Guid.NewGuid();
        db.StudentConceptMasteries.Add(new StudentConceptMastery
        {
            BookId = bookId, ConceptKey = "الولاء", ExposureCount = 2, AssessmentCount = 1,
            CorrectAnswerCount = 0, MasteryScore = .15, LearningLevel = LearningLevel.Introduced
        });
        await db.SaveChangesAsync();
        var service = new StudentLearningService(db);

        var recommendations = await service.GetReviewRecommendationsAsync(bookId);

        recommendations.Should().ContainSingle(r => r.ConceptKey == "الولاء" && r.Priority == 100);
    }

    [Fact]
    public async Task IncorrectAssessment_MarksLessonNeedsReview()
    {
        using var db = CreateDb();
        var book = new Book { Title = "كتاب", Author = "مؤلف" };
        var lesson = new Lesson { Book = book, Title = "درس", Overview = "", HistoricalContext = "" };
        var assessment = new Assessment { Book = book, Lesson = lesson, Title = "تقييم" };
        var question = new AssessmentQuestion { Assessment = assessment, SourceLesson = lesson, Question = "سؤال", QuestionType = "Understanding", Difficulty = "Beginner" };
        var answer = new StudentAnswer { AssessmentQuestion = question, AnswerText = "جواب" };
        db.AddRange(book, lesson, assessment, question, answer,
            new LessonProgress { Lesson = lesson, Status = LessonProgressStatus.InProgress },
            new AssessmentResult { StudentAnswer = answer, Score = .2, AnswerStatus = AssessmentAnswerStatus.Incorrect, Level = LearningLevel.Introduced });
        await db.SaveChangesAsync();
        var service = new StudentLearningService(db);

        await service.RefreshLessonProgressAfterAssessmentAsync(lesson.Id, assessment.Id);

        (await service.GetLessonProgressAsync(lesson.Id))!.Status.Should().Be(LessonProgressStatus.NeedsReview);
    }

    [Fact]
    public async Task PartialAnswer_IsClassifiedAndDuplicateSubmissionDoesNotCreateNewEvidence()
    {
        using var db = CreateDb();
        var book = new Book { Title = "كتاب", Author = "مؤلف" };
        var lesson = new Lesson { Book = book, Title = "درس", Overview = "", HistoricalContext = "" };
        var assessment = new Assessment { Book = book, Lesson = lesson, Title = "تقييم" };
        var question = new AssessmentQuestion
        {
            Assessment = assessment, SourceLesson = lesson, Question = "اشرح الدباغ", QuestionType = "Understanding",
            Difficulty = "Beginner", ExpectedConceptsCsv = "الدباغ", SourcePagesCsv = "1", EvaluationGuidance = "المعنى"
        };
        db.AddRange(book, lesson, assessment, question, new LessonProgress { Lesson = lesson, Status = LessonProgressStatus.InProgress });
        await db.SaveChangesAsync();
        var evaluator = new Mock<IAssessmentEvaluator>();
        evaluator.Setup(x => x.EvaluateAnswerAsync(It.IsAny<AssessmentEvaluationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentEvaluationResult { Score = .55, UnderstoodConcepts = ["الدباغ"], MissingConcepts = ["أثره"] });
        var service = new AssessmentService(db, evaluator.Object,
            new MasteryEngineService(db, NullLogger<MasteryEngineService>.Instance), NullLogger<AssessmentService>.Instance,
            new StudentLearningService(db));
        var request = new SubmitStudentAnswerRequest { QuestionId = question.Id, StudentAnswer = "هو معالجة الجلد", SubmissionId = "request-1" };

        var first = await service.SubmitAnswerAsync(request);
        var second = await service.SubmitAnswerAsync(request);

        first.AnswerStatus.Should().Be(AssessmentAnswerStatus.PartiallyCorrect);
        second.AnswerId.Should().Be(first.AnswerId);
        (await db.StudentAnswers.CountAsync()).Should().Be(1);
        evaluator.Verify(x => x.EvaluateAnswerAsync(It.IsAny<AssessmentEvaluationInput>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task MockAssessmentEvaluator_EvaluatesStudentConceptualUnderstanding_Properly()
    {
        var evaluator = new BukhariAI.Infrastructure.AI.MockAssessmentEvaluator(NullLogger<BukhariAI.Infrastructure.AI.MockAssessmentEvaluator>.Instance);
        var input = new AssessmentEvaluationInput
        {
            Question = "ما دلالة تشبيه الدباغ بالذكاة في قوله «دباغها ذكاتها» على قصر الطهارة على مأكول اللحم؟",
            StudentAnswer = "الدباغ كالذكاة يطهر جلد ما يحل أكله لو ذكي، أما ما لا يؤكل لحمه فلا يطهره الدباغ",
            ExpectedConcepts = ["دلالة تشبيه الدباغ بالذكاة", "مأكول اللحم"]
        };

        var result = await evaluator.EvaluateAnswerAsync(input);

        result.Should().NotBeNull();
        result.Score.Should().BeGreaterThan(0.5);
        result.UnderstoodConcepts.Should().NotBeEmpty();
        result.Feedback.Should().NotBeNullOrWhiteSpace();
    }
}
