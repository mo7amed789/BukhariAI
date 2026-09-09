using BukhariAI.Application.Abstractions;
using BukhariAI.Application.AdaptiveLearning;
using BukhariAI.Application.Assessments;
using BukhariAI.Application.LearningSessions;
using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Infrastructure.Persistence;

/// <summary>Single read-model façade that turns existing learner evidence into the next tutor experience.</summary>
public sealed class LearningSessionService : ILearningSessionService
{
    private readonly BukhariDbContext _db;
    private readonly IAdaptiveLearningService _adaptive;
    private readonly IStudentLearningService _studentLearning;
    private readonly ILogger<LearningSessionService> _logger;

    public LearningSessionService(BukhariDbContext db, IAdaptiveLearningService adaptive, IStudentLearningService studentLearning, ILogger<LearningSessionService> logger)
        => (_db, _adaptive, _studentLearning, _logger) = (db, adaptive, studentLearning, logger);

    public async Task<LearningSessionNextDto> GetNextAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var lessons = await _db.Lessons.AsNoTracking().Where(l => l.BookId == bookId)
            .Include(l => l.LessonPages).Include(l => l.LessonProgresses).Include(l => l.Assessments).ThenInclude(a => a.Questions).ThenInclude(q => q.StudentAnswers)
            .OrderBy(l => l.StartPage).ToListAsync(cancellationToken);
        var adaptive = await _adaptive.GetAdaptiveStateAsync(bookId, LearningIntent.ContinueLearning, cancellationToken);

        var unfinished = lessons.FirstOrDefault(l => l.LessonProgresses.FirstOrDefault()?.Status is LessonProgressStatus.NotStarted or LessonProgressStatus.InProgress or LessonProgressStatus.NeedsReview);
        if (unfinished is not null)
        {
            var pendingAssessment = unfinished.Assessments.FirstOrDefault(a => a.Questions.Any(q => q.StudentAnswers.Count == 0));
            if (pendingAssessment is not null && unfinished.LessonProgresses.FirstOrDefault()?.Status != LessonProgressStatus.NotStarted)
                return Log(Build(LearningSessionAction.TakeAssessment, "assessment_pending", "الدرس قُرئ لكن يوجد تقييم لم يُجب عنه.", LearningIntent.ContinueLearning, unfinished, pendingAssessment.Id));
            return Log(Build(LearningSessionAction.ContinueLesson, "unfinished_lesson", "يوجد درس لم يكتمل تعليميًا بعد.", LearningIntent.ContinueLearning, unfinished));
        }

        var reviews = await _studentLearning.GetReviewRecommendationsAsync(bookId, cancellationToken);
        var due = reviews.OrderByDescending(r => r.Priority).FirstOrDefault(r => r.RecommendedAction == AdaptiveLearningAction.Review);
        if (due is not null)
            return Log(new LearningSessionNextDto { Action = LearningSessionAction.ReviewConcept, ReasonCode = due.ReasonCode, Reason = due.Reason, LearningIntent = LearningIntent.ReviewDueConcepts, LessonId = due.LessonId, SourcePages = due.SourcePages, Review = due, LearningLevel = due.CurrentLearningLevel, Concepts = adaptive.Concepts.Where(c => c.ConceptKey == due.ConceptKey).ToList() });

        var weak = adaptive.Concepts.Where(c => c.IsWeak).OrderBy(c => c.Confidence).FirstOrDefault();
        if (weak is not null)
            return Log(new LearningSessionNextDto { Action = LearningSessionAction.ReinforceConcept, ReasonCode = weak.ReasonCode, Reason = weak.Reason, LearningIntent = LearningIntent.ReinforceWeakConcepts, LessonId = FindLesson(lessons, weak), SourcePages = FindPages(lessons, weak), Concepts = [weak], LearningLevel = weak.LearningLevel, Confidence = weak.Confidence });

        var verify = adaptive.Concepts.FirstOrDefault(c => c.Action == AdaptiveLearningAction.VerifyMastery);
        if (verify is not null)
            return Log(new LearningSessionNextDto { Action = LearningSessionAction.VerifyMastery, ReasonCode = verify.ReasonCode, Reason = verify.Reason, LearningIntent = LearningIntent.VerifyMastery, LessonId = FindLesson(lessons, verify), SourcePages = FindPages(lessons, verify), Concepts = [verify], LearningLevel = verify.LearningLevel, Confidence = verify.Confidence });

        if (lessons.Count == 0)
            return Log(new LearningSessionNextDto { Action = LearningSessionAction.StartNewLesson, ReasonCode = "new_book", Reason = "لا توجد دروس محفوظة بعد.", LearningIntent = LearningIntent.NewLesson, SourcePages = [1, 2] });

        var completedEnd = lessons.Where(l => l.LessonProgresses.FirstOrDefault()?.Status == LessonProgressStatus.Completed).Select(l => l.EndPage).DefaultIfEmpty(0).Max();
        var knownPages = await _db.BookPages.AsNoTracking().Where(p => p.BookId == bookId).Select(p => p.PageNumber).ToListAsync(cancellationToken);
        var nextPages = knownPages.Where(p => p > completedEnd).OrderBy(p => p).Take(2).ToList();
        if (nextPages.Count > 0)
            return Log(new LearningSessionNextDto { Action = LearningSessionAction.ContinueToNextPages, ReasonCode = "next_uncompleted_source_pages", Reason = "الصفحات التالية محفوظة ولم تُدرس بعد.", LearningIntent = LearningIntent.NewLesson, SourcePages = nextPages });
        return Log(new LearningSessionNextDto { Action = LearningSessionAction.Completed, ReasonCode = "known_source_complete", Reason = "اكتملت جميع صفحات المصدر المحفوظة.", LearningIntent = LearningIntent.ContinueLearning });
    }

    public async Task<LearningDashboardDto> GetDashboardAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var lessons = await _db.Lessons.AsNoTracking().Where(l => l.BookId == bookId).Include(l => l.LessonProgresses).OrderBy(l => l.StartPage).ToListAsync(cancellationToken);
        var adaptive = await _adaptive.GetAdaptiveStateAsync(bookId, LearningIntent.ContinueLearning, cancellationToken);
        var next = await GetNextAsync(bookId, cancellationToken);
        var current = lessons.FirstOrDefault(l => l.LessonProgresses.FirstOrDefault()?.Status != LessonProgressStatus.Completed);
        return new LearningDashboardDto { BookId = bookId, TotalLessons = lessons.Count, CompletedLessons = lessons.Count(l => l.LessonProgresses.FirstOrDefault()?.Status == LessonProgressStatus.Completed), CurrentLessonId = current?.Id, CurrentPages = current is null ? [] : Enumerable.Range(current.StartPage, Math.Max(0, current.EndPage-current.StartPage+1)).ToList(), IntroducedConcepts = adaptive.Concepts.Count(c => c.LearningLevel == LearningLevel.Introduced), UnderstoodConcepts = adaptive.Concepts.Count(c => c.LearningLevel == LearningLevel.Understood), MasteredConcepts = adaptive.Concepts.Count(c => c.LearningLevel == LearningLevel.Mastered), WeakConcepts = adaptive.Concepts.Count(c => c.IsWeak), DueReviews = adaptive.Concepts.Count(c => c.IsReviewDue), Next = next };
    }

    private LearningSessionNextDto Log(LearningSessionNextDto next)
    {
        _logger.LogInformation("LearningSessionDecision: Action={Action}, Intent={Intent}, LessonId={LessonId}, Level={Level}, Confidence={Confidence}, ReasonCode={ReasonCode}", next.Action, next.LearningIntent, next.LessonId, next.LearningLevel, next.Confidence, next.ReasonCode);
        return next;
    }
    private static LearningSessionNextDto Build(LearningSessionAction action, string code, string reason, LearningIntent intent, Lesson lesson, Guid? assessment = null) => new() { Action = action, ReasonCode = code, Reason = reason, LearningIntent = intent, LessonId = lesson.Id, SourcePages = lesson.LessonPages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToList(), Progress = lesson.LessonProgresses.FirstOrDefault() is { } p ? new LessonProgressDto { LessonId = p.LessonId, Status = p.Status, ReadAtUtc = p.ReadAtUtc, CompletedAtUtc = p.CompletedAtUtc, LastAssessmentId = p.LastAssessmentId, UnderstandingLevel = p.UnderstandingLevel } : null, AssessmentId = assessment };
    private static Guid? FindLesson(List<Lesson> lessons, AdaptiveConceptState state) => state.LastAssessedLessonId ?? state.FirstIntroducedLessonId;
    private static List<int> FindPages(List<Lesson> lessons, AdaptiveConceptState state) => lessons.FirstOrDefault(lesson => lesson.Id == (state.LastAssessedLessonId ?? state.FirstIntroducedLessonId))?.LessonPages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToList() ?? [];
}
