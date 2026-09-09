using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Mastery;
using BukhariAI.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;
using AppLearningContext = BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext;

namespace BukhariAI.UnitTests;

public class StudentMasteryEngineTests
{
    private readonly ITestOutputHelper _output;

    public StudentMasteryEngineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static BukhariDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: $"BukhariAI_Mastery_{Guid.NewGuid()}")
            .EnableSensitiveDataLogging()
            .Options;

        return new BukhariDbContext(options);
    }

    // =========================================================================
    // TEST 1: New concept starts at Introduced
    // =========================================================================
    [Fact]
    public async Task NewConcept_WhenFirstExposed_StartsAtIntroducedLevelWithBaseScore()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var masteryEngine = new MasteryEngineService(context, NullLogger<MasteryEngineService>.Instance);
        var bookId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        // Act
        var masteries = await masteryEngine.RecordConceptExposuresAsync(
            bookId,
            lessonId,
            ["الدباغ", "القرظ"]);

        // Assert
        masteries.Should().HaveCount(2);
        var dibagh = masteries.First(m => m.ConceptKey == "الدباغ");
        dibagh.LearningLevel.Should().Be(LearningLevel.Introduced);
        dibagh.ExposureCount.Should().Be(1);
        dibagh.AssessmentCount.Should().Be(0);
        dibagh.CorrectAnswerCount.Should().Be(0);
        dibagh.MasteryScore.Should().Be(0.20);
        dibagh.FirstIntroducedLessonId.Should().Be(lessonId);
    }

    // =========================================================================
    // TEST 2: Assessment creates StudentConceptMastery
    // =========================================================================
    [Fact]
    public async Task Assessment_WhenEvaluatedForNewConcept_CreatesStudentConceptMastery()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var masteryEngine = new MasteryEngineService(context, NullLogger<MasteryEngineService>.Instance);
        var bookId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        var evalResult = new AssessmentEvaluationResult
        {
            Score = 0.80,
            Level = LearningLevel.Understood,
            UnderstoodConcepts = ["الولاء"],
            MissingConcepts = [],
            Misconceptions = [],
            Feedback = "إجابة ممتازة وواضحة."
        };

        // Act
        var updated = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId,
            lessonId,
            ["الولاء"],
            evalResult);

        // Assert
        updated.Should().HaveCount(1);
        var wala = updated.First();
        wala.ConceptKey.Should().Be("الولاء");
        wala.AssessmentCount.Should().Be(1);
        wala.CorrectAnswerCount.Should().Be(1);
        wala.MasteryScore.Should().BeGreaterThan(0.20);
        wala.LastAssessedAt.Should().NotBeNull();
        wala.LastAssessedLessonId.Should().Be(lessonId);

        var inDb = await context.StudentConceptMasteries.FirstOrDefaultAsync(m => m.ConceptKey == "الولاء");
        inDb.Should().NotBeNull();
        inDb!.AssessmentCount.Should().Be(1);
    }

    // =========================================================================
    // TEST 3: Correct answer increases mastery
    // =========================================================================
    [Fact]
    public async Task CorrectAnswer_IncreasesMasteryScore_AndPreservesExposureCount()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var masteryEngine = new MasteryEngineService(context, NullLogger<MasteryEngineService>.Instance);
        var bookId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        // Start with initial exposure
        await masteryEngine.RecordConceptExposuresAsync(bookId, lessonId, ["دباغ الميتة"]);

        var evalResult = new AssessmentEvaluationResult
        {
            Score = 0.90,
            Level = LearningLevel.Mastered,
            UnderstoodConcepts = ["دباغ الميتة"],
            Feedback = "فهم دقيق لشروط الدباغ وتطهير الجلد."
        };

        // Act
        var updated = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId,
            lessonId,
            ["دباغ الميتة"],
            evalResult);

        // Assert
        var mastery = updated.First();
        mastery.ExposureCount.Should().Be(1);
        mastery.AssessmentCount.Should().Be(1);
        mastery.CorrectAnswerCount.Should().Be(1);
        mastery.MasteryScore.Should().BeApproximately(0.488, 0.05);
        mastery.LearningLevel.Should().Be(LearningLevel.Familiar, "One correct answer produces Familiar, not Mastered directly.");
    }

    // =========================================================================
    // TEST 4: Repeated correct answers advance learning level
    // =========================================================================
    [Fact]
    public async Task RepeatedCorrectAnswers_AdvanceLearningLevel_FromIntroducedToFamiliarToUnderstoodToMastered()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var masteryEngine = new MasteryEngineService(context, NullLogger<MasteryEngineService>.Instance);
        var bookId = Guid.NewGuid();
        var lesson1Id = Guid.NewGuid();
        var lesson2Id = Guid.NewGuid();
        var lesson3Id = Guid.NewGuid();
        var lesson4Id = Guid.NewGuid();

        // 0. Initial Exposure -> Introduced
        var initial = await masteryEngine.RecordConceptExposuresAsync(bookId, lesson1Id, ["تخصيص العموم"]);
        initial.First().LearningLevel.Should().Be(LearningLevel.Introduced);

        // 1. Assessment 1 (Correct) -> Familiar
        var r1 = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lesson1Id, ["تخصيص العموم"],
            new AssessmentEvaluationResult { Score = 0.90, UnderstoodConcepts = ["تخصيص العموم"] });
        r1.First().LearningLevel.Should().Be(LearningLevel.Familiar);
        r1.First().CorrectAnswerCount.Should().Be(1);

        // 2. Assessment 2 (Correct) -> Understood
        var r2 = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lesson2Id, ["تخصيص العموم"],
            new AssessmentEvaluationResult { Score = 0.95, UnderstoodConcepts = ["تخصيص العموم"] });
        r2.First().LearningLevel.Should().Be(LearningLevel.Understood);
        r2.First().CorrectAnswerCount.Should().Be(2);

        // 3. Assessment 3 (Correct) -> Understood/Mastered
        var r3 = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lesson3Id, ["تخصيص العموم"],
            new AssessmentEvaluationResult { Score = 0.95, UnderstoodConcepts = ["تخصيص العموم"] });
        r3.First().CorrectAnswerCount.Should().Be(3);

        // 4. Assessment 4 (Correct) -> Mastered
        var r4 = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lesson4Id, ["تخصيص العموم"],
            new AssessmentEvaluationResult { Score = 1.0, UnderstoodConcepts = ["تخصيص العموم"] });
        r4.First().LearningLevel.Should().Be(LearningLevel.Mastered);
        r4.First().MasteryScore.Should().BeGreaterThanOrEqualTo(0.85);
    }

    // =========================================================================
    // TEST 5: Incorrect answers reduce mastery confidence without deleting history
    // =========================================================================
    [Fact]
    public async Task IncorrectAnswers_ReduceConfidence_WithoutDeletingHistoricalKnowledgeOrExposures()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var masteryEngine = new MasteryEngineService(context, NullLogger<MasteryEngineService>.Instance);
        var bookId = Guid.NewGuid();
        var lessonId = Guid.NewGuid();

        // Give student 2 initial correct answers
        await masteryEngine.RecordConceptExposuresAsync(bookId, lessonId, ["مرجع الضمير"]);
        await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lessonId, ["مرجع الضمير"],
            new AssessmentEvaluationResult { Score = 0.90, UnderstoodConcepts = ["مرجع الضمير"] });
        var step2 = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lessonId, ["مرجع الضمير"],
            new AssessmentEvaluationResult { Score = 0.90, UnderstoodConcepts = ["مرجع الضمير"] });

        double scoreBeforeFail = step2.First().MasteryScore;
        scoreBeforeFail.Should().BeGreaterThan(0.60);

        // Act - Student has misconception / fails assessment
        var failResult = new AssessmentEvaluationResult
        {
            Score = 0.20,
            Level = LearningLevel.Introduced,
            UnderstoodConcepts = [],
            MissingConcepts = ["مرجع الضمير"],
            Misconceptions = ["مرجع الضمير"],
            Feedback = "خلط بين مرجع الضمير في (هو حرام) ومرجع الضمير في بيع الميتة."
        };

        var afterFail = await masteryEngine.UpdateMasteryAfterAssessmentAsync(
            bookId, lessonId, ["مرجع الضمير"], failResult);

        // Assert
        var mastery = afterFail.First();
        mastery.MasteryScore.Should().BeLessThan(scoreBeforeFail);
        mastery.ExposureCount.Should().Be(1, "Exposure count must be preserved.");
        mastery.AssessmentCount.Should().Be(3, "Assessment count tracks all attempts.");
        mastery.CorrectAnswerCount.Should().Be(2, "Historical correct answers are preserved.");
        mastery.FirstIntroducedLessonId.Should().Be(lessonId, "First introduced lesson ID is preserved.");
    }

    // =========================================================================
    // TEST 6: Different wording evaluated as correct
    // =========================================================================
    [Fact]
    public void EvaluatorPrompt_InstructsGemini_ToRewardConceptualUnderstandingRegardlessOfWording()
    {
        // Arrange & Act
        var evaluator = new AiAssessmentEvaluator(
            new HttpClient(),
            Options.Create(new AiOptions()),
            NullLogger<AiAssessmentEvaluator>.Instance);

        // We verify the system prompt instructions directly
        // Reflection to inspect private BuildSystemPrompt
        var method = typeof(AiAssessmentEvaluator).GetMethod("BuildSystemPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        string systemPrompt = (string)method!.Invoke(null, null)!;

        // Assert
        systemPrompt.Should().Contain("الطالب قد يستخدم صياغة مختلفة تماماً أو أسلوبه الخاص");
        systemPrompt.Should().Contain("جوهر فهمه صحيحاً ودقيقاً 100%");
        systemPrompt.Should().Contain("قيّم الفهم المفاهيمي العميق (Conceptual Understanding)");
    }

    // =========================================================================
    // TEST 7: Keyword-only answers are not automatically considered correct
    // =========================================================================
    [Fact]
    public void EvaluatorPrompt_InstructsGemini_ToDetectMisconceptionsEvenIfKeywordsAreMentioned()
    {
        // Arrange & Act
        var method = typeof(AiAssessmentEvaluator).GetMethod("BuildSystemPrompt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        string systemPrompt = (string)method!.Invoke(null, null)!;

        // Assert
        systemPrompt.Should().Contain("رؤية المفهوم أو حفظ ألفاظه لا يعني فهمه");
        systemPrompt.Should().Contain("مطابقة الكلمات المفتاحية (Keyword Matching) وحدها لا تستحق الدرجة");
        systemPrompt.Should().Contain("الطالب قد يذكر جميع المصطلحات والكلمات المفتاحية");
        systemPrompt.Should().Contain("لا تنخدع بالكلمات، واكشف سوء الفهم");
    }

    // =========================================================================
    // TEST 8: Adaptive memory is included in Gemini prompt
    // =========================================================================
    [Fact]
    public void LessonPromptBuilder_BuildUserPrompt_IncludesStudentMasterySectionWithScoresAndLevels()
    {
        // Arrange
        var builder = new LessonPromptBuilder();
        var context = new AppLearningContext
        {
            StudentMastery =
            [
                new StudentConceptMasteryItem
                {
                    Concept = "الدباغ",
                    Level = LearningLevel.Mastered,
                    Score = 0.92,
                    ExposureCount = 5,
                    AssessmentCount = 4
                },
                new StudentConceptMasteryItem
                {
                    Concept = "الولاء",
                    Level = LearningLevel.Understood,
                    Score = 0.74,
                    ExposureCount = 3,
                    AssessmentCount = 2
                },
                new StudentConceptMasteryItem
                {
                    Concept = "جلود السباع",
                    Level = LearningLevel.Familiar,
                    Score = 0.48,
                    ExposureCount = 2,
                    AssessmentCount = 1
                },
                new StudentConceptMasteryItem
                {
                    Concept = "مرجع الضمير في \"هو حرام\"",
                    Level = LearningLevel.Introduced,
                    Score = 0.20,
                    ExposureCount = 1,
                    AssessmentCount = 0
                }
            ]
        };

        // Act
        string userPrompt = builder.BuildUserPrompt("نص المستخرج للصفحة 115", context);

        // Assert
        userPrompt.Should().Contain("### مستوى إتقان الطالب الحالي للمفاهيم (STUDENT MASTERY):");
        userPrompt.Should().Contain("Concept: الدباغ");
        userPrompt.Should().Contain("Level: Mastered");
        userPrompt.Should().Contain("Score: 0.92");

        userPrompt.Should().Contain("Concept: الولاء");
        userPrompt.Should().Contain("Level: Understood");
        userPrompt.Should().Contain("Score: 0.74");

        userPrompt.Should().Contain("Concept: جلود السباع");
        userPrompt.Should().Contain("Level: Familiar");
        userPrompt.Should().Contain("Score: 0.48");

        userPrompt.Should().Contain("Concept: مرجع الضمير في \"هو حرام\"");
        userPrompt.Should().Contain("Level: Introduced");
        userPrompt.Should().Contain("Score: 0.20");
    }

    // =========================================================================
    // TEST 9 & 10: Mastered concepts not repeatedly explained from zero; Introduced explained from first principles
    // =========================================================================
    [Fact]
    public void LessonPromptBuilder_BuildSystemPrompt_ContainsPedagogicalAdaptationRules()
    {
        // Arrange
        var builder = new LessonPromptBuilder();

        // Act
        string systemPrompt = builder.BuildSystemPrompt();

        // Assert
        // 9. Mastered concepts
        systemPrompt.Should().Contain("المفاهيم المتقنة (Mastered):");
        systemPrompt.Should().Contain("لا تُعد شرحها أو تعريفها من الصفر كأن الطالب يراها لأول مرة");
        systemPrompt.Should().Contain("استخدمها كأسس وبديهيات معرفية معلومة يُبنى عليها مباشرة");

        // 10. Introduced / Weak concepts
        systemPrompt.Should().Contain("المفاهيم المبتدئة (Introduced) أو المفاهيم الضعيفة (Weak / Low Score):");
        systemPrompt.Should().Contain("اشرحها تفصيلياً من المبادئ الأولى (First Principles)");

        // Understood & Familiar
        systemPrompt.Should().Contain("المفاهيم المفهومة (Understood):");
        systemPrompt.Should().Contain("المفاهيم المألوفة (Familiar):");

        // Continuity
        systemPrompt.Should().Contain("الاستمرارية التعليمية الطبيعية (Educational Continuity):");
        systemPrompt.Should().Contain("كما عرفنا في الدرس السابق أن الدباغ يطهر الجلد");
        systemPrompt.Should().Contain("تنبيه حاسم: لا تربط ولا تُشر إلى دروس سابقة إلا إذا كانت الذاكرة التعليمية السابقة تدعم ذلك الارتباط فعلاً");
    }

    // =========================================================================
    // TEST 11: Data persists correctly in SQL Server (Assessment & Mastery entities)
    // =========================================================================
    [Fact]
    public async Task PersistLessonAsync_PersistsAssessmentQuestionsAndConceptMasteryInDatabase()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var service = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);

        var lessonResponse = new GenerateLessonResponse
        {
            Title = "درس تجريبي في أحكام الميتة والدباغ",
            Overview = "نظرة عامة على أحكام الدباغ والشحوم.",
            HistoricalContext = "لا يوجد سياق تاريخي إضافي موثق في النص المقدم.",
            SourcePages = [110, 111],
            Hadiths =
            [
                new HadithExplanation
                {
                    Reference = "حديث ميمونة في الدباغ",
                    Problem = "هل يطهر جلد الميتة بالدباغ؟",
                    Conclusion = "يطهر جلد الميتة بالدباغ.",
                    EasyExplanation = "الدباغ (معالجة جلد الميتة بالماء والقرظ حتى يطهر) مشروع.",
                    Evidence = [new Evidence { Text = "إذا دبغ الإهاب فقد طهر", Role = "دليل أصلي" }]
                }
            ],
            AssessmentQuestions =
            [
                new AssessmentQuestionDto
                {
                    Question = "ما دلالة قول النبي ﷺ «إذا دبغ الإهاب فقد طهر» على طهارة جلد الميتة؟",
                    QuestionType = "EvidenceAnalysis",
                    Difficulty = "Intermediate",
                    ExpectedConcepts = ["الدباغ", "الإهاب", "تطهير الميتة"],
                    SourcePages = [110],
                    EvaluationGuidance = "يجب أن يوضح الطالب أن الحديث علّق الحكم بالدباغ مما يدل على أنه سبب التطهير."
                }
            ]
        };

        var extractionResult = new PdfExtractionResult
        {
            Pages =
            [
                new ExtractedPage { PageNumber = 110, Text = "إذا دبغ الإهاب فقد طهر..." },
                new ExtractedPage { PageNumber = 111, Text = "الشحوم لا تباع..." }
            ]
        };

        // Act
        Guid lessonId = await service.PersistLessonAsync(lessonResponse, extractionResult);

        // Assert
        var savedLesson = await service.GetLessonByIdAsync(lessonId);
        savedLesson.Should().NotBeNull();
        savedLesson!.Assessments.Should().HaveCount(1);

        var assessment = savedLesson.Assessments.First();
        assessment.Title.Should().Contain("تقييم فهم درس");
        assessment.Questions.Should().HaveCount(1);

        var question = assessment.Questions.First();
        question.Question.Should().Contain("دلالة قول النبي");
        question.ExpectedConceptsCsv.Should().Contain("الدباغ");
        question.EvaluationGuidance.Should().Contain("سبب التطهير");

        // Assert StudentConceptMasteries created for exposures
        var masteries = await context.StudentConceptMasteries.ToListAsync();
        masteries.Should().NotBeEmpty();
        masteries.Should().Contain(m => m.ConceptKey == "الدباغ" && m.LearningLevel == LearningLevel.Introduced && m.ExposureCount >= 1);
    }

    // =========================================================================
    // TEST 12: End-to-End Assessment Submission and Evaluation Flow with Mock Evaluator
    // =========================================================================
    [Fact]
    public async Task AssessmentService_SubmitAnswer_EvaluatesAnswerAndUpdatesMastery()
    {
        // Arrange
        using var context = CreateInMemoryDbContext();
        var persistenceService = new LessonPersistenceService(context, NullLogger<LessonPersistenceService>.Instance);
        var masteryEngine = new MasteryEngineService(context, NullLogger<MasteryEngineService>.Instance);

        var mockEvaluator = new Mock<IAssessmentEvaluator>();
        mockEvaluator.Setup(e => e.EvaluateAnswerAsync(It.IsAny<AssessmentEvaluationInput>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AssessmentEvaluationResult
            {
                Score = 0.85,
                Level = LearningLevel.Understood,
                UnderstoodConcepts = ["الدباغ", "الإهاب"],
                MissingConcepts = [],
                Misconceptions = [],
                Feedback = "إجابة متميزة بينت أثر الدباغ في تطهير الإهاب."
            });

        var assessmentService = new AssessmentService(
            context,
            mockEvaluator.Object,
            masteryEngine,
            NullLogger<AssessmentService>.Instance);

        var book = await persistenceService.EnsureDefaultBookAsync();

        var lessonResponse = new GenerateLessonResponse
        {
            Title = "درس الدباغ",
            Overview = "شرح أحكام الدباغ",
            HistoricalContext = "سياق تاريخي",
            SourcePages = [110],
            Hadiths =
            [
                new HadithExplanation
                {
                    Reference = "حديث ميمونة",
                    Problem = "حكم الدباغ",
                    Conclusion = "الدباغ مطهر",
                    EasyExplanation = "شرح الدباغ"
                }
            ],
            AssessmentQuestions =
            [
                new AssessmentQuestionDto
                {
                    Question = "اشرح كيف يطهر الدباغ جلد الميتة وفق الحديث؟",
                    QuestionType = "ConceptualExplanation",
                    Difficulty = "Intermediate",
                    ExpectedConcepts = ["الدباغ", "الإهاب"],
                    SourcePages = [110],
                    EvaluationGuidance = "معيار التقييم"
                }
            ]
        };

        var extractionResult = new PdfExtractionResult
        {
            Pages = [new ExtractedPage { PageNumber = 110, Text = "إذا دبغ الإهاب فقد طهر" }]
        };

        Guid lessonId = await persistenceService.PersistLessonAsync(lessonResponse, extractionResult, book.Id);
        var lesson = await persistenceService.GetLessonByIdAsync(lessonId);
        var questionId = lesson!.Assessments.First().Questions.First().Id;

        // Act - Submit student answer
        var submitRequest = new SubmitStudentAnswerRequest
        {
            QuestionId = questionId,
            StudentAnswer = "الدباغ ينزع الخبث والرطوبات النجسة من جلد الميتة فيصير طاهراً صالحاً للاستعمال كما بين الحديث في لفظ الإهاب."
        };

        var response = await assessmentService.SubmitAnswerAsync(submitRequest);

        // Assert
        response.Should().NotBeNull();
        response.Score.Should().Be(0.85);
        response.Level.Should().Be(LearningLevel.Understood);
        response.UnderstoodConcepts.Should().Contain("الدباغ");
        response.Feedback.Should().Contain("إجابة متميزة");
        response.UpdatedMasteries.Should().NotBeEmpty();

        var dibaghMastery = response.UpdatedMasteries.First(m => m.ConceptKey == "الدباغ");
        dibaghMastery.AssessmentCount.Should().Be(1);
        dibaghMastery.CorrectAnswerCount.Should().Be(1);
        dibaghMastery.MasteryScore.Should().BeGreaterThan(0.20);
        dibaghMastery.LearningLevel.Should().Be(LearningLevel.Familiar);

        // Verify database persistence of Answer and Result
        var answerInDb = await context.StudentAnswers.Include(a => a.AssessmentResult).FirstOrDefaultAsync(a => a.Id == response.AnswerId);
        answerInDb.Should().NotBeNull();
        answerInDb!.AnswerText.Should().Contain("الدباغ ينزع الخبث");
        answerInDb.AssessmentResult.Should().NotBeNull();
        answerInDb.AssessmentResult!.Score.Should().Be(0.85);
    }
}
