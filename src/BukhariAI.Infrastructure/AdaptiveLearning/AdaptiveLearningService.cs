using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.AdaptiveLearning;
using BukhariAI.Application.Assessments;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Infrastructure.AdaptiveLearning;

/// <summary>
/// Authoritative, deterministic tutoring policy and interactive review engine.
/// Provides deep concept reinforcement, spaced-repetition flashcards, mastery matrix, and quick review quizzes.
/// </summary>
public sealed class AdaptiveLearningService : IAdaptiveLearningService
{
    private readonly BukhariDbContext _db;
    private readonly IAssessmentEvaluator _evaluator;
    private readonly IMasteryEngineService _masteryEngine;
    private readonly ILogger<AdaptiveLearningService> _logger;

    public AdaptiveLearningService(
        BukhariDbContext db,
        ILogger<AdaptiveLearningService> logger)
        : this(db, null!, null!, logger)
    {
    }

    public AdaptiveLearningService(
        BukhariDbContext db,
        IAssessmentEvaluator evaluator,
        IMasteryEngineService masteryEngine,
        ILogger<AdaptiveLearningService> logger)
    {
        _db = db;
        _evaluator = evaluator;
        _masteryEngine = masteryEngine;
        _logger = logger;
    }

    public async Task<AdaptiveLearningStateDto> GetAdaptiveStateAsync(Guid bookId, LearningIntent intent = LearningIntent.ContinueLearning, CancellationToken cancellationToken = default)
    {
        var masteries = await _db.StudentConceptMasteries.AsNoTracking()
            .Where(m => m.BookId == bookId).ToListAsync(cancellationToken);
        var states = masteries.Select(m => AdaptiveLearningPolicy.Evaluate(m, DateTime.UtcNow)).ToList();
        _logger.LogInformation("Evaluated adaptive learning state for BookId {BookId}: {TotalCount} concepts ({DueCount} due, {WeakCount} weak).",
            bookId, states.Count, states.Count(s => s.IsReviewDue), states.Count(s => s.IsWeak));

        foreach (var state in states)
        {
            _logger.LogDebug("AdaptiveDecision: Concept={Concept}, PreviousLevel={Level}, Confidence={Confidence:F2}, Action={Action}, Reason={ReasonCode}",
                state.ConceptKey, state.LearningLevel, state.Confidence, state.Action, state.ReasonCode);
        }
        return new AdaptiveLearningStateDto { BookId = bookId, Intent = intent, Concepts = states };
    }

    public async Task<List<AdaptiveConceptState>> GetDueReviewsAsync(Guid bookId, CancellationToken cancellationToken = default) =>
        (await GetAdaptiveStateAsync(bookId, LearningIntent.ReviewDueConcepts, cancellationToken)).Concepts
            .Where(c => c.IsReviewDue).OrderByDescending(c => c.Confidence).ToList();

    public async Task<ConceptReinforcementSessionDto> GenerateConceptReinforcementAsync(
        Guid bookId,
        GenerateReinforcementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conceptKey = (request.ConceptKey ?? string.Empty).Trim();

        var mastery = await _db.StudentConceptMasteries.AsNoTracking()
            .FirstOrDefaultAsync(m => m.BookId == bookId && m.ConceptKey == conceptKey, cancellationToken);

        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.BookId == bookId)
            .Include(l => l.Hadiths).ThenInclude(h => h.Evidences)
            .Include(l => l.LessonPages)
            .Include(l => l.ReviewQuestions)
            .ToListAsync(cancellationToken);

        // Find best matching lesson
        var targetLesson = (request.LessonId.HasValue ? lessons.FirstOrDefault(l => l.Id == request.LessonId.Value) : null)
            ?? (mastery?.LastAssessedLessonId.HasValue == true ? lessons.FirstOrDefault(l => l.Id == mastery.LastAssessedLessonId.Value) : null)
            ?? (mastery?.FirstIntroducedLessonId.HasValue == true ? lessons.FirstOrDefault(l => l.Id == mastery.FirstIntroducedLessonId.Value) : null)
            ?? lessons.FirstOrDefault(l => l.Hadiths.Any(h => h.Problem.Contains(conceptKey, StringComparison.OrdinalIgnoreCase) || h.Conclusion.Contains(conceptKey, StringComparison.OrdinalIgnoreCase)))
            ?? lessons.FirstOrDefault();

        var state = mastery != null ? AdaptiveLearningPolicy.Evaluate(mastery, DateTime.UtcNow) : null;
        var currentLevel = state?.LearningLevel ?? LearningLevel.Introduced;
        var confidence = state?.Confidence ?? 0.35;

        // Extract context from lesson hadiths
        var matchingHadith = targetLesson?.Hadiths.FirstOrDefault(h =>
            h.Problem.Contains(conceptKey, StringComparison.OrdinalIgnoreCase) ||
            h.Reasoning.Contains(conceptKey, StringComparison.OrdinalIgnoreCase) ||
            h.Conclusion.Contains(conceptKey, StringComparison.OrdinalIgnoreCase))
            ?? targetLesson?.Hadiths.FirstOrDefault();

        var keyEvidence = matchingHadith?.Evidences.FirstOrDefault();
        var pages = targetLesson?.LessonPages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToList() ?? [];

        // Build pedagogical reinforcement details
        string title = ConceptSanitizer.CleanAndValidate(conceptKey)
            ?? (matchingHadith?.Problem != null ? ConceptSanitizer.DeriveTopicTitle(matchingHadith.Problem) : "المسألة الفقهية");
        string explanation = matchingHadith != null
            ? $"المسألة تدور حول {matchingHadith.Problem}. وخلاصة الحكم: {matchingHadith.Conclusion}. ووجه الاستدلال: {matchingHadith.Reasoning}"
            : $"هذا المفهوم يتناول المسائل المتعلقة بـ ({title}) وبيان أحكامها الشرعية من واقع نصوص المتن والأدلة.";

        string misconception = matchingHadith != null
            ? (!string.IsNullOrWhiteSpace(matchingHadith.ScholarlyDiscussion)
                ? matchingHadith.ScholarlyDiscussion
                : "الخلط الشائع يقع بين إطلاق الحكم وتخصيصه، أو بين طهارة العين واستعمالها في الرطب واليابس؛ والضابط هو مراعاة ورود الدليل المخصص.")
            : "الخلط الشائع يقع في عدم التفريق بين الأصل العام والاستثناء الوارد في الدليل الخاص.";

        string mnemonic = matchingHadith != null
            ? $"💡 ضابط الحفظ والتثبيت: تذكر الحديث «{(keyEvidence?.Text?.Length > 40 ? keyEvidence.Text[..40] + "..." : keyEvidence?.Text ?? matchingHadith.Conclusion)}» لربط المسألة بحكمها فوراً."
            : $"💡 ضابط الحفظ: اربط المفهوم بدليله الخاص وقاعدته الفقهية لترسيخ الفهم ومنع التداخل.";

        string question = matchingHadith != null
            ? $"بيّن باختصار الحكم المستفاد في مسألة ({title}) مع ذكر وجه الاستدلال بالدليل النبوي."
            : $"اشرح مفهوم ({title}) وبيّن كيفية الاستدلال عليه في سياق هذا الباب.";

        return new ConceptReinforcementSessionDto
        {
            BookId = bookId,
            ConceptKey = conceptKey,
            CurrentLevel = currentLevel,
            Confidence = confidence,
            ConceptTitle = title,
            SimplifiedExplanation = explanation,
            KeyEvidenceText = keyEvidence?.Text,
            KeyEvidenceRole = keyEvidence?.Role ?? "دليل واستدلال",
            CoreMisconceptionClarification = misconception,
            MnemonicOrMemoryAnchor = mnemonic,
            VerificationQuestion = question,
            QuestionGuidance = "أجب بأسلوبك مع التركيز على وجه الدلالة واستحضار الدليل.",
            QuestionType = "Understanding",
            SourceLessonId = targetLesson?.Id,
            LessonTitle = targetLesson?.Title,
            SourcePages = pages
        };
    }

    public async Task<ReinforcementEvaluationResultDto> EvaluateReinforcementAnswerAsync(
        Guid bookId,
        SubmitReinforcementAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conceptKey = (request.ConceptKey ?? string.Empty).Trim();
        var studentAnswer = (request.StudentAnswer ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(studentAnswer))
        {
            throw new ArgumentException("Student answer cannot be empty.", nameof(request));
        }

        var mastery = await _db.StudentConceptMasteries
            .FirstOrDefaultAsync(m => m.BookId == bookId && m.ConceptKey == conceptKey, cancellationToken);

        if (mastery == null)
        {
            mastery = new StudentConceptMastery
            {
                Id = Guid.NewGuid(),
                BookId = bookId,
                ConceptKey = conceptKey,
                LearningLevel = LearningLevel.Introduced,
                ExposureCount = 1,
                AssessmentCount = 0,
                CorrectAnswerCount = 0,
                DemonstratedContextCount = 1,
                MasteryScore = 0.35,
                FirstIntroducedLessonId = request.LessonId
            };
            _db.StudentConceptMasteries.Add(mastery);
        }

        // AI evaluation input
        var evalInput = new AssessmentEvaluationInput
        {
            OriginalSourceText = $"المفهوم المستهدف بالتثبيت: {conceptKey}",
            LessonExplanation = $"جلسة تثبيت وتقوية للمفهوم الشرعي: {conceptKey}",
            Question = $"اشرح مفهوم ({conceptKey}) وبيّن وجه الاستدلال والحكم الشرعي المرتبط به.",
            QuestionType = "Understanding",
            Difficulty = "Intermediate",
            ExpectedConcepts = [conceptKey],
            EvaluationGuidance = "قيّم فهم الطالب للمفهوم وخلو إجابته من اللبس ومطابقتها لوجه الاستدلال.",
            StudentAnswer = studentAnswer,
            RelevantStudentMastery = [new StudentConceptMasteryItem { Concept = conceptKey, Level = mastery.LearningLevel, Score = mastery.MasteryScore }]
        };

        var evalResult = await _evaluator.EvaluateAnswerAsync(evalInput, cancellationToken);

        // Update Mastery in DB
        mastery.AssessmentCount++;
        mastery.LastAssessedAt = DateTime.UtcNow;
        if (request.LessonId.HasValue) mastery.LastAssessedLessonId = request.LessonId;

        bool isPassed = evalResult.Score >= 0.60;
        if (isPassed)
        {
            mastery.CorrectAnswerCount++;
            mastery.DemonstratedContextCount = Math.Max(mastery.DemonstratedContextCount, 2);
            mastery.MasteryScore = Math.Clamp(mastery.MasteryScore + 0.30, 0.65, 1.0);
            mastery.LearningLevel = mastery.MasteryScore >= 0.85 && mastery.CorrectAnswerCount >= 2
                ? LearningLevel.Mastered
                : LearningLevel.Understood;

            // Resolve any open ReviewRecommendation for this concept
            var recs = await _db.ReviewRecommendations
                .Where(r => r.BookId == bookId && r.ConceptKey == conceptKey && r.ReviewedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var r in recs)
            {
                r.ReviewedAtUtc = DateTime.UtcNow;
            }
        }
        else
        {
            mastery.MasteryScore = Math.Clamp(mastery.MasteryScore - 0.05, 0.1, 0.6);
            if (mastery.LearningLevel == LearningLevel.Unknown) mastery.LearningLevel = LearningLevel.Introduced;
        }

        await _db.SaveChangesAsync(cancellationToken);

        var updatedDecision = AdaptiveLearningPolicy.Evaluate(mastery, DateTime.UtcNow);

        return new ReinforcementEvaluationResultDto
        {
            ConceptKey = conceptKey,
            Score = evalResult.Score,
            NewLevel = mastery.LearningLevel,
            NewConfidence = updatedDecision.Confidence,
            IsMasteredOrUnderstood = isPassed,
            Feedback = evalResult.Feedback,
            UnderstoodPoints = evalResult.UnderstoodConcepts,
            AdvicePoints = evalResult.MissingConcepts.Concat(evalResult.Misconceptions).ToList()
        };
    }

    public async Task<List<ReviewFlashcardDto>> GetReviewDeckAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var masteries = await _db.StudentConceptMasteries.AsNoTracking()
            .Where(m => m.BookId == bookId).ToListAsync(cancellationToken);

        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.BookId == bookId)
            .Include(l => l.Hadiths).ThenInclude(h => h.Evidences)
            .Include(l => l.LessonPages)
            .Include(l => l.ReviewQuestions)
            .ToListAsync(cancellationToken);

        var deck = new List<ReviewFlashcardDto>();
        var now = DateTime.UtcNow;

        // 1. Cards from Concepts & Masteries
        foreach (var m in masteries)
        {
            var decision = AdaptiveLearningPolicy.Evaluate(m, now);
            var lesson = lessons.FirstOrDefault(l => l.Id == (m.LastAssessedLessonId ?? m.FirstIntroducedLessonId)) ?? lessons.FirstOrDefault();
            var hadith = lesson?.Hadiths.FirstOrDefault(h => h.Problem.Contains(m.ConceptKey, StringComparison.OrdinalIgnoreCase) || h.Conclusion.Contains(m.ConceptKey, StringComparison.OrdinalIgnoreCase)) ?? lesson?.Hadiths.FirstOrDefault();

            deck.Add(new ReviewFlashcardDto
            {
                Id = $"c_{m.Id}",
                ConceptKey = m.ConceptKey,
                Category = "مسألة فقهية",
                FrontText = $"ما الحكم والضابط في مسألة: «{m.ConceptKey}»؟",
                FrontSubtitle = lesson != null ? $"من درس: {lesson.Title}" : "مسألة علمية",
                BackTitle = m.ConceptKey,
                BackExplanation = hadith?.Conclusion ?? $"المسألة تتناول أحكام ({m.ConceptKey}) وفق نصوص المتن والأدلة المعتمدة.",
                EvidenceSnippet = hadith?.Evidences.FirstOrDefault()?.Text,
                MemoryTip = hadith != null ? $"وجه الاستدلال: {hadith.Reasoning}" : null,
                CurrentLevel = decision.LearningLevel,
                Confidence = decision.Confidence,
                IsWeak = decision.IsWeak,
                IsReviewDue = decision.IsReviewDue,
                SourceLessonId = lesson?.Id,
                LessonTitle = lesson?.Title,
                SourcePages = lesson?.LessonPages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToList() ?? []
            });
        }

        // 2. Cards from Lessons (Hadith evidence & core rulings)
        foreach (var lesson in lessons)
        {
            var pages = lesson.LessonPages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToList();
            foreach (var hadith in lesson.Hadiths)
            {
                var evidence = hadith.Evidences.FirstOrDefault();
                if (evidence != null && !deck.Any(d => d.FrontText.Contains(hadith.Reference ?? hadith.Problem)))
                {
                    deck.Add(new ReviewFlashcardDto
                    {
                        Id = $"h_{hadith.Id}",
                        ConceptKey = hadith.Problem,
                        Category = "دليل واستدلال",
                        FrontText = $"ما وجه الدلالة والحكم المستنبط من الحديث:\n«{evidence.Text}»؟",
                        FrontSubtitle = hadith.Reference ?? lesson.Title,
                        BackTitle = hadith.Problem,
                        BackExplanation = $"النتيجة الفقهية: {hadith.Conclusion}",
                        EvidenceSnippet = $"وجه الاستدلال: {hadith.Reasoning}",
                        MemoryTip = hadith.ScholarlyDiscussion,
                        CurrentLevel = LearningLevel.Understood,
                        Confidence = 0.75,
                        IsWeak = false,
                        IsReviewDue = false,
                        SourceLessonId = lesson.Id,
                        LessonTitle = lesson.Title,
                        SourcePages = pages
                    });
                }
            }

            // Self-review questions as flashcards
            foreach (var q in lesson.ReviewQuestions)
            {
                deck.Add(new ReviewFlashcardDto
                {
                    Id = $"q_{q.Id}",
                    ConceptKey = lesson.Title,
                    Category = "استرجاع نشط",
                    FrontText = q.Question,
                    FrontSubtitle = $"سؤال مدارسة — {lesson.Title}",
                    BackTitle = "خلاصة الجواب والتأصيل",
                    BackExplanation = lesson.Overview ?? "راجع الشرح والتفصيل في متن الدرس.",
                    EvidenceSnippet = lesson.Hadiths.FirstOrDefault()?.Conclusion,
                    MemoryTip = "استحضر أدلة المسألة وأقوال الأئمة في المسألة.",
                    CurrentLevel = LearningLevel.Familiar,
                    Confidence = 0.60,
                    IsWeak = false,
                    IsReviewDue = false,
                    SourceLessonId = lesson.Id,
                    LessonTitle = lesson.Title,
                    SourcePages = pages
                });
            }
        }

        // Order: Weak and due reviews first, then randomized / prioritized
        return deck.OrderByDescending(d => d.IsWeak).ThenByDescending(d => d.IsReviewDue).ThenBy(d => d.Confidence).ToList();
    }

    public async Task<FlashcardRatingResultDto> RateFlashcardAsync(
        Guid bookId,
        FlashcardRatingRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var conceptKey = (request.ConceptKey ?? string.Empty).Trim();
        var rating = (request.Rating ?? "Good").Trim();

        var mastery = await _db.StudentConceptMasteries
            .FirstOrDefaultAsync(m => m.BookId == bookId && m.ConceptKey == conceptKey, cancellationToken);

        if (mastery == null)
        {
            mastery = new StudentConceptMastery
            {
                Id = Guid.NewGuid(),
                BookId = bookId,
                ConceptKey = conceptKey,
                LearningLevel = LearningLevel.Familiar,
                ExposureCount = 1,
                AssessmentCount = 0,
                CorrectAnswerCount = 0,
                DemonstratedContextCount = 1,
                MasteryScore = 0.50
            };
            _db.StudentConceptMasteries.Add(mastery);
        }

        mastery.ExposureCount++;
        string scheduledText;

        if (string.Equals(rating, "Again", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(rating, "Hard", StringComparison.OrdinalIgnoreCase))
        {
            mastery.MasteryScore = Math.Clamp(mastery.MasteryScore - 0.10, 0.20, 1.0);
            scheduledText = "مقرر إعادة المراجعة خلال الجلسة الحالية";
        }
        else if (string.Equals(rating, "Easy", StringComparison.OrdinalIgnoreCase))
        {
            mastery.CorrectAnswerCount++;
            mastery.MasteryScore = Math.Clamp(mastery.MasteryScore + 0.15, 0.0, 1.0);
            if (mastery.MasteryScore >= 0.80) mastery.LearningLevel = LearningLevel.Mastered;
            else if (mastery.MasteryScore >= 0.60) mastery.LearningLevel = LearningLevel.Understood;
            scheduledText = "تم تثبيت المفهوم — المراجعة القادمة بعد أسبوعين";
        }
        else // Good
        {
            mastery.CorrectAnswerCount++;
            mastery.MasteryScore = Math.Clamp(mastery.MasteryScore + 0.08, 0.0, 1.0);
            if (mastery.MasteryScore >= 0.65) mastery.LearningLevel = LearningLevel.Understood;
            scheduledText = "جيد — المراجعة القادمة بعد ٧ أيام";
        }

        mastery.LastAssessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var decision = AdaptiveLearningPolicy.Evaluate(mastery, DateTime.UtcNow);

        return new FlashcardRatingResultDto
        {
            ConceptKey = conceptKey,
            NewLevel = mastery.LearningLevel,
            NewConfidence = decision.Confidence,
            NextReviewScheduled = scheduledText
        };
    }

    public async Task<List<MasteryMatrixItemDto>> GetMasteryMatrixAsync(Guid bookId, CancellationToken cancellationToken = default)
    {
        var masteries = await _db.StudentConceptMasteries.AsNoTracking()
            .Where(m => m.BookId == bookId).ToListAsync(cancellationToken);

        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.BookId == bookId)
            .Include(l => l.LessonPages)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var matrix = new List<MasteryMatrixItemDto>();

        foreach (var m in masteries)
        {
            var decision = AdaptiveLearningPolicy.Evaluate(m, now);
            var lesson = lessons.FirstOrDefault(l => l.Id == (m.LastAssessedLessonId ?? m.FirstIntroducedLessonId));
            var pages = lesson?.LessonPages.OrderBy(p => p.PageNumber).Select(p => p.PageNumber).ToList() ?? [];

            string statusText = decision.LearningLevel switch
            {
                LearningLevel.Mastered => "متقن ومستقر في الذاكرة التراكمية",
                LearningLevel.Understood => "مستوعب بالأدلة بشكل سليم",
                LearningLevel.Familiar when decision.IsWeak => "يحتاج إلى تثبيت وشرح بديل",
                LearningLevel.Familiar => "مألوف وقيد الترسيخ بالتقييم",
                _ => "مقدم حديثاً في المسار"
            };

            matrix.Add(new MasteryMatrixItemDto
            {
                Id = m.Id,
                ConceptKey = m.ConceptKey,
                Category = "مسألة فقهية",
                LearningLevel = decision.LearningLevel,
                Confidence = decision.Confidence,
                MasteryScore = m.MasteryScore,
                ExposureCount = m.ExposureCount,
                AssessmentCount = m.AssessmentCount,
                CorrectAnswerCount = m.CorrectAnswerCount,
                DemonstratedContextCount = m.DemonstratedContextCount,
                IsWeak = decision.IsWeak,
                IsReviewDue = decision.IsReviewDue,
                LastAssessedAt = m.LastAssessedAt,
                LessonId = lesson?.Id,
                LessonTitle = lesson?.Title,
                SourcePages = pages,
                StatusDescription = statusText
            });
        }

        return matrix.OrderByDescending(m => m.IsWeak)
            .ThenByDescending(m => m.IsReviewDue)
            .ThenBy(m => m.Confidence)
            .ToList();
    }

    public async Task<QuickReviewQuizDto> GenerateReviewQuizAsync(Guid bookId, int count = 3, CancellationToken cancellationToken = default)
    {
        var dueReviews = await GetDueReviewsAsync(bookId, cancellationToken);
        var state = await GetAdaptiveStateAsync(bookId, LearningIntent.ReinforceWeakConcepts, cancellationToken);
        var weakConcepts = state.Concepts.Where(c => c.IsWeak).ToList();

        var combinedConcepts = weakConcepts.Concat(dueReviews).DistinctBy(c => c.ConceptKey).Take(count).ToList();

        var lessons = await _db.Lessons.AsNoTracking()
            .Where(l => l.BookId == bookId)
            .Include(l => l.Hadiths)
            .Include(l => l.ReviewQuestions)
            .ToListAsync(cancellationToken);

        var questions = new List<QuickReviewQuestionDto>();
        int index = 1;

        foreach (var concept in combinedConcepts)
        {
            var lesson = lessons.FirstOrDefault(l => l.Id == (concept.LastAssessedLessonId ?? concept.FirstIntroducedLessonId)) ?? lessons.FirstOrDefault();
            var hadith = lesson?.Hadiths.FirstOrDefault(h => h.Problem.Contains(concept.ConceptKey, StringComparison.OrdinalIgnoreCase)) ?? lesson?.Hadiths.FirstOrDefault();

            questions.Add(new QuickReviewQuestionDto
            {
                Index = index++,
                ConceptKey = concept.ConceptKey,
                Question = hadith != null
                    ? $"ما هو الضابط والحكم الشرعي في مسألة ({concept.ConceptKey})؟ وبيّن وجه الاستدلال النبوي عليها."
                    : $"اشرح بإيجاز مسألة ({concept.ConceptKey}) مستحضراً الدليل والتوجيه الفقهي.",
                QuestionType = concept.SuggestedQuestionType,
                Difficulty = concept.IsWeak ? "Beginner" : "Intermediate",
                Guidance = "بيّن وجه الدلالة والحكم باختصار ودقة.",
                SourceLessonId = lesson?.Id,
                LessonTitle = lesson?.Title
            });
        }

        // Fallback to lesson review questions if empty
        if (questions.Count == 0 && lessons.Count > 0)
        {
            foreach (var l in lessons.Take(count))
            {
                var q = l.ReviewQuestions.FirstOrDefault();
                if (q != null)
                {
                    questions.Add(new QuickReviewQuestionDto
                    {
                        Index = index++,
                        ConceptKey = l.Title,
                        Question = q.Question,
                        QuestionType = "Retrieval",
                        Difficulty = "Intermediate",
                        Guidance = "استحضر أدلة الباب وخلاصته.",
                        SourceLessonId = l.Id,
                        LessonTitle = l.Title
                    });
                }
            }
        }

        return new QuickReviewQuizDto
        {
            BookId = bookId,
            Title = "اختبار التثبيت والمراجعة السريع",
            Questions = questions
        };
    }
}

public static class AdaptiveLearningPolicy
{
    /// <summary>
    /// Confidence = 45% current evidence score + 25% successful retrieval +
    /// 15% cross-context evidence + 10% exposure + 5% recency, reduced by up
    /// to 25% for incorrect answers. It is intentionally explainable and stable.
    /// </summary>
    public static AdaptiveConceptState Evaluate(StudentConceptMastery mastery, DateTime now)
    {
        var accuracy = mastery.AssessmentCount == 0 ? 0d : (double)mastery.CorrectAnswerCount / mastery.AssessmentCount;
        var contexts = Math.Min(1d, mastery.DemonstratedContextCount / 3d);
        var exposure = Math.Min(1d, mastery.ExposureCount / 3d);
        var recency = mastery.LastAssessedAt switch
        {
            null => 0d,
            var value when value >= now.AddDays(-14) => 1d,
            var value when value >= now.AddDays(-30) => .5d,
            _ => 0d
        };
        var incorrect = Math.Max(0, mastery.AssessmentCount - mastery.CorrectAnswerCount);
        var failurePenalty = mastery.AssessmentCount == 0 ? 0d : .25d * incorrect / mastery.AssessmentCount;
        var confidence = Math.Clamp(.45 * mastery.MasteryScore + .25 * accuracy + .15 * contexts + .10 * exposure + .05 * recency - failurePenalty, 0, 1);
        var reviewDue = mastery.LastAssessedAt is { } assessed && assessed < now.AddDays(-21) && mastery.LearningLevel is LearningLevel.Understood or LearningLevel.Mastered;
        var weak = mastery.AssessmentCount > 0 && (accuracy < .5 || confidence < .40) || mastery.LearningLevel == LearningLevel.Introduced && mastery.ExposureCount >= 2;

        var (action, code, reason) = mastery.LearningLevel switch
        {
            LearningLevel.Unknown => (AdaptiveLearningAction.Introduce, "unknown", "لم يواجه الطالب المفهوم من قبل."),
            LearningLevel.Introduced when weak => (AdaptiveLearningAction.Explain, "introduced_weak", "ظهر المفهوم دون دليل كافٍ على الفهم؛ يحتاج إلى شرح من زاوية أخرى."),
            LearningLevel.Introduced => (AdaptiveLearningAction.Explain, "introduced", "المفهوم قُدّم مرة واحدة ولم يُتحقق من فهمه."),
            LearningLevel.Familiar when weak => (AdaptiveLearningAction.Reinforce, "familiar_weak", "التعرّف موجود لكن أدلة التقييم ضعيفة أو متعارضة."),
            LearningLevel.Familiar => (AdaptiveLearningAction.AskQuestion, "familiar_retrieval", "يحتاج إلى استرجاع وشرح لإثبات الفهم."),
            LearningLevel.Understood when reviewDue => (AdaptiveLearningAction.Review, "understood_review_due", "مر وقت على آخر استرجاع ناجح."),
            LearningLevel.Understood => (AdaptiveLearningAction.VerifyMastery, "understood_verify", "الفهم ظاهر؛ يلزم تحقق عبر سياق آخر قبل الإتقان."),
            LearningLevel.Mastered when reviewDue => (AdaptiveLearningAction.Review, "mastered_review_due", "الإتقان يحتاج استرجاعاً دورياً بعد انقطاع طويل."),
            LearningLevel.Mastered => (AdaptiveLearningAction.NoAction, "mastered_recent", "دليل الإتقان حديث ومستقر."),
            _ => (AdaptiveLearningAction.Introduce, "unknown", "لا توجد أدلة كافية.")
        };

        return new AdaptiveConceptState
        {
            ConceptKey = mastery.ConceptKey,
            LearningLevel = mastery.LearningLevel,
            Confidence = Math.Round(confidence, 3),
            ExposureCount = mastery.ExposureCount,
            AssessmentCount = mastery.AssessmentCount,
            CorrectAnswerCount = mastery.CorrectAnswerCount,
            DemonstratedContextCount = mastery.DemonstratedContextCount,
            LastAssessedAt = mastery.LastAssessedAt,
            FirstIntroducedLessonId = mastery.FirstIntroducedLessonId,
            LastAssessedLessonId = mastery.LastAssessedLessonId,
            Action = action,
            ReasonCode = code,
            Reason = reason,
            SuggestedQuestionType = QuestionType(action),
            IsWeak = weak,
            IsReviewDue = reviewDue
        };
    }

    private static string QuestionType(AdaptiveLearningAction action) => action switch
    {
        AdaptiveLearningAction.Introduce or AdaptiveLearningAction.Explain => "Recognition",
        AdaptiveLearningAction.Reinforce => "Understanding",
        AdaptiveLearningAction.AskQuestion => "Explanation",
        AdaptiveLearningAction.VerifyMastery => "Reasoning",
        AdaptiveLearningAction.Review => "Retrieval",
        _ => "None"
    };
}
