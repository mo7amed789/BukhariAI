using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Infrastructure.Mastery;

public sealed class MasteryEngineService : IMasteryEngineService
{
    private readonly BukhariDbContext _dbContext;
    private readonly ILogger<MasteryEngineService> _logger;

    public MasteryEngineService(
        BukhariDbContext dbContext,
        ILogger<MasteryEngineService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public Task<List<StudentConceptMastery>> UpdateMasteryAfterAssessmentAsync(
        Guid bookId,
        Guid? lessonId,
        List<string> expectedConcepts,
        AssessmentEvaluationResult evaluationResult,
        CancellationToken cancellationToken = default)
        => UpdateMasteryAfterAssessmentAsync(bookId, lessonId, expectedConcepts, evaluationResult, null, cancellationToken);

    public Task<List<StudentConceptMastery>> RecordConceptExposuresAsync(
        Guid bookId,
        Guid lessonId,
        IEnumerable<string> conceptKeys,
        CancellationToken cancellationToken = default)
        => RecordConceptExposuresAsync(bookId, lessonId, conceptKeys, null, cancellationToken);

    public Task<List<StudentConceptMastery>> GetStudentMasteryByBookIdAsync(
        Guid bookId,
        CancellationToken cancellationToken = default)
        => GetStudentMasteryByBookIdAsync(bookId, null, cancellationToken);

    public async Task<List<StudentConceptMastery>> UpdateMasteryAfterAssessmentAsync(
        Guid bookId,
        Guid? lessonId,
        List<string> expectedConcepts,
        AssessmentEvaluationResult evaluationResult,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedConcepts);
        ArgumentNullException.ThrowIfNull(evaluationResult);

        var targetUserId = userId ?? User.DefaultUserId;

        // Gather all concepts to update (expected, understood, missing, misconceptions)
        var allConcepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in expectedConcepts)
        {
            string? clean = ConceptSanitizer.CleanAndValidate(c);
            if (clean != null) allConcepts.Add(clean);
        }
        foreach (var c in evaluationResult.UnderstoodConcepts)
        {
            string? clean = ConceptSanitizer.CleanAndValidate(c);
            if (clean != null) allConcepts.Add(clean);
        }
        foreach (var c in evaluationResult.MissingConcepts)
        {
            string? clean = ConceptSanitizer.CleanAndValidate(c);
            if (clean != null) allConcepts.Add(clean);
        }
        foreach (var c in evaluationResult.Misconceptions)
        {
            string? clean = ConceptSanitizer.CleanAndValidate(c);
            if (clean != null) allConcepts.Add(clean);
        }

        if (allConcepts.Count == 0)
        {
            return [];
        }

        var normalizedKeys = allConcepts.Select(NormalizeConceptKey).Distinct().ToList();

        var existingMasteries = await _dbContext.Set<StudentConceptMastery>()
            .Where(m => m.BookId == bookId && m.UserId == targetUserId)
            .ToListAsync(cancellationToken);

        var masteryMap = existingMasteries.ToDictionary(
            m => NormalizeConceptKey(m.ConceptKey),
            StringComparer.OrdinalIgnoreCase);

        var updatedMasteries = new List<StudentConceptMastery>();

        foreach (string validConcept in allConcepts)
        {
            string normalizedKey = NormalizeConceptKey(validConcept);

            if (!masteryMap.TryGetValue(normalizedKey, out var mastery))
            {
                mastery = new StudentConceptMastery
                {
                    Id = Guid.NewGuid(),
                    UserId = targetUserId,
                    BookId = bookId,
                    ConceptKey = validConcept,
                    LearningLevel = LearningLevel.Introduced,
                    ExposureCount = 1,
                    AssessmentCount = 0,
                    CorrectAnswerCount = 0,
                    MasteryScore = 0.20,
                    FirstIntroducedLessonId = lessonId,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.Set<StudentConceptMastery>().Add(mastery);
                masteryMap[normalizedKey] = mastery;
            }

            // Update assessment counts and timestamps
            var priorAssessedLessonId = mastery.LastAssessedLessonId;
            mastery.AssessmentCount++;
            mastery.LastAssessedAt = DateTime.UtcNow;
            if (lessonId.HasValue)
            {
                mastery.LastAssessedLessonId = lessonId.Value;
            }

            // Determine if this concept was understood in this assessment
            bool hasMisconception = evaluationResult.Misconceptions
                .Any(m => NormalizeConceptKey(m).Contains(normalizedKey) || normalizedKey.Contains(NormalizeConceptKey(m)));

            bool isExplicitlyUnderstood = evaluationResult.UnderstoodConcepts
                .Any(u => NormalizeConceptKey(u).Contains(normalizedKey) || normalizedKey.Contains(NormalizeConceptKey(u)));

            bool isMissing = evaluationResult.MissingConcepts
                .Any(ms => NormalizeConceptKey(ms).Contains(normalizedKey) || normalizedKey.Contains(NormalizeConceptKey(ms)));

            bool isDemonstrated = (isExplicitlyUnderstood || (evaluationResult.Score >= 0.65 && !hasMisconception && !isMissing));

            if (isDemonstrated)
            {
                mastery.CorrectAnswerCount++;
                if (lessonId.HasValue && priorAssessedLessonId != lessonId)
                {
                    mastery.DemonstratedContextCount++;
                }

                // Evidence Accumulation: Increase mastery score asymptotically towards 1.0
                double gainFactor = 0.40 * Math.Max(0.5, evaluationResult.Score);
                mastery.MasteryScore += (1.0 - mastery.MasteryScore) * gainFactor;
            }
            else if (hasMisconception)
            {
                // Misconception reduces confidence moderately while preserving history
                mastery.MasteryScore = Math.Max(0.10, mastery.MasteryScore * 0.65 - 0.05);
            }
            else if (isMissing || evaluationResult.Score < 0.40)
            {
                // Missing concept decays confidence slightly
                mastery.MasteryScore = Math.Max(0.10, mastery.MasteryScore * 0.80);
            }

            mastery.MasteryScore = Math.Clamp(mastery.MasteryScore, 0.0, 1.0);
            mastery.LearningLevel = CalculateMasteryLevel(mastery.MasteryScore, mastery.CorrectAnswerCount, mastery.DemonstratedContextCount);
            mastery.UpdatedAtUtc = DateTime.UtcNow;

            updatedMasteries.Add(mastery);

            _logger.LogInformation(
                "Updated StudentConceptMastery for '{Concept}' (Book: {BookId}): Score={Score:F2}, Level={Level}, Assessed={AssessedCount}, Correct={CorrectCount}.",
                mastery.ConceptKey,
                bookId,
                mastery.MasteryScore,
                mastery.LearningLevel,
                mastery.AssessmentCount,
                mastery.CorrectAnswerCount);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return updatedMasteries;
    }

    public async Task<List<StudentConceptMastery>> RecordConceptExposuresAsync(
        Guid bookId,
        Guid lessonId,
        IEnumerable<string> conceptKeys,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conceptKeys);

        var targetUserId = userId ?? User.DefaultUserId;

        var existingMasteries = await _dbContext.Set<StudentConceptMastery>()
            .Where(m => m.BookId == bookId && m.UserId == targetUserId)
            .ToListAsync(cancellationToken);

        var masteryMap = existingMasteries.ToDictionary(
            m => NormalizeConceptKey(m.ConceptKey),
            StringComparer.OrdinalIgnoreCase);

        var result = new List<StudentConceptMastery>();

        foreach (var rawConcept in conceptKeys)
        {
            string? validConcept = ConceptSanitizer.CleanAndValidate(rawConcept);
            if (validConcept == null) continue;

            string normalizedKey = NormalizeConceptKey(validConcept);

            if (masteryMap.TryGetValue(normalizedKey, out var mastery))
            {
                mastery.ExposureCount++;
                mastery.UpdatedAtUtc = DateTime.UtcNow;
                result.Add(mastery);
            }
            else
            {
                var newMastery = new StudentConceptMastery
                {
                    Id = Guid.NewGuid(),
                    UserId = targetUserId,
                    BookId = bookId,
                    ConceptKey = validConcept,
                    LearningLevel = LearningLevel.Introduced,
                    ExposureCount = 1,
                    AssessmentCount = 0,
                    CorrectAnswerCount = 0,
                    MasteryScore = 0.20,
                    FirstIntroducedLessonId = lessonId,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                _dbContext.Set<StudentConceptMastery>().Add(newMastery);
                masteryMap[normalizedKey] = newMastery;
                result.Add(newMastery);
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<List<StudentConceptMastery>> GetStudentMasteryByBookIdAsync(
        Guid bookId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;

        return await _dbContext.Set<StudentConceptMastery>()
            .AsNoTracking()
            .Where(m => m.BookId == bookId && m.UserId == targetUserId)
            .OrderByDescending(m => m.MasteryScore)
            .ThenByDescending(m => m.ExposureCount)
            .ToListAsync(cancellationToken);
    }

    public static LearningLevel CalculateMasteryLevel(double score, int correctAnswerCount, int demonstratedContextCount = 0)
    {
        // Rule: Mastered requires high score AND at least 3 consistent demonstrated correct answers
        if (score >= 0.85 && correctAnswerCount >= 3 && demonstratedContextCount >= 3)
        {
            return LearningLevel.Mastered;
        }

        // Rule: Understood requires solid score AND at least 2 demonstrated correct answers
        if (score >= 0.65 && correctAnswerCount >= 2)
        {
            return LearningLevel.Understood;
        }

        // Rule: Familiar requires moderate score AND at least 1 demonstrated correct answer
        if (score >= 0.40 && correctAnswerCount >= 1)
        {
            return LearningLevel.Familiar;
        }

        return LearningLevel.Introduced;
    }

    private static string NormalizeConceptKey(string text)
    {
        return text.Trim()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ة", "ه")
            .Replace("ى", "ي");
    }
}
