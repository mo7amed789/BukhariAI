using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Infrastructure.Persistence;

public sealed class AssessmentService : IAssessmentService
{
    private readonly BukhariDbContext _dbContext;
    private readonly IAssessmentEvaluator _evaluator;
    private readonly IMasteryEngineService _masteryEngine;
    private readonly IStudentLearningService? _studentLearning;
    private readonly ILogger<AssessmentService> _logger;

    public AssessmentService(
        BukhariDbContext dbContext,
        IAssessmentEvaluator evaluator,
        IMasteryEngineService masteryEngine,
        ILogger<AssessmentService> logger,
        IStudentLearningService? studentLearning = null)
    {
        _dbContext = dbContext;
        _evaluator = evaluator;
        _masteryEngine = masteryEngine;
        _studentLearning = studentLearning;
        _logger = logger;
    }

    public Task<SubmitStudentAnswerResponse> SubmitAnswerAsync(
        SubmitStudentAnswerRequest request,
        CancellationToken cancellationToken = default)
        => SubmitAnswerAsync(request, null, cancellationToken);

    public Task<AssessmentDetailDto?> GetAssessmentByIdAsync(
        Guid assessmentId,
        CancellationToken cancellationToken = default)
        => GetAssessmentByIdAsync(assessmentId, null, cancellationToken);

    public Task<List<AssessmentDetailDto>> GetAssessmentsByLessonIdAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
        => GetAssessmentsByLessonIdAsync(lessonId, null, cancellationToken);

    public Task<List<StudentConceptMasteryDto>> GetMasteryProfileByBookIdAsync(
        Guid bookId,
        CancellationToken cancellationToken = default)
        => GetMasteryProfileByBookIdAsync(bookId, null, cancellationToken);

    public async Task<SubmitStudentAnswerResponse> SubmitAnswerAsync(
        SubmitStudentAnswerRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StudentAnswer);

        var targetUserId = userId ?? User.DefaultUserId;

        var question = await _dbContext.Set<AssessmentQuestion>()
            .Include(q => q.Assessment)
                .ThenInclude(a => a.Book)
            .Include(q => q.SourceLesson)
                .ThenInclude(l => l!.LessonPages)
                    .ThenInclude(lp => lp.BookPage)
            .Include(q => q.SourceLesson)
                .ThenInclude(l => l!.Hadiths)
            .FirstOrDefaultAsync(q => q.Id == request.QuestionId, cancellationToken);

        if (question == null)
        {
            throw new KeyNotFoundException($"Assessment question with ID '{request.QuestionId}' was not found.");
        }

        // An idempotency key protects learning evidence from accidental retries and race conditions.
        StudentAnswer? studentAnswer = null;
        if (!string.IsNullOrWhiteSpace(request.SubmissionId))
        {
            var existing = await _dbContext.Set<StudentAnswer>()
                .Include(a => a.AssessmentResult)
                .FirstOrDefaultAsync(a => a.UserId == targetUserId && a.AssessmentQuestionId == question.Id && a.SubmissionId == request.SubmissionId.Trim(), cancellationToken);

            if (existing != null)
            {
                if (existing.LifecycleStatus == EvaluationLifecycleStatus.Completed && existing.AssessmentResult != null)
                {
                    return await BuildExistingResponseAsync(existing, question.Assessment.BookId, targetUserId, cancellationToken);
                }

                if (existing.LifecycleStatus == EvaluationLifecycleStatus.Pending && existing.SubmittedAtUtc > DateTime.UtcNow.AddMinutes(-2))
                {
                    throw new InvalidOperationException("طلب تقييم الإجابة قيد المعالجة حالياً. يرجى الانتظار للحصول على النتيجة.");
                }

                // If prior attempt failed or timed out, reuse the existing reservation
                studentAnswer = existing;
                studentAnswer.LifecycleStatus = EvaluationLifecycleStatus.Pending;
                studentAnswer.AnswerText = request.StudentAnswer.Trim();
                studentAnswer.SubmittedAtUtc = DateTime.UtcNow;
                studentAnswer.ErrorMessage = null;
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        var bookId = question.Assessment.BookId;
        var lesson = question.SourceLesson;

        // 1. Reserve StudentAnswer atomically with Pending state if new
        if (studentAnswer == null)
        {
            studentAnswer = new StudentAnswer
            {
                Id = Guid.NewGuid(),
                UserId = targetUserId,
                AssessmentQuestionId = question.Id,
                AnswerText = request.StudentAnswer.Trim(),
                SubmissionId = string.IsNullOrWhiteSpace(request.SubmissionId) ? null : request.SubmissionId.Trim(),
                LifecycleStatus = EvaluationLifecycleStatus.Pending,
                SubmittedAtUtc = DateTime.UtcNow
            };
            _dbContext.Set<StudentAnswer>().Add(studentAnswer);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException) when (!string.IsNullOrWhiteSpace(request.SubmissionId))
            {
                // Concurrency race: another parallel request reserved or completed the submission
                var conflictRecord = await _dbContext.Set<StudentAnswer>()
                    .Include(a => a.AssessmentResult)
                    .FirstOrDefaultAsync(a => a.UserId == targetUserId && a.AssessmentQuestionId == question.Id && a.SubmissionId == request.SubmissionId.Trim(), cancellationToken);

                if (conflictRecord?.AssessmentResult != null)
                {
                    return await BuildExistingResponseAsync(conflictRecord, bookId, targetUserId, cancellationToken);
                }

                throw new InvalidOperationException("طلب تقييم الإجابة قيد المعالجة في طلب متزامن آخر.");
            }
        }

        // 2. Build Evaluation Input
        string originalSourceText = string.Empty;
        string lessonExplanation = string.Empty;

        if (lesson != null)
        {
            var explanations = new List<string> { lesson.Title, lesson.Overview };
            foreach (var h in lesson.Hadiths.Take(2))
            {
                explanations.Add($"المسألة: {h.Reference}\nالمشكلة: {h.Problem}\nالاستدلال: {h.Reasoning}\nالنتيجة: {h.Conclusion}");
            }
            lessonExplanation = string.Join("\n\n", explanations);
        }

        var expectedConcepts = ParseCsvList(question.ExpectedConceptsCsv);

        // Get relevant student mastery context
        var currentMasteries = await _masteryEngine.GetStudentMasteryByBookIdAsync(bookId, targetUserId, cancellationToken);
        var relevantMasteryItems = currentMasteries
            .Where(m => expectedConcepts.Any(ec => ec.Contains(m.ConceptKey, StringComparison.OrdinalIgnoreCase) || m.ConceptKey.Contains(ec, StringComparison.OrdinalIgnoreCase)))
            .Select(m => new StudentConceptMasteryItem
            {
                Concept = m.ConceptKey,
                Level = m.LearningLevel,
                Score = m.MasteryScore,
                ExposureCount = m.ExposureCount,
                AssessmentCount = m.AssessmentCount
            })
            .ToList();

        var evalInput = new AssessmentEvaluationInput
        {
            OriginalSourceText = originalSourceText,
            LessonExplanation = lessonExplanation,
            Question = question.Question,
            QuestionType = question.QuestionType,
            Difficulty = question.Difficulty,
            ExpectedConcepts = expectedConcepts,
            EvaluationGuidance = question.EvaluationGuidance,
            StudentAnswer = request.StudentAnswer,
            RelevantStudentMastery = relevantMasteryItems
        };

        // 3. AI Evaluation outside database transaction
        _logger.LogInformation("Calling AI evaluator for question '{Question}' (Student: {UserId}, Answer length: {Length}).",
            question.Question, targetUserId, request.StudentAnswer.Length);

        AssessmentEvaluationResult evalResult;
        try
        {
            evalResult = await _evaluator.EvaluateAnswerAsync(evalInput, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI evaluation failed for student answer {AnswerId}.", studentAnswer.Id);
            studentAnswer.LifecycleStatus = EvaluationLifecycleStatus.Failed;
            studentAnswer.ErrorMessage = ex.Message;
            await _dbContext.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        // 4. Create AssessmentResult and mark Completed
        studentAnswer.LifecycleStatus = EvaluationLifecycleStatus.Completed;
        studentAnswer.ErrorMessage = null;

        var assessmentResult = studentAnswer.AssessmentResult;
        if (assessmentResult == null)
        {
            assessmentResult = new AssessmentResult
            {
                Id = Guid.NewGuid(),
                StudentAnswerId = studentAnswer.Id,
                Score = evalResult.Score,
                AnswerStatus = DetermineAnswerStatus(evalResult),
                Level = evalResult.Level,
                UnderstoodConceptsJson = JsonSerializer.Serialize(evalResult.UnderstoodConcepts),
                MissingConceptsJson = JsonSerializer.Serialize(evalResult.MissingConcepts),
                MisconceptionsJson = JsonSerializer.Serialize(evalResult.Misconceptions),
                Feedback = evalResult.Feedback,
                EvaluatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Set<AssessmentResult>().Add(assessmentResult);
        }
        else
        {
            assessmentResult.Score = evalResult.Score;
            assessmentResult.AnswerStatus = DetermineAnswerStatus(evalResult);
            assessmentResult.Level = evalResult.Level;
            assessmentResult.UnderstoodConceptsJson = JsonSerializer.Serialize(evalResult.UnderstoodConcepts);
            assessmentResult.MissingConceptsJson = JsonSerializer.Serialize(evalResult.MissingConcepts);
            assessmentResult.MisconceptionsJson = JsonSerializer.Serialize(evalResult.Misconceptions);
            assessmentResult.Feedback = evalResult.Feedback;
            assessmentResult.EvaluatedAtUtc = DateTime.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // 5. Update StudentConceptMastery for all evaluated & expected concepts
        var allTargetConcepts = expectedConcepts
            .Concat(evalResult.UnderstoodConcepts)
            .Concat(evalResult.MissingConcepts)
            .Concat(evalResult.Misconceptions)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allTargetConcepts.Count == 0 && lesson != null)
        {
            allTargetConcepts = lesson.Hadiths.Select(h => h.Problem).Where(p => !string.IsNullOrWhiteSpace(p)).Take(2).ToList();
        }

        var updatedMasteries = await _masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId,
            lesson?.Id,
            allTargetConcepts,
            evalResult,
            targetUserId,
            cancellationToken);

        if (lesson is not null && _studentLearning is not null)
        {
            await _studentLearning.RefreshLessonProgressAfterAssessmentAsync(lesson.Id, question.AssessmentId, targetUserId, cancellationToken);
        }

        var masteryDtos = updatedMasteries.Select(m => new StudentConceptMasteryDto
        {
            Id = m.Id,
            BookId = m.BookId,
            ConceptKey = m.ConceptKey,
            LearningLevel = m.LearningLevel,
            ExposureCount = m.ExposureCount,
            AssessmentCount = m.AssessmentCount,
            CorrectAnswerCount = m.CorrectAnswerCount,
            DemonstratedContextCount = m.DemonstratedContextCount,
            MasteryScore = m.MasteryScore,
            LastAssessedAt = m.LastAssessedAt,
            FirstIntroducedLessonId = m.FirstIntroducedLessonId,
            LastAssessedLessonId = m.LastAssessedLessonId
        }).ToList();

        return new SubmitStudentAnswerResponse
        {
            AnswerId = studentAnswer.Id,
            ResultId = assessmentResult.Id,
            Score = assessmentResult.Score,
            AnswerStatus = assessmentResult.AnswerStatus,
            Level = assessmentResult.Level,
            UnderstoodConcepts = evalResult.UnderstoodConcepts,
            MissingConcepts = evalResult.MissingConcepts,
            Misconceptions = evalResult.Misconceptions,
            Feedback = assessmentResult.Feedback,
            UpdatedMasteries = masteryDtos,
            EvaluatedAtUtc = assessmentResult.EvaluatedAtUtc
        };
    }

    public async Task<AssessmentDetailDto?> GetAssessmentByIdAsync(
        Guid assessmentId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var assessment = await _dbContext.Set<Assessment>()
            .AsNoTracking()
            .Include(a => a.Questions)
                .ThenInclude(q => q.StudentAnswers.Where(sa => sa.UserId == targetUserId))
                    .ThenInclude(sa => sa.AssessmentResult)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, cancellationToken);

        if (assessment == null) return null;

        return MapAssessmentToDto(assessment);
    }

    public async Task<List<AssessmentDetailDto>> GetAssessmentsByLessonIdAsync(
        Guid lessonId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var assessments = await _dbContext.Set<Assessment>()
            .AsNoTracking()
            .Where(a => a.LessonId == lessonId)
            .Include(a => a.Questions)
                .ThenInclude(q => q.StudentAnswers.Where(sa => sa.UserId == targetUserId))
                    .ThenInclude(sa => sa.AssessmentResult)
            .OrderBy(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return assessments.Select(MapAssessmentToDto).ToList();
    }

    public async Task<List<StudentConceptMasteryDto>> GetMasteryProfileByBookIdAsync(
        Guid bookId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var targetUserId = userId ?? User.DefaultUserId;
        var masteries = await _masteryEngine.GetStudentMasteryByBookIdAsync(bookId, targetUserId, cancellationToken);

        return masteries.Select(m => new StudentConceptMasteryDto
        {
            Id = m.Id,
            BookId = m.BookId,
            ConceptKey = m.ConceptKey,
            LearningLevel = m.LearningLevel,
            ExposureCount = m.ExposureCount,
            AssessmentCount = m.AssessmentCount,
            CorrectAnswerCount = m.CorrectAnswerCount,
            DemonstratedContextCount = m.DemonstratedContextCount,
            MasteryScore = m.MasteryScore,
            LastAssessedAt = m.LastAssessedAt,
            FirstIntroducedLessonId = m.FirstIntroducedLessonId,
            LastAssessedLessonId = m.LastAssessedLessonId
        }).ToList();
    }

    private static AssessmentDetailDto MapAssessmentToDto(Assessment assessment)
    {
        return new AssessmentDetailDto
        {
            Id = assessment.Id,
            BookId = assessment.BookId,
            LessonId = assessment.LessonId,
            Title = assessment.Title,
            CreatedAtUtc = assessment.CreatedAtUtc,
            Questions = assessment.Questions.Select(q => new AssessmentQuestionDetailDto
            {
                Id = q.Id,
                AssessmentId = q.AssessmentId,
                SourceLessonId = q.SourceLessonId,
                Question = q.Question,
                QuestionType = q.QuestionType,
                Difficulty = q.Difficulty,
                ExpectedConcepts = ParseCsvList(q.ExpectedConceptsCsv),
                SourcePages = ParseIntCsvList(q.SourcePagesCsv),
                EvaluationGuidance = q.EvaluationGuidance,
                Answers = q.StudentAnswers.Select(sa => new StudentAnswerDetailDto
                {
                    Id = sa.Id,
                    AnswerText = sa.AnswerText,
                    SubmittedAtUtc = sa.SubmittedAtUtc,
                    Score = sa.AssessmentResult?.Score,
                    AnswerStatus = sa.AssessmentResult?.AnswerStatus,
                    Level = sa.AssessmentResult?.Level,
                    Feedback = sa.AssessmentResult?.Feedback,
                    UnderstoodConcepts = sa.AssessmentResult != null ? DeserializeStringList(sa.AssessmentResult.UnderstoodConceptsJson) : [],
                    MissingConcepts = sa.AssessmentResult != null ? DeserializeStringList(sa.AssessmentResult.MissingConceptsJson) : [],
                    Misconceptions = sa.AssessmentResult != null ? DeserializeStringList(sa.AssessmentResult.MisconceptionsJson) : []
                }).ToList()
            }).ToList()
        };
    }

    private static List<string> ParseCsvList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static List<int> ParseIntCsvList(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => int.TryParse(s, out int val) ? val : (int?)null)
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();
    }

    private static List<string> DeserializeStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private async Task<SubmitStudentAnswerResponse> BuildExistingResponseAsync(StudentAnswer answer, Guid bookId, Guid targetUserId, CancellationToken cancellationToken)
    {
        var result = answer.AssessmentResult!;
        return new SubmitStudentAnswerResponse
        {
            AnswerId = answer.Id,
            ResultId = result.Id,
            Score = result.Score,
            AnswerStatus = result.AnswerStatus,
            Level = result.Level,
            UnderstoodConcepts = DeserializeStringList(result.UnderstoodConceptsJson),
            MissingConcepts = DeserializeStringList(result.MissingConceptsJson),
            Misconceptions = DeserializeStringList(result.MisconceptionsJson),
            Feedback = result.Feedback,
            UpdatedMasteries = await GetMasteryProfileByBookIdAsync(bookId, targetUserId, cancellationToken),
            EvaluatedAtUtc = result.EvaluatedAtUtc
        };
    }

    private static AssessmentAnswerStatus DetermineAnswerStatus(AssessmentEvaluationResult result)
    {
        if (result.Score >= .80 && result.Misconceptions.Count == 0 && result.MissingConcepts.Count == 0)
            return AssessmentAnswerStatus.Correct;
        if (result.Score >= .40 || result.UnderstoodConcepts.Count > 0)
            return AssessmentAnswerStatus.PartiallyCorrect;
        if (result.Misconceptions.Count > 0)
            return AssessmentAnswerStatus.Incorrect;
        return AssessmentAnswerStatus.InsufficientEvidence;
    }
}
