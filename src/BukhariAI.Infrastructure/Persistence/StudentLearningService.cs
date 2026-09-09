using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AdaptiveLearning;
using Microsoft.EntityFrameworkCore;

namespace BukhariAI.Infrastructure.Persistence;

/// <summary>
/// Turns stored reading and assessment evidence into progress and review work.
/// No AI judgment is made here: all decisions are deterministic and auditable.
/// </summary>
public sealed class StudentLearningService : IStudentLearningService
{
    private readonly BukhariDbContext _db;

    public StudentLearningService(BukhariDbContext db) => _db = db;

    public Task<LessonProgressDto?> GetLessonProgressAsync(Guid lessonId, CancellationToken cancellationToken = default)
        => GetLessonProgressAsync(lessonId, null, cancellationToken);

    public async Task<LessonProgressDto?> GetLessonProgressAsync(Guid lessonId, Guid? userId, CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var progress = await _db.LessonProgresses.AsNoTracking()
            .FirstOrDefaultAsync(p => p.LessonId == lessonId && p.UserId == targetUserId, cancellationToken);
        return progress is null ? null : Map(progress);
    }

    public Task<LessonProgressDto> UpdateLessonProgressAsync(Guid lessonId, UpdateLessonProgressRequest request, CancellationToken cancellationToken = default)
        => UpdateLessonProgressAsync(lessonId, request, null, cancellationToken);

    public Task<List<StudentConceptMasteryDto>> GetWeakConceptsAsync(Guid bookId, CancellationToken cancellationToken = default)
        => GetWeakConceptsAsync(bookId, null, cancellationToken);

    public Task<List<ReviewRecommendationDto>> GetReviewRecommendationsAsync(Guid bookId, CancellationToken cancellationToken = default)
        => GetReviewRecommendationsAsync(bookId, null, cancellationToken);

    public Task RefreshLessonProgressAfterAssessmentAsync(Guid lessonId, Guid assessmentId, CancellationToken cancellationToken = default)
        => RefreshLessonProgressAfterAssessmentAsync(lessonId, assessmentId, null, cancellationToken);

    public async Task<LessonProgressDto> UpdateLessonProgressAsync(Guid lessonId, UpdateLessonProgressRequest request, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var progress = await _db.LessonProgresses.FirstOrDefaultAsync(p => p.LessonId == lessonId && p.UserId == targetUserId, cancellationToken);

        if (progress is null)
        {
            progress = new LessonProgress
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                LessonId = lessonId,
                Status = request.Status,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.LessonProgresses.Add(progress);
        }

        progress.Status = request.Status;
        var now = DateTime.UtcNow;
        if (request.Status == LessonProgressStatus.InProgress && progress.ReadAtUtc is null) progress.ReadAtUtc = now;
        if (request.Status == LessonProgressStatus.Completed) progress.CompletedAtUtc = now;
        if (request.Status == LessonProgressStatus.NeedsReview) progress.LastReviewedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);
        return Map(progress);
    }

    public async Task<List<StudentConceptMasteryDto>> GetWeakConceptsAsync(Guid bookId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var concepts = await _db.StudentConceptMasteries.AsNoTracking()
            .Where(m => m.BookId == bookId && m.UserId == targetUserId && m.LearningLevel != LearningLevel.Mastered &&
                (m.AssessmentCount > 0 || m.ExposureCount > 0) && m.MasteryScore < 0.65)
            .OrderBy(m => m.MasteryScore).ThenByDescending(m => m.AssessmentCount)
            .ToListAsync(cancellationToken);
        return concepts.Select(Map).ToList();
    }

    public async Task<List<ReviewRecommendationDto>> GetReviewRecommendationsAsync(Guid bookId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var masteries = await _db.StudentConceptMasteries
            .Where(m => m.BookId == bookId && m.UserId == targetUserId && m.LearningLevel != LearningLevel.Mastered)
            .ToListAsync(cancellationToken);
        var active = await _db.ReviewRecommendations
            .Where(r => r.BookId == bookId && r.ReviewedAtUtc == null)
            .ToListAsync(cancellationToken);
        var lessonIds = masteries.Select(m => m.LastAssessedLessonId ?? m.FirstIntroducedLessonId).Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        var lessonPages = await _db.LessonPages.AsNoTracking().Where(page => lessonIds.Contains(page.LessonId)).ToListAsync(cancellationToken);
        var pageMap = lessonPages.GroupBy(page => page.LessonId)
            .ToDictionary(group => group.Key, group => string.Join(',', group.OrderBy(page => page.PageNumber).Select(page => page.PageNumber)));
        var now = DateTime.UtcNow;

        foreach (var mastery in masteries)
        {
            var decision = AdaptiveLearningPolicy.Evaluate(mastery, now);
            if (!decision.IsWeak && !decision.IsReviewDue) continue;
            var existing = active.FirstOrDefault(r => Normalize(r.ConceptKey) == Normalize(mastery.ConceptKey));
            if (existing is null)
            {
                existing = new ReviewRecommendation { Id = Guid.NewGuid(), BookId = bookId, ConceptKey = mastery.ConceptKey };
                _db.ReviewRecommendations.Add(existing);
                active.Add(existing);
            }
            existing.LessonId = mastery.LastAssessedLessonId ?? mastery.FirstIntroducedLessonId;
            existing.Reason = decision.Reason;
            existing.ReasonCode = decision.ReasonCode;
            existing.CurrentLearningLevel = decision.LearningLevel;
            existing.RecommendedAction = decision.Action;
            existing.SuggestedQuestionType = decision.SuggestedQuestionType;
            var sourceLesson = mastery.LastAssessedLessonId ?? mastery.FirstIntroducedLessonId;
            existing.SourcePagesCsv = sourceLesson.HasValue && pageMap.TryGetValue(sourceLesson.Value, out var pages) ? pages : string.Empty;
            existing.Priority = decision.IsWeak ? 100 : 40;
            existing.GeneratedAtUtc = now;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return active.OrderByDescending(r => r.Priority).ThenByDescending(r => r.GeneratedAtUtc)
            .Select(Map).ToList();
    }

    public async Task RefreshLessonProgressAfterAssessmentAsync(Guid lessonId, Guid assessmentId, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var progress = await _db.LessonProgresses.FirstOrDefaultAsync(p => p.LessonId == lessonId && p.UserId == targetUserId, cancellationToken);
        if (progress is null) return;

        var results = await _db.AssessmentResults.AsNoTracking()
            .Where(r => r.StudentAnswer.AssessmentQuestion.SourceLessonId == lessonId && r.StudentAnswer.UserId == targetUserId)
            .ToListAsync(cancellationToken);
        if (results.Count == 0) return;

        progress.LastAssessmentId = assessmentId;
        progress.UnderstandingLevel = (int)Math.Round(results.Average(r => r.Score) * 100);
        var strong = results.Count(r => r.AnswerStatus is AssessmentAnswerStatus.Correct or AssessmentAnswerStatus.PartiallyCorrect);
        // Completing an assessment is evidence the lesson was read; the result determines if it is ready to complete.
        progress.Status = strong * 2 >= results.Count && results.Average(r => r.Score) >= 0.65
            ? LessonProgressStatus.Completed
            : LessonProgressStatus.NeedsReview;
        if (progress.Status == LessonProgressStatus.Completed) progress.CompletedAtUtc = DateTime.UtcNow;
        else progress.LastReviewedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static LessonProgressDto Map(LessonProgress p) => new()
    {
        LessonId = p.LessonId, Status = p.Status, ReadAtUtc = p.ReadAtUtc, CompletedAtUtc = p.CompletedAtUtc,
        LastReviewedAtUtc = p.LastReviewedAtUtc, UnderstandingLevel = p.UnderstandingLevel, LastAssessmentId = p.LastAssessmentId
    };
    private static StudentConceptMasteryDto Map(StudentConceptMastery m) => new()
    {
        Id = m.Id, BookId = m.BookId, ConceptKey = m.ConceptKey, LearningLevel = m.LearningLevel, ExposureCount = m.ExposureCount,
        AssessmentCount = m.AssessmentCount, CorrectAnswerCount = m.CorrectAnswerCount, DemonstratedContextCount = m.DemonstratedContextCount, MasteryScore = m.MasteryScore,
        LastAssessedAt = m.LastAssessedAt, FirstIntroducedLessonId = m.FirstIntroducedLessonId, LastAssessedLessonId = m.LastAssessedLessonId
    };
    private static ReviewRecommendationDto Map(ReviewRecommendation r) => new()
    {
        Id = r.Id, BookId = r.BookId, LessonId = r.LessonId, ConceptKey = r.ConceptKey, Reason = r.Reason,
        ReasonCode = r.ReasonCode, CurrentLearningLevel = r.CurrentLearningLevel, RecommendedAction = r.RecommendedAction,
        SuggestedQuestionType = r.SuggestedQuestionType, SourcePages = ParsePages(r.SourcePagesCsv), Priority = r.Priority, GeneratedAtUtc = r.GeneratedAtUtc
    };
    private static List<int> ParsePages(string pages) => pages.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(value => int.TryParse(value, out var page) ? page : 0).Where(page => page > 0).ToList();
    private static string Normalize(string value) => value.Trim().Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا").Replace("ة", "ه").Replace("ى", "ي");
}
