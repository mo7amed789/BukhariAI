using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Mastery;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BukhariAI.UnitTests;

public class AssessmentIdempotencyStateMachineTests
{
    private BukhariDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new BukhariDbContext(options);
    }

    private (Book book, Lesson lesson, Assessment assessment, AssessmentQuestion question) SeedAssessmentData(BukhariDbContext db)
    {
        var book = new Book { Id = Guid.NewGuid(), Title = "صحيح البخاري" };
        var lesson = new Lesson { Id = Guid.NewGuid(), BookId = book.Id, Title = "كتاب الوضوء", Overview = "شرح الوضوء" };
        var assessment = new Assessment { Id = Guid.NewGuid(), BookId = book.Id, LessonId = lesson.Id, Title = "اختبار الدرس الأول" };
        var question = new AssessmentQuestion
        {
            Id = Guid.NewGuid(),
            AssessmentId = assessment.Id,
            SourceLessonId = lesson.Id,
            Question = "ما حكم دباغ جلود الميتة؟",
            ExpectedConceptsCsv = "الدباغ,الطهارة",
            EvaluationGuidance = "التحقق من فهم أثر الدباغ في التطهير"
        };

        db.Books.Add(book);
        db.Lessons.Add(lesson);
        db.Assessments.Add(assessment);
        db.AssessmentQuestions.Add(question);
        db.SaveChanges();

        return (book, lesson, assessment, question);
    }

    [Fact]
    public async Task SubmitAnswer_SuccessfulEvaluation_LifecycleStatusTransitionsToCompleted()
    {
        using var db = CreateDbContext("Lifecycle_Completed");
        var (_, _, _, question) = SeedAssessmentData(db);

        var mockEvaluator = new Mock<IAssessmentEvaluator>();
        mockEvaluator.Setup(e => e.EvaluateAnswerAsync(It.IsAny<AssessmentEvaluationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentEvaluationResult
            {
                Score = 0.95,
                Level = LearningLevel.Mastered,
                UnderstoodConcepts = ["الدباغ", "الطهارة"],
                MissingConcepts = [],
                Misconceptions = [],
                Feedback = "إجابة ممتازة ومطابقة للأدلة."
            });

        var masteryEngine = new MasteryEngineService(db, NullLogger<MasteryEngineService>.Instance);
        var studentLearning = new StudentLearningService(db);
        var assessmentService = new AssessmentService(db, mockEvaluator.Object, masteryEngine, NullLogger<AssessmentService>.Instance, studentLearning);

        var studentId = Guid.NewGuid();
        var submissionId = "sub-12345";

        var response = await assessmentService.SubmitAnswerAsync(new SubmitStudentAnswerRequest
        {
            QuestionId = question.Id,
            StudentAnswer = "الدباغ يطهر جلد الميتة لحديث أيما إهاب دبغ فقد طهر",
            SubmissionId = submissionId
        }, studentId);

        Assert.NotNull(response);
        Assert.Equal(0.95, response.Score);
        Assert.Equal(AssessmentAnswerStatus.Correct, response.AnswerStatus);

        // Verify stored StudentAnswer has Completed lifecycle status
        var storedAnswer = await db.StudentAnswers.Include(sa => sa.AssessmentResult).FirstOrDefaultAsync(sa => sa.Id == response.AnswerId);
        Assert.NotNull(storedAnswer);
        Assert.Equal(EvaluationLifecycleStatus.Completed, storedAnswer.LifecycleStatus);
        Assert.Equal(studentId, storedAnswer.UserId);
        Assert.Null(storedAnswer.ErrorMessage);
        Assert.NotNull(storedAnswer.AssessmentResult);

        // Replaying with identical submissionId must return the cached result without calling evaluator again
        var replayResponse = await assessmentService.SubmitAnswerAsync(new SubmitStudentAnswerRequest
        {
            QuestionId = question.Id,
            StudentAnswer = "إجابة مكررة",
            SubmissionId = submissionId
        }, studentId);

        Assert.Equal(response.AnswerId, replayResponse.AnswerId);
        Assert.Equal(response.ResultId, replayResponse.ResultId);
        mockEvaluator.Verify(e => e.EvaluateAnswerAsync(It.IsAny<AssessmentEvaluationInput>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SubmitAnswer_EvaluationThrowsException_StatusTransitionsToFailed()
    {
        using var db = CreateDbContext("Lifecycle_Failed");
        var (_, _, _, question) = SeedAssessmentData(db);

        var mockEvaluator = new Mock<IAssessmentEvaluator>();
        mockEvaluator.Setup(e => e.EvaluateAnswerAsync(It.IsAny<AssessmentEvaluationInput>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("AI service timeout or network error"));

        var masteryEngine = new MasteryEngineService(db, NullLogger<MasteryEngineService>.Instance);
        var studentLearning = new StudentLearningService(db);
        var assessmentService = new AssessmentService(db, mockEvaluator.Object, masteryEngine, NullLogger<AssessmentService>.Instance, studentLearning);

        var studentId = Guid.NewGuid();
        var submissionId = "sub-failure-test";

        await Assert.ThrowsAsync<HttpRequestException>(() => assessmentService.SubmitAnswerAsync(new SubmitStudentAnswerRequest
        {
            QuestionId = question.Id,
            StudentAnswer = "إجابة أثناء انقطاع الاتصال",
            SubmissionId = submissionId
        }, studentId));

        // Verify the record was saved with Failed status and error message
        var failedAnswer = await db.StudentAnswers.FirstOrDefaultAsync(sa => sa.SubmissionId == submissionId);
        Assert.NotNull(failedAnswer);
        Assert.Equal(EvaluationLifecycleStatus.Failed, failedAnswer.LifecycleStatus);
        Assert.Contains("timeout", failedAnswer.ErrorMessage);
    }
}
