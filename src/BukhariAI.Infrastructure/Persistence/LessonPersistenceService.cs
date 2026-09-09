using System.Text.RegularExpressions;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainLearningContext = BukhariAI.Domain.Entities.LessonLearningContext;
using AppLearningContext = BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext;

namespace BukhariAI.Infrastructure.Persistence;

public sealed partial class LessonPersistenceService : ILessonPersistenceService
{
    private static readonly Regex InlineTermRegex = GeneratedInlineTermRegex();
    private static readonly Regex PersonSplitRegex = GeneratedPersonSplitRegex();

    private readonly BukhariDbContext _dbContext;
    private readonly ILogger<LessonPersistenceService> _logger;
    private readonly ICurrentUserService? _currentUserService;

    public LessonPersistenceService(
        BukhariDbContext dbContext,
        ILogger<LessonPersistenceService> logger,
        ICurrentUserService? currentUserService = null)
    {
        _dbContext = dbContext;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    public async Task<Book> EnsureDefaultBookAsync(Guid? userId = null, CancellationToken cancellationToken = default)
    {
        Guid targetUserId = userId ?? _currentUserService?.UserId ?? User.DefaultUserId;

        var existing = await _dbContext.Books
            .FirstOrDefaultAsync(b => b.Title == "صحيح البخاري" && b.UserId == targetUserId, cancellationToken);

        if (existing != null)
        {
            return existing;
        }

        var defaultBook = new Book
        {
            Id = Guid.NewGuid(),
            UserId = targetUserId,
            Title = "صحيح البخاري",
            Author = "الإمام محمد بن إسماعيل البخاري",
            Description = "الجامع المسند الصحيح المختصر من أمور رسول الله صلى الله عليه وسلم وسننه وأيامه",
            CreatedAtUtc = DateTime.UtcNow
        };

        var learningContext = new DomainLearningContext
        {
            Id = Guid.NewGuid(),
            BookId = defaultBook.Id,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Books.Add(defaultBook);
        _dbContext.LessonLearningContexts.Add(learningContext);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created default book entity 'صحيح البخاري' with ID {BookId} for user {UserId}.", defaultBook.Id, targetUserId);
        return defaultBook;
    }

    public async Task<Book> CreateBookAsync(string title, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        string normalizedTitle = title.Trim();
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            throw new ArgumentException("Book title is required.", nameof(title));

        Guid targetUserId = userId ?? _currentUserService?.UserId ?? User.DefaultUserId;

        var existing = await _dbContext.Books
            .FirstOrDefaultAsync(b => b.Title == normalizedTitle && b.UserId == targetUserId, cancellationToken);
        if (existing != null) return existing;

        var book = new Book
        {
            Id = Guid.NewGuid(),
            UserId = targetUserId,
            Title = normalizedTitle,
            CreatedAtUtc = DateTime.UtcNow
        };
        var learningContext = new DomainLearningContext
        {
            Id = Guid.NewGuid(),
            BookId = book.Id,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Books.Add(book);
        _dbContext.LessonLearningContexts.Add(learningContext);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return book;
    }

    public async Task<Guid> PersistLessonAsync(
        GenerateLessonResponse response,
        PdfExtractionResult extractionResult,
        Guid? bookId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(extractionResult);

        // 1. Resolve Target Book
        Book book;
        Guid targetUserId = _currentUserService?.UserId ?? User.DefaultUserId;
        if (bookId.HasValue)
        {
            book = await _dbContext.Books
                .FirstOrDefaultAsync(b => b.Id == bookId.Value, cancellationToken)
                ?? await EnsureDefaultBookAsync(targetUserId, cancellationToken);
        }
        else
        {
            book = await EnsureDefaultBookAsync(targetUserId, cancellationToken);
        }

        _logger.LogInformation(
            "Persisting lesson '{Title}' for Book '{BookTitle}' (Pages {StartPage}-{EndPage}).",
            response.Title,
            book.Title,
            response.Metadata.StartPage,
            response.Metadata.EndPage);

        // 2. Resolve / Create BookPage Records
        var pageNumbers = extractionResult.Pages.Select(p => p.PageNumber).Distinct().ToList();
        if (pageNumbers.Count == 0)
        {
            int start = response.Metadata.StartPage > 0 ? response.Metadata.StartPage : 1;
            int end = response.Metadata.EndPage >= start ? response.Metadata.EndPage : start;
            pageNumbers = Enumerable.Range(start, end - start + 1).ToList();
        }

        var existingPages = await _dbContext.BookPages
            .Where(bp => bp.BookId == book.Id && pageNumbers.Contains(bp.PageNumber))
            .ToListAsync(cancellationToken);

        var bookPageMap = existingPages.ToDictionary(p => p.PageNumber);

        foreach (int pageNumber in pageNumbers)
        {
            if (bookPageMap.TryGetValue(pageNumber, out var existingPage))
            {
                // Page content is intentionally not transcribed or stored locally.
            }
            else
            {
                var newBookPage = new BookPage
                {
                    Id = Guid.NewGuid(),
                    BookId = book.Id,
                    PageNumber = pageNumber,
                    ExtractedText = string.Empty,
                    UsedOcr = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.BookPages.Add(newBookPage);
                bookPageMap[pageNumber] = newBookPage;
            }
        }

        // 3. Create Lesson Entity
        var lesson = new Lesson
        {
            Id = Guid.NewGuid(),
            BookId = book.Id,
            Title = response.Title,
            Overview = response.Overview,
            HistoricalContext = response.HistoricalContext,
            StartPage = response.Metadata.StartPage > 0 ? response.Metadata.StartPage : response.SourcePages.DefaultIfEmpty(0).Min(),
            EndPage = response.Metadata.EndPage > 0 ? response.Metadata.EndPage : response.SourcePages.DefaultIfEmpty(0).Max(),
            CreatedAtUtc = DateTime.UtcNow
        };

        // 4. Create LessonPage Join Records
        foreach (var kvp in bookPageMap)
        {
            lesson.LessonPages.Add(new LessonPage
            {
                LessonId = lesson.Id,
                BookPageId = kvp.Value.Id,
                PageNumber = kvp.Key
            });
        }

        // 5. Store Connections & Review Questions
        foreach (string conn in response.Connections)
        {
            if (string.IsNullOrWhiteSpace(conn)) continue;
            lesson.Connections.Add(new LessonConnection
            {
                Id = Guid.NewGuid(),
                LessonId = lesson.Id,
                Description = conn.Trim()
            });
        }

        foreach (string q in response.ReviewQuestions)
        {
            if (string.IsNullOrWhiteSpace(q)) continue;
            lesson.ReviewQuestions.Add(new ReviewQuestion
            {
                Id = Guid.NewGuid(),
                LessonId = lesson.Id,
                Question = q.Trim()
            });
        }

        // 5b. Store Assessment & Assessment Questions
        if (response.AssessmentQuestions != null && response.AssessmentQuestions.Count > 0)
        {
            var assessment = new Assessment
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                LessonId = lesson.Id,
                Title = $"تقييم فهم درس: {response.Title}",
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var aqDto in response.AssessmentQuestions)
            {
                // An assessment may only be persisted if it identifies a source page
                // belonging to this lesson and carries an evaluation rubric.  This is
                // the last deterministic guard against unsupported AI questions.
                if (!IsSourceBoundAssessmentQuestion(aqDto, response.SourcePages)) continue;
                assessment.Questions.Add(new AssessmentQuestion
                {
                    Id = Guid.NewGuid(),
                    AssessmentId = assessment.Id,
                    SourceLessonId = lesson.Id,
                    Question = aqDto.Question.Trim(),
                    QuestionType = string.IsNullOrWhiteSpace(aqDto.QuestionType) ? "ConceptualExplanation" : aqDto.QuestionType.Trim(),
                    Difficulty = string.IsNullOrWhiteSpace(aqDto.Difficulty) ? "Intermediate" : aqDto.Difficulty.Trim(),
                    ExpectedConceptsCsv = aqDto.ExpectedConcepts != null && aqDto.ExpectedConcepts.Count > 0 ? string.Join(",", aqDto.ExpectedConcepts) : string.Empty,
                    SourcePagesCsv = aqDto.SourcePages != null && aqDto.SourcePages.Count > 0 ? string.Join(",", aqDto.SourcePages) : string.Empty,
                    EvaluationGuidance = aqDto.EvaluationGuidance?.Trim() ?? string.Empty,
                    CreatedAtUtc = DateTime.UtcNow
                });
            }

            lesson.Assessments.Add(assessment);
            _dbContext.Set<Assessment>().Add(assessment);
        }

        // 6. Resolve Reusable People and Create LessonPerson
        var resolvedPeople = new Dictionary<string, (Person Person, string ContextDesc)>(StringComparer.OrdinalIgnoreCase);

        var allPeopleStrings = response.Hadiths
            .SelectMany(h => h.People)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (string rawPerson in allPeopleStrings)
        {
            var (name, contextDesc) = ParsePersonString(rawPerson);
            if (string.IsNullOrWhiteSpace(name)) continue;

            if (!resolvedPeople.TryGetValue(name, out var item))
            {
                var person = await ResolveOrCreatePersonAsync(name, contextDesc, cancellationToken);
                resolvedPeople[name] = (person, contextDesc);
            }

            var resolved = resolvedPeople[name];
            if (!lesson.LessonPeople.Any(lp => lp.PersonId == resolved.Person.Id))
            {
                lesson.LessonPeople.Add(new LessonPerson
                {
                    LessonId = lesson.Id,
                    PersonId = resolved.Person.Id,
                    ContextDescription = resolved.ContextDesc
                });
            }
        }

        // 7. Store LessonHadiths (with Evidences, Points, HadithPeople)
        foreach (var hadithDto in response.Hadiths)
        {
            var hadithEntity = new LessonHadith
            {
                Id = Guid.NewGuid(),
                LessonId = lesson.Id,
                Reference = hadithDto.Reference,
                Summary = hadithDto.Summary,
                Problem = hadithDto.Problem,
                Reasoning = hadithDto.Reasoning,
                ScholarlyDiscussion = hadithDto.ScholarlyDiscussion,
                Conclusion = hadithDto.Conclusion,
                EasyExplanation = hadithDto.EasyExplanation,
                HistoricalContext = response.HistoricalContext
            };

            // Evidence
            foreach (var ev in hadithDto.Evidence)
            {
                if (string.IsNullOrWhiteSpace(ev.Text)) continue;
                hadithEntity.Evidences.Add(new HadithEvidence
                {
                    Id = Guid.NewGuid(),
                    HadithId = hadithEntity.Id,
                    Text = ev.Text,
                    Role = ev.Role,
                    SourcePagesCsv = ev.SourcePages != null && ev.SourcePages.Count > 0
                        ? string.Join(",", ev.SourcePages)
                        : string.Empty
                });
            }

            // Points
            foreach (string pt in hadithDto.Lessons)
            {
                if (string.IsNullOrWhiteSpace(pt)) continue;
                hadithEntity.LessonPoints.Add(new HadithLessonPoint
                {
                    Id = Guid.NewGuid(),
                    HadithId = hadithEntity.Id,
                    Point = pt.Trim()
                });
            }

            // HadithPeople
            foreach (string rawPerson in hadithDto.People)
            {
                var (name, _) = ParsePersonString(rawPerson);
                if (!string.IsNullOrWhiteSpace(name) && resolvedPeople.TryGetValue(name, out var item))
                {
                    if (!hadithEntity.HadithPeople.Any(hp => hp.PersonId == item.Person.Id))
                    {
                        hadithEntity.HadithPeople.Add(new HadithPerson
                        {
                            HadithId = hadithEntity.Id,
                            PersonId = item.Person.Id
                        });
                    }
                }
            }

            lesson.Hadiths.Add(hadithEntity);
        }

        // 8. Record Lesson Progress
        var progress = new LessonProgress
        {
            Id = Guid.NewGuid(),
            UserId = book.UserId != Guid.Empty && book.UserId != User.DefaultUserId ? book.UserId : targetUserId,
            LessonId = lesson.Id,
            Status = LessonProgressStatus.NotStarted,
            ReadAtUtc = null,
            UnderstandingLevel = null,
            LastReviewedAtUtc = null
        };
        lesson.LessonProgresses.Add(progress);

        // Add Lesson & children to context
        _dbContext.Lessons.Add(lesson);

        // 9. Update Active Educational Memory (Progressive LearningContext)
        var learningContext = await _dbContext.LessonLearningContexts
            .Include(lc => lc.KnownPeople)
            .Include(lc => lc.KnownTerms)
            .Include(lc => lc.KnownTopics)
            .FirstOrDefaultAsync(lc => lc.BookId == book.Id, cancellationToken);

        if (learningContext == null)
        {
            learningContext = new DomainLearningContext
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.LessonLearningContexts.Add(learningContext);
        }

        // 9a. Update KnownPeople with TimesSeen and Level progression
        foreach (var (person, contextDesc) in resolvedPeople.Values)
        {
            var existingKp = learningContext.KnownPeople.FirstOrDefault(kp => kp.PersonId == person.Id);
            if (existingKp != null)
            {
                existingKp.TimesSeen++;
                existingKp.LastReferencedLessonId = lesson.Id;
                existingKp.LearningLevel = CalculateLearningLevel(existingKp.TimesSeen);

                // Preserve rich existing biography; update only if existing is brief and new is descriptive
                if (string.IsNullOrWhiteSpace(existingKp.ShortBiography) ||
                    (existingKp.ShortBiography.Length < 25 && contextDesc.Length >= 25))
                {
                    existingKp.ShortBiography = contextDesc;
                }
            }
            else
            {
                var newKp = new KnownPerson
                {
                    Id = Guid.NewGuid(),
                    LearningContextId = learningContext.Id,
                    PersonId = person.Id,
                    ShortBiography = contextDesc,
                    TimesSeen = 1,
                    LearningLevel = LearningLevel.Introduced,
                    FirstIntroducedLessonId = lesson.Id,
                    LastReferencedLessonId = lesson.Id
                };
                _dbContext.KnownPeople.Add(newKp);
                learningContext.KnownPeople.Add(newKp);
            }
        }

        // 9b. Update KnownTopics
        if (!string.IsNullOrWhiteSpace(response.Title))
        {
            var existingTopic = learningContext.KnownTopics
                .FirstOrDefault(kt => string.Equals(kt.Topic, response.Title, StringComparison.OrdinalIgnoreCase));

            if (existingTopic != null)
            {
                existingTopic.LastReferencedLessonId = lesson.Id;
            }
            else
            {
                var newTopic = new KnownTopic
                {
                    Id = Guid.NewGuid(),
                    LearningContextId = learningContext.Id,
                    Topic = response.Title.Trim(),
                    FirstIntroducedLessonId = lesson.Id,
                    LastReferencedLessonId = lesson.Id
                };
                _dbContext.KnownTopics.Add(newTopic);
                learningContext.KnownTopics.Add(newTopic);
            }
        }

        // 9c. Extract and Update KnownTerms with TimesSeen and Level progression
        var allTextToScanForTerms = response.Hadiths
            .Select(h => $"{h.EasyExplanation} {h.Summary} {h.Reasoning}")
            .Concat([response.Overview])
            .ToList();

        foreach (string textBlock in allTextToScanForTerms)
        {
            var extractedTerms = ExtractInlineTerms(textBlock);
            foreach (var (termName, explanation) in extractedTerms)
            {
                if (string.IsNullOrWhiteSpace(termName)) continue;

                var existingKt = learningContext.KnownTerms
                    .FirstOrDefault(kt => NormalizeKey(kt.Term) == NormalizeKey(termName));

                if (existingKt != null)
                {
                    existingKt.TimesSeen++;
                    existingKt.LastReferencedLessonId = lesson.Id;
                    existingKt.LearningLevel = CalculateLearningLevel(existingKt.TimesSeen);

                    // Update explanation only if existing is empty or substantially shorter than new
                    if (string.IsNullOrWhiteSpace(existingKt.Explanation) ||
                        (existingKt.Explanation.Length < 20 && explanation.Length >= 20))
                    {
                        existingKt.Explanation = explanation;
                    }
                }
                else
                {
                    var newTerm = new KnownTerm
                    {
                        Id = Guid.NewGuid(),
                        LearningContextId = learningContext.Id,
                        Term = termName.Trim(),
                        Explanation = explanation.Trim(),
                        TimesSeen = 1,
                        LearningLevel = LearningLevel.Introduced,
                        FirstIntroducedLessonId = lesson.Id,
                        LastReferencedLessonId = lesson.Id
                    };
                    _dbContext.KnownTerms.Add(newTerm);
                    learningContext.KnownTerms.Add(newTerm);
                }
            }
        }

        learningContext.UpdatedAtUtc = DateTime.UtcNow;

        // 9d. Record initial concept exposures in StudentConceptMastery
        var exposedConcepts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kt in learningContext.KnownTerms)
        {
            string? cleanKt = ConceptSanitizer.CleanAndValidate(kt.Term);
            if (cleanKt != null) exposedConcepts.Add(cleanKt);
        }

        if (response.AssessmentQuestions != null)
        {
            foreach (var exp in response.AssessmentQuestions.SelectMany(aq => aq.ExpectedConcepts))
            {
                string? cleanExp = ConceptSanitizer.CleanAndValidate(exp);
                if (cleanExp != null) exposedConcepts.Add(cleanExp);
            }
        }

        // If no explicit concepts, derive high-quality topic titles from Hadith problems or lesson title
        if (exposedConcepts.Count == 0)
        {
            foreach (var h in response.Hadiths.Where(h => !string.IsNullOrWhiteSpace(h.Problem)))
            {
                string derived = ConceptSanitizer.DeriveTopicTitle(h.Problem);
                if (ConceptSanitizer.IsValidConcept(derived)) exposedConcepts.Add(derived);
            }

            if (exposedConcepts.Count == 0 && !string.IsNullOrWhiteSpace(response.Title))
            {
                string derived = ConceptSanitizer.DeriveTopicTitle(response.Title);
                if (ConceptSanitizer.IsValidConcept(derived)) exposedConcepts.Add(derived);
            }
        }

        var existingMasteries = await _dbContext.Set<StudentConceptMastery>()
            .Where(m => m.BookId == book.Id)
            .ToListAsync(cancellationToken);

        var masteryMap = existingMasteries.ToDictionary(
            m => NormalizeKey(m.ConceptKey),
            StringComparer.OrdinalIgnoreCase);

        foreach (var concept in exposedConcepts)
        {
            string? validConcept = ConceptSanitizer.CleanAndValidate(concept);
            if (validConcept == null) continue;

            string norm = NormalizeKey(validConcept);
            if (masteryMap.TryGetValue(norm, out var existingMastery))
            {
                existingMastery.ExposureCount++;
                existingMastery.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                var newMastery = new StudentConceptMastery
                {
                    Id = Guid.NewGuid(),
                    BookId = book.Id,
                    ConceptKey = validConcept,
                    LearningLevel = LearningLevel.Introduced,
                    ExposureCount = 1,
                    AssessmentCount = 0,
                    CorrectAnswerCount = 0,
                    MasteryScore = 0.20,
                    FirstIntroducedLessonId = lesson.Id,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow
                };
                _dbContext.Set<StudentConceptMastery>().Add(newMastery);
                masteryMap[norm] = newMastery;
            }
        }

        // 10. Record Reading Session
        var readingSession = new ReadingSession
        {
            Id = Guid.NewGuid(),
            BookId = book.Id,
            LessonId = lesson.Id,
            StartPage = lesson.StartPage,
            EndPage = lesson.EndPage,
            StartedAtUtc = DateTime.UtcNow,
            CompletedAtUtc = null,
            Status = ReadingSessionStatus.NotStarted
        };
        _dbContext.ReadingSessions.Add(readingSession);

        // 11. Save all changes atomically
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Successfully persisted complete lesson aggregate '{Title}' (ID: {LessonId}) under Book '{BookTitle}'.",
            lesson.Title,
            lesson.Id,
            book.Title);

        return lesson.Id;
    }

    public async Task<Guid> PersistOrUpdateQuranSurahLessonAsync(
        BukhariAI.Application.Quran.QuranSurahAnalysisResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(response.SurahInfo);

        // 1. Ensure Quran Book collection exists
        const string quranBookTitle = "📖 القرآن الكريم وعلومه";
        var book = await _dbContext.Books
            .FirstOrDefaultAsync(b => b.Title == quranBookTitle || b.Title == "القرآن الكريم", cancellationToken);

        if (book == null)
        {
            book = new Book
            {
                Id = Guid.NewGuid(),
                Title = quranBookTitle,
                Author = "كلام الله تعالى المنزّل على نبيه محمد صلى الله عليه وسلم",
                Description = "مسار حفظ وتدبر سور القرآن الكريم بالروابط الذهنية وعلم المناسبات وسر الفواصل وضبط المتشابهات.",
                CreatedAtUtc = DateTime.UtcNow
            };
            var learningContext = new DomainLearningContext
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Books.Add(book);
            _dbContext.LessonLearningContexts.Add(learningContext);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        string rawSurahName = response.SurahInfo.Name.Trim();
        string cleanSurahName = rawSurahName.Replace("سورة ", "").Trim();

        // Parse Start & End Ayah
        int startAyah = 1;
        int endAyah = response.SurahInfo.TotalAyat > 0 ? response.SurahInfo.TotalAyat : 30;
        if (!string.IsNullOrWhiteSpace(response.SurahInfo.AnalyzedRange))
        {
            var parts = response.SurahInfo.AnalyzedRange.Split(new[] { "-", "–", "إلى" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0].Trim(), out int s) && int.TryParse(parts[1].Trim(), out int e))
            {
                startAyah = s;
                endAyah = e;
            }
        }
        else if (response.AyahAnalyses.Count > 0)
        {
            startAyah = response.AyahAnalyses.Min(a => a.AyahNumber);
            endAyah = response.AyahAnalyses.Max(a => a.AyahNumber);
        }

        // Include ayah range in lesson title for long surahs (> 25 verses) or specific chunks
        bool isPartialChunk = response.SurahInfo.TotalAyat > 25 || startAyah > 1 || endAyah < response.SurahInfo.TotalAyat;
        string lessonTitle = isPartialChunk
            ? $"تدبر وتحفيظ سورة {cleanSurahName} (الآيات {startAyah} - {endAyah})"
            : $"تدبر وتحفيظ سورة {cleanSurahName}";

        // 2. Check if a lesson for this Surah/Chunk ALREADY EXISTS in the Quran book
        var existingLesson = await _dbContext.Lessons
            .Include(l => l.Hadiths)
                .ThenInclude(h => h.Evidences)
            .Include(l => l.Hadiths)
                .ThenInclude(h => h.LessonPoints)
            .Include(l => l.Connections)
            .Include(l => l.ReviewQuestions)
            .Include(l => l.LessonPages)
            .Include(l => l.LessonProgresses)
            .FirstOrDefaultAsync(l => l.BookId == book.Id && (l.Title == lessonTitle || (l.StartPage == startAyah && l.EndPage == endAyah && l.Title.Contains(cleanSurahName))), cancellationToken);

        Lesson targetLesson;
        bool isUpdated = existingLesson != null;

        if (existingLesson != null)
        {
            _logger.LogInformation("Updating existing Quran lesson '{LessonTitle}' (ID {LessonId}).", existingLesson.Title, existingLesson.Id);
            targetLesson = existingLesson;
            targetLesson.Title = lessonTitle;
            targetLesson.Overview = !string.IsNullOrWhiteSpace(response.SurahInfo.MainObjective) ? response.SurahInfo.MainObjective : targetLesson.Overview;
            targetLesson.HistoricalContext = $"سورة {response.SurahInfo.RevelationType}، إجمالي آياتها {response.SurahInfo.TotalAyat} آية. النطاق المتدارس: {startAyah} - {endAyah}.";
            targetLesson.StartPage = Math.Min(targetLesson.StartPage > 0 ? targetLesson.StartPage : startAyah, startAyah);
            targetLesson.EndPage = Math.Max(targetLesson.EndPage, endAyah);
            targetLesson.CreatedAtUtc = DateTime.UtcNow;
        }
        else
        {
            _logger.LogInformation("Creating new Quran lesson '{LessonTitle}' for Book '{BookTitle}'.", lessonTitle, book.Title);
            targetLesson = new Lesson
            {
                Id = Guid.NewGuid(),
                BookId = book.Id,
                Title = lessonTitle,
                Overview = response.SurahInfo.MainObjective ?? string.Empty,
                HistoricalContext = $"سورة {response.SurahInfo.RevelationType}، إجمالي آياتها {response.SurahInfo.TotalAyat} آية. النطاق المتدارس: {startAyah} - {endAyah}.",
                StartPage = startAyah,
                EndPage = endAyah,
                CreatedAtUtc = DateTime.UtcNow
            };
            _dbContext.Lessons.Add(targetLesson);

            // Initialize LessonProgress
            var progress = new LessonProgress
            {
                Id = Guid.NewGuid(),
                LessonId = targetLesson.Id,
                Status = LessonProgressStatus.NotStarted,
                ReadAtUtc = DateTime.UtcNow
            };
            _dbContext.LessonProgresses.Add(progress);
        }

        // 3. Ensure BookPages exist for these Ayah numbers
        var ayahNumbers = response.AyahAnalyses.Select(a => a.AyahNumber).DefaultIfEmpty(startAyah).Distinct().ToList();
        var existingPages = await _dbContext.BookPages
            .Where(bp => bp.BookId == book.Id && ayahNumbers.Contains(bp.PageNumber))
            .ToListAsync(cancellationToken);
        var pageMap = existingPages.ToDictionary(p => p.PageNumber);

        foreach (int num in ayahNumbers)
        {
            if (!pageMap.TryGetValue(num, out var p))
            {
                p = new BookPage
                {
                    Id = Guid.NewGuid(),
                    BookId = book.Id,
                    PageNumber = num,
                    ExtractedText = string.Empty,
                    UsedOcr = false,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _dbContext.BookPages.Add(p);
                pageMap[num] = p;
            }

            if (!targetLesson.LessonPages.Any(lp => lp.PageNumber == num))
            {
                targetLesson.LessonPages.Add(new LessonPage
                {
                    LessonId = targetLesson.Id,
                    BookPageId = p.Id,
                    PageNumber = num
                });
            }
        }

        // 4. Update / Add LessonHadiths (representing Ayahs)
        foreach (var ayah in response.AyahAnalyses)
        {
            string refKey = $"الآية {ayah.AyahNumber}";
            var hadith = targetLesson.Hadiths.FirstOrDefault(h => h.Reference == refKey);

            if (hadith == null)
            {
                hadith = new LessonHadith
                {
                    Id = Guid.NewGuid(),
                    LessonId = targetLesson.Id,
                    Reference = refKey,
                    Summary = !string.IsNullOrWhiteSpace(ayah.EndingFasila) ? $"فاصلة الآية: {ayah.EndingFasila}" : string.Empty,
                    Problem = ayah.GeneralMeaning,
                    Reasoning = !string.IsNullOrWhiteSpace(ayah.ContextWithNext) ? $"🔗 وجه المناسبة والربط البياني: {ayah.ContextWithNext}" : string.Empty,
                    Conclusion = !string.IsNullOrWhiteSpace(ayah.EndingReason) ? $"🎯 سر الفاصلة: {ayah.EndingReason}" : string.Empty,
                    EasyExplanation = !string.IsNullOrWhiteSpace(ayah.ActionableTadabbur) ? $"✨ الوقفة التدبرية: {ayah.ActionableTadabbur}" : string.Empty,
                    HistoricalContext = targetLesson.HistoricalContext
                };

                hadith.Evidences.Add(new HadithEvidence
                {
                    Id = Guid.NewGuid(),
                    HadithId = hadith.Id,
                    Text = $"﴿ {ayah.AyahText} ﴾",
                    Role = "نص الآية القرآنية",
                    SourcePagesCsv = ayah.AyahNumber.ToString()
                });

                if (ayah.Vocabulary != null)
                {
                    foreach (var v in ayah.Vocabulary)
                    {
                        hadith.LessonPoints.Add(new HadithLessonPoint
                        {
                            Id = Guid.NewGuid(),
                            HadithId = hadith.Id,
                            Point = $"{v.Word}: {v.Meaning}"
                        });
                    }
                }

                targetLesson.Hadiths.Add(hadith);
            }
            else
            {
                // Update existing Ayah
                hadith.Summary = !string.IsNullOrWhiteSpace(ayah.EndingFasila) ? $"فاصلة الآية: {ayah.EndingFasila}" : hadith.Summary;
                hadith.Problem = !string.IsNullOrWhiteSpace(ayah.GeneralMeaning) ? ayah.GeneralMeaning : hadith.Problem;
                hadith.Reasoning = !string.IsNullOrWhiteSpace(ayah.ContextWithNext) ? $"🔗 وجه المناسبة والربط البياني: {ayah.ContextWithNext}" : hadith.Reasoning;
                hadith.Conclusion = !string.IsNullOrWhiteSpace(ayah.EndingReason) ? $"🎯 سر الفاصلة: {ayah.EndingReason}" : hadith.Conclusion;
                hadith.EasyExplanation = !string.IsNullOrWhiteSpace(ayah.ActionableTadabbur) ? $"✨ الوقفة التدبرية: {ayah.ActionableTadabbur}" : hadith.EasyExplanation;

                var evidence = hadith.Evidences.FirstOrDefault();
                if (evidence == null)
                {
                    hadith.Evidences.Add(new HadithEvidence
                    {
                        Id = Guid.NewGuid(),
                        HadithId = hadith.Id,
                        Text = $"﴿ {ayah.AyahText} ﴾",
                        Role = "نص الآية القرآنية",
                        SourcePagesCsv = ayah.AyahNumber.ToString()
                    });
                }
                else
                {
                    evidence.Text = $"﴿ {ayah.AyahText} ﴾";
                }

                if (ayah.Vocabulary != null && ayah.Vocabulary.Count > 0)
                {
                    foreach (var v in ayah.Vocabulary)
                    {
                        string ptText = $"{v.Word}: {v.Meaning}";
                        if (!hadith.LessonPoints.Any(p => p.Point == ptText))
                        {
                            hadith.LessonPoints.Add(new HadithLessonPoint
                            {
                                Id = Guid.NewGuid(),
                                HadithId = hadith.Id,
                                Point = ptText
                            });
                        }
                    }
                }
            }
        }

        // 5. Update Connections (Thematic sections + Mutashabihat)
        if (response.ThematicSections != null)
        {
            foreach (var sec in response.ThematicSections)
            {
                string desc = $"📑 المحور: {sec.Title} (الآيات {sec.AyahRange}) — {sec.Summary}";
                if (!targetLesson.Connections.Any(c => c.Description == desc))
                {
                    targetLesson.Connections.Add(new LessonConnection
                    {
                        Id = Guid.NewGuid(),
                        LessonId = targetLesson.Id,
                        Description = desc
                    });
                }
            }
        }

        if (response.Mutashabihat != null)
        {
            foreach (var m in response.Mutashabihat)
            {
                string desc = $"⚖️ متشابهة آية ({m.BaseAyahNumber}) مع {m.SimilarSurahOrAyah} — الفرق: {m.DifferenceSummary} — الضابط: {m.MnemonicRule}";
                if (!targetLesson.Connections.Any(c => c.Description == desc))
                {
                    targetLesson.Connections.Add(new LessonConnection
                    {
                        Id = Guid.NewGuid(),
                        LessonId = targetLesson.Id,
                        Description = desc
                    });
                }
            }
        }

        // 6. Update Review Questions (from core reflections)
        if (response.CoreReflections != null)
        {
            foreach (var r in response.CoreReflections)
            {
                string q = $"كيف تطبق هذه الهداية في واقعك: \"{r}\"؟";
                if (!targetLesson.ReviewQuestions.Any(rq => rq.Question == q))
                {
                    targetLesson.ReviewQuestions.Add(new ReviewQuestion
                    {
                        Id = Guid.NewGuid(),
                        LessonId = targetLesson.Id,
                        Question = q
                    });
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Successfully persisted/updated Quran lesson {LessonId} for {LessonTitle}.", targetLesson.Id, lessonTitle);

        response.LessonId = targetLesson.Id;
        response.IsExistingLessonUpdated = isUpdated;

        return targetLesson.Id;
    }

    public async Task<AppLearningContext> GetActiveEducationalContextAsync(
        Guid? bookId = null,
        CancellationToken cancellationToken = default)
    {
        Book book;
        Guid targetUserId = _currentUserService?.UserId ?? User.DefaultUserId;
        if (bookId.HasValue)
        {
            book = await _dbContext.Books.FirstOrDefaultAsync(b => b.Id == bookId.Value, cancellationToken)
                   ?? await EnsureDefaultBookAsync(targetUserId, cancellationToken);
        }
        else
        {
            book = await EnsureDefaultBookAsync(targetUserId, cancellationToken);
        }

        var domainContext = await _dbContext.LessonLearningContexts
            .AsNoTracking()
            .Include(lc => lc.KnownPeople)
                .ThenInclude(kp => kp.Person)
            .Include(lc => lc.KnownTerms)
            .Include(lc => lc.KnownTopics)
            .FirstOrDefaultAsync(lc => lc.BookId == book.Id, cancellationToken);

        var recentLessons = await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => l.BookId == book.Id)
            .OrderByDescending(l => l.CreatedAtUtc)
            .Take(5)
            .Include(l => l.Hadiths)
                .ThenInclude(h => h.LessonPoints)
            .Include(l => l.Connections)
            .ToListAsync(cancellationToken);

        var appCtx = new AppLearningContext();

        if (domainContext != null)
        {
            // Format Known People
            foreach (var kp in domainContext.KnownPeople.OrderByDescending(p => p.TimesSeen))
            {
                string bio = !string.IsNullOrWhiteSpace(kp.ShortBiography)
                    ? kp.ShortBiography
                    : kp.Person.Description;

                string entry = string.IsNullOrWhiteSpace(bio)
                    ? kp.Person.Name
                    : $"{kp.Person.Name} — {bio}";

                appCtx.People.Add(entry);
            }

            // Format Known Terms
            foreach (var kt in domainContext.KnownTerms.OrderByDescending(t => t.TimesSeen))
            {
                string entry = string.IsNullOrWhiteSpace(kt.Explanation)
                    ? kt.Term
                    : $"{kt.Term} — {kt.Explanation}";

                appCtx.Terms.Add(entry);
            }

            // Format Topics
            foreach (var topic in domainContext.KnownTopics)
            {
                if (!string.IsNullOrWhiteSpace(topic.Topic))
                {
                    appCtx.Topics.Add(topic.Topic);
                }
            }
        }

        // Format Key Ideas from recent lesson points
        foreach (var l in recentLessons)
        {
            foreach (var h in l.Hadiths)
            {
                foreach (var pt in h.LessonPoints)
                {
                    if (!string.IsNullOrWhiteSpace(pt.Point) && !appCtx.KeyIdeas.Contains(pt.Point))
                    {
                        appCtx.KeyIdeas.Add(pt.Point);
                    }
                }

                if (!string.IsNullOrWhiteSpace(h.Problem) && !string.IsNullOrWhiteSpace(h.Conclusion))
                {
                    string disc = $"{h.Problem} → النتيجة: {h.Conclusion}";
                    if (!appCtx.PreviousDiscussions.Contains(disc))
                    {
                        appCtx.PreviousDiscussions.Add(disc);
                    }
                }
            }

            foreach (var conn in l.Connections)
            {
                if (!string.IsNullOrWhiteSpace(conn.Description) && !appCtx.EstablishedConnections.Contains(conn.Description))
                {
                    appCtx.EstablishedConnections.Add(conn.Description);
                }
            }
        }

        // Format Active Student Concept Mastery profile
        var studentMasteries = await _dbContext.Set<StudentConceptMastery>()
            .AsNoTracking()
            .Where(m => m.BookId == book.Id)
            .OrderByDescending(m => m.MasteryScore)
            .ThenByDescending(m => m.ExposureCount)
            .ToListAsync(cancellationToken);

        foreach (var sm in studentMasteries)
        {
            appCtx.StudentMastery.Add(new StudentConceptMasteryItem
            {
                Concept = sm.ConceptKey,
                Level = sm.LearningLevel,
                Score = sm.MasteryScore,
                ExposureCount = sm.ExposureCount,
                AssessmentCount = sm.AssessmentCount
            });
        }

        return appCtx;
    }

    public async Task<Lesson?> GetLessonByIdAsync(
        Guid lessonId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Lessons
            .AsNoTracking()
            .Include(l => l.Book)
            .Include(l => l.LessonPages)
                .ThenInclude(lp => lp.BookPage)
            .Include(l => l.Connections)
            .Include(l => l.ReviewQuestions)
            .Include(l => l.Assessments)
                .ThenInclude(a => a.Questions)
                    .ThenInclude(q => q.StudentAnswers)
                        .ThenInclude(sa => sa.AssessmentResult)
            .Include(l => l.LessonPeople)
                .ThenInclude(lp => lp.Person)
            .Include(l => l.LessonProgresses)
            .Include(l => l.Hadiths)
                .ThenInclude(h => h.Evidences)
            .Include(l => l.Hadiths)
                .ThenInclude(h => h.LessonPoints)
            .Include(l => l.Hadiths)
                .ThenInclude(h => h.HadithPeople)
                    .ThenInclude(hp => hp.Person)
            .FirstOrDefaultAsync(l => l.Id == lessonId, cancellationToken);
    }

    public async Task<List<Lesson>> GetLessonsByBookIdAsync(
        Guid bookId,
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Lessons
            .AsNoTracking()
            .Where(l => l.BookId == bookId)
            .OrderByDescending(l => l.CreatedAtUtc)
            .ThenByDescending(l => l.StartPage)
            .Include(l => l.LessonPages)
            .Include(l => l.LessonPeople)
                .ThenInclude(lp => lp.Person)
            .Include(l => l.LessonProgresses)
            .ToListAsync(cancellationToken);
    }

    public async Task<DomainLearningContext?> GetLearningContextByBookIdAsync(
        Guid bookId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.LessonLearningContexts
            .AsNoTracking()
            .Include(lc => lc.KnownPeople)
                .ThenInclude(kp => kp.Person)
            .Include(lc => lc.KnownTerms)
            .Include(lc => lc.KnownTopics)
            .FirstOrDefaultAsync(lc => lc.BookId == bookId, cancellationToken);
    }

    public async Task<List<Book>> GetBooksAsync(
        Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        Guid targetUserId = userId ?? _currentUserService?.UserId ?? User.DefaultUserId;

        var books = await _dbContext.Books
            .AsNoTracking()
            .Where(b => b.UserId == targetUserId)
            .OrderBy(b => b.Title)
            .Include(b => b.Lessons)
            .ToListAsync(cancellationToken);

        if (books.Count == 0)
        {
            await EnsureDefaultBookAsync(targetUserId, cancellationToken);
            books = await _dbContext.Books
                .AsNoTracking()
                .Where(b => b.UserId == targetUserId)
                .OrderBy(b => b.Title)
                .Include(b => b.Lessons)
                .ToListAsync(cancellationToken);
        }

        return books;
    }

    public Task<Book> EnsureDefaultBookAsync(CancellationToken cancellationToken) => EnsureDefaultBookAsync(null, cancellationToken);
    public Task<List<Book>> GetBooksAsync(CancellationToken cancellationToken) => GetBooksAsync(null, cancellationToken);
    public Task<Book> CreateBookAsync(string title, CancellationToken cancellationToken) => CreateBookAsync(title, null, cancellationToken);

    private static LearningLevel CalculateLearningLevel(int timesSeen)
    {
        // Exposure establishes only recognition.  Understanding and mastery are
        // deliberately reserved for demonstrated assessment evidence.
        return timesSeen switch
        {
            <= 1 => LearningLevel.Introduced,
            _ => LearningLevel.Familiar
        };
    }

    private static bool IsSourceBoundAssessmentQuestion(AssessmentQuestionDto question, IReadOnlyCollection<int> lessonPages)
    {
        if (string.IsNullOrWhiteSpace(question.Question) ||
            string.IsNullOrWhiteSpace(question.EvaluationGuidance) ||
            question.ExpectedConcepts.Count == 0 || question.SourcePages.Count == 0)
        {
            return false;
        }

        return question.SourcePages.All(lessonPages.Contains);
    }

    private async Task<Person> ResolveOrCreatePersonAsync(
        string name,
        string description,
        CancellationToken cancellationToken)
    {
        string normalizedName = NormalizeKey(name);

        // Check local change tracker
        var local = _dbContext.People.Local.FirstOrDefault(p => NormalizeKey(p.Name) == normalizedName);
        if (local != null) return local;

        // Check database
        var existing = await _dbContext.People
            .FirstOrDefaultAsync(p => p.Name == name || p.Name == normalizedName, cancellationToken);

        if (existing != null)
        {
            if (string.IsNullOrWhiteSpace(existing.Description) && !string.IsNullOrWhiteSpace(description))
            {
                existing.Description = description;
            }
            return existing;
        }

        var newPerson = new Person
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.People.Add(newPerson);
        return newPerson;
    }

    private static (string Name, string Description) ParsePersonString(string rawPerson)
    {
        var match = PersonSplitRegex.Match(rawPerson);
        if (match.Success)
        {
            return (match.Groups[1].Value.Trim(), match.Groups[2].Value.Trim());
        }
        return (rawPerson.Trim(), string.Empty);
    }

    private static List<(string Term, string Explanation)> ExtractInlineTerms(string text)
    {
        var results = new List<(string Term, string Explanation)>();
        if (string.IsNullOrWhiteSpace(text)) return results;

        var matches = InlineTermRegex.Matches(text);
        foreach (Match match in matches)
        {
            string rawCandidate = match.Groups[1].Value.Trim();
            string candidateExplanation = match.Groups[2].Value.Trim();

            // Ignore verse references, page numbers, or citations like (ص: 12), (رواه البخاري), (الآية: 5)
            if (candidateExplanation.Contains(':') ||
                candidateExplanation.StartsWith("رواه", StringComparison.OrdinalIgnoreCase) ||
                candidateExplanation.StartsWith("أخرجه", StringComparison.OrdinalIgnoreCase) ||
                candidateExplanation.StartsWith("انظر", StringComparison.OrdinalIgnoreCase) ||
                int.TryParse(candidateExplanation, out _))
            {
                continue;
            }

            string? cleanTerm = ConceptSanitizer.CleanAndValidate(rawCandidate);
            if (cleanTerm != null && candidateExplanation.Length >= 4 && candidateExplanation.Length <= 250)
            {
                results.Add((cleanTerm, candidateExplanation));
            }
        }

        return results;
    }

    private static string NormalizeKey(string text)
    {
        return text.Trim()
            .Replace("أ", "ا")
            .Replace("إ", "ا")
            .Replace("آ", "ا")
            .Replace("ة", "ه")
            .Replace("ى", "ي");
    }

    [GeneratedRegex(@"(?<=[\s،؛.:\-\n\r«»""']|^)([\p{IsArabic}]{2,25}(?:\s+[\p{IsArabic}]{2,20}){0,2})\s*\(([^)]+)\)")]
    private static partial Regex GeneratedInlineTermRegex();

    [GeneratedRegex(@"^(.*?)\s*(?:—|–|-)\s*(.*)$")]
    private static partial Regex GeneratedPersonSplitRegex();
}
