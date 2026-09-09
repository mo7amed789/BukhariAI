using BukhariAI.Application.Abstractions;
using BukhariAI.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace BukhariAI.Application.Lessons.GenerateLesson;

public sealed class GenerateLessonHandler
{
    private readonly IPdfExtractionService _pdfExtractionService;
    private readonly IAiService _aiService;
    private readonly ILessonPersistenceService? _persistenceService;
    private readonly ILogger<GenerateLessonHandler> _logger;
    private readonly IAdaptiveLearningService? _adaptiveLearning;

    public GenerateLessonHandler(
        IPdfExtractionService pdfExtractionService,
        IAiService aiService,
        ILogger<GenerateLessonHandler> logger,
        ILessonPersistenceService? persistenceService = null,
        IAdaptiveLearningService? adaptiveLearning = null)
    {
        _pdfExtractionService = pdfExtractionService;
        _aiService = aiService;
        _logger = logger;
        _persistenceService = persistenceService;
        _adaptiveLearning = adaptiveLearning;
    }

    public async Task<GenerateLessonResponse> HandleAsync(
        GenerateLessonCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.PdfStream);

        _logger.LogInformation(
            "Lesson generation started. File: '{FileName}', Requested StartPage: {StartPage}, Requested EndPage: {EndPage}.",
            command.FileName,
            command.StartPage,
            command.EndPage);

        // 1. Render the requested pages. No text is extracted locally.
        PdfExtractionResult extractionResult = await _pdfExtractionService.ExtractAsync(
            command.PdfStream,
            command.StartPage,
            command.EndPage,
            cancellationToken);

        if (extractionResult.Pages.Count == 0)
        {
            throw new InvalidOperationException("No content could be extracted from the requested page range.");
        }

        List<int> processedPageNumbers = new(extractionResult.Pages.Count);

        foreach (var page in extractionResult.Pages)
        {
            processedPageNumbers.Add(page.PageNumber);
            _logger.LogInformation(
                "[Vision DataFlow] Page {PageNumber}: screenshot prepared ({Bytes} bytes); no local text extraction performed.",
                page.PageNumber,
                page.ImageBytes.Length);
        }

        _logger.LogInformation(
            "Prepared {Count} page screenshots. Sending them directly to the Vision AI service.",
            extractionResult.Pages.Count);

        // 3. Resolve active educational memory context
        LessonLearningContext? educationalContext = command.PreviousContext;
        if (educationalContext == null && _persistenceService != null)
        {
            try
            {
                educationalContext = await _persistenceService.GetActiveEducationalContextAsync(command.BookId, cancellationToken);
                _logger.LogInformation(
                    "Retrieved active educational context for AI generation: {PeopleCount} people, {TermsCount} terms, {TopicsCount} topics.",
                    educationalContext.People.Count,
                    educationalContext.Terms.Count,
                    educationalContext.Topics.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load active educational context from database. Proceeding without prior context: {Message}", ex.Message);
            }
        }

        if (educationalContext is not null && command.BookId.HasValue && _adaptiveLearning is not null)
        {
            var adaptiveState = await _adaptiveLearning.GetAdaptiveStateAsync(command.BookId.Value, educationalContext.LearningIntent, cancellationToken);
            educationalContext.AdaptiveDecisions = adaptiveState.Concepts.Where(c => c.Action != AdaptiveLearningAction.NoAction).ToList();
        }

        // 4. AI Service generation
        _logger.LogInformation("AI generation started.");
        GenerateLessonResponse lessonResponse = await _aiService.GenerateLessonAsync(
            extractionResult.Pages,
            educationalContext,
            cancellationToken);
        _logger.LogInformation("AI generation completed. Lesson Title: '{Title}'.", lessonResponse.Title);

        // 5. Enrich metadata and ensure SourcePages are preserved
        var finalSourcePages = lessonResponse.SourcePages.Count > 0
            ? lessonResponse.SourcePages
            : processedPageNumbers;

        var response = new GenerateLessonResponse
        {
            Title = lessonResponse.Title,
            Overview = lessonResponse.Overview,
            HistoricalContext = lessonResponse.HistoricalContext,
            SourcePages = finalSourcePages,
            Hadiths = lessonResponse.Hadiths,
            Connections = lessonResponse.Connections,
            ReviewQuestions = lessonResponse.ReviewQuestions,
            AssessmentQuestions = lessonResponse.AssessmentQuestions,
            Metadata = new LessonMetadata
            {
                StartPage = command.StartPage,
                EndPage = command.EndPage,
                TotalPagesProcessed = extractionResult.Pages.Count,
                UsedOcr = false,
                GeneratedAtUtc = DateTime.UtcNow
            }
        };

        // 6. Persist lesson and update progressive learning memory
        if (_persistenceService != null)
        {
            try
            {
                var persistedLessonId = await _persistenceService.PersistLessonAsync(response, extractionResult, command.BookId, cancellationToken);
                response.LessonId = persistedLessonId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist lesson '{Title}' to database: {Message}", response.Title, ex.Message);
            }
        }

        return response;
    }

    public async Task<GenerateLessonResponse> HandleFromTextAsync(
        GenerateLessonFromTextCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.SourceText);

        _logger.LogInformation(
            "Text-based lesson generation started. Text length: {Length} chars, Pages: {StartPage}-{EndPage}.",
            command.SourceText.Length,
            command.StartPage,
            command.EndPage);

        LessonLearningContext? educationalContext = command.PreviousContext;
        if (educationalContext == null && _persistenceService != null)
        {
            try
            {
                educationalContext = await _persistenceService.GetActiveEducationalContextAsync(command.BookId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load active educational context from database: {Message}", ex.Message);
            }
        }

        if (educationalContext is not null && command.BookId.HasValue && _adaptiveLearning is not null)
        {
            var adaptiveState = await _adaptiveLearning.GetAdaptiveStateAsync(command.BookId.Value, educationalContext.LearningIntent, cancellationToken);
            educationalContext.AdaptiveDecisions = adaptiveState.Concepts.Where(c => c.Action != AdaptiveLearningAction.NoAction).ToList();
        }

        _logger.LogInformation("AI text generation started.");
        GenerateLessonResponse lessonResponse = await _aiService.GenerateLessonFromTextAsync(
            command.SourceText,
            educationalContext,
            cancellationToken);

        int startPage = command.StartPage > 0 ? command.StartPage : 1;
        int endPage = command.EndPage >= startPage ? command.EndPage : startPage;

        var sourcePages = Enumerable.Range(startPage, endPage - startPage + 1).ToList();

        var response = new GenerateLessonResponse
        {
            Title = lessonResponse.Title,
            Overview = lessonResponse.Overview,
            HistoricalContext = lessonResponse.HistoricalContext,
            SourcePages = lessonResponse.SourcePages.Count > 0 ? lessonResponse.SourcePages : sourcePages,
            Hadiths = lessonResponse.Hadiths,
            Connections = lessonResponse.Connections,
            ReviewQuestions = lessonResponse.ReviewQuestions,
            AssessmentQuestions = lessonResponse.AssessmentQuestions,
            Metadata = new LessonMetadata
            {
                StartPage = startPage,
                EndPage = endPage,
                TotalPagesProcessed = sourcePages.Count,
                UsedOcr = false,
                GeneratedAtUtc = DateTime.UtcNow
            }
        };

        if (_persistenceService != null)
        {
            try
            {
                var dummyExtraction = new PdfExtractionResult
                {
                    Pages = sourcePages.Select(p => new PageScreenshot { PageNumber = p, ImageBytes = [], MediaType = "image/png" }).ToList()
                };
                var persistedLessonId = await _persistenceService.PersistLessonAsync(response, dummyExtraction, command.BookId, cancellationToken);
                response.LessonId = persistedLessonId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist text lesson '{Title}' to database: {Message}", response.Title, ex.Message);
            }
        }

        return response;
    }

    public async Task<GenerateLessonResponse> HandleFromImagesAsync(
        GenerateLessonFromImagesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Images.Count == 0) throw new ArgumentException("At least one image is required.", nameof(command));

        _logger.LogInformation(
            "Images-based lesson generation started. Image count: {Count}, Pages: {StartPage}-{EndPage}.",
            command.Images.Count,
            command.StartPage,
            command.EndPage);

        LessonLearningContext? educationalContext = command.PreviousContext;
        if (educationalContext == null && _persistenceService != null)
        {
            try
            {
                educationalContext = await _persistenceService.GetActiveEducationalContextAsync(command.BookId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not load active educational context from database: {Message}", ex.Message);
            }
        }

        if (educationalContext is not null && command.BookId.HasValue && _adaptiveLearning is not null)
        {
            var adaptiveState = await _adaptiveLearning.GetAdaptiveStateAsync(command.BookId.Value, educationalContext.LearningIntent, cancellationToken);
            educationalContext.AdaptiveDecisions = adaptiveState.Concepts.Where(c => c.Action != AdaptiveLearningAction.NoAction).ToList();
        }

        _logger.LogInformation("AI vision generation started for pasted images.");
        GenerateLessonResponse lessonResponse = await _aiService.GenerateLessonAsync(
            command.Images,
            educationalContext,
            cancellationToken);

        int startPage = command.StartPage > 0 ? command.StartPage : 1;
        int endPage = command.EndPage >= startPage ? command.EndPage : (startPage + command.Images.Count - 1);
        var sourcePages = Enumerable.Range(startPage, command.Images.Count).ToList();

        var response = new GenerateLessonResponse
        {
            Title = lessonResponse.Title,
            Overview = lessonResponse.Overview,
            HistoricalContext = lessonResponse.HistoricalContext,
            SourcePages = lessonResponse.SourcePages.Count > 0 ? lessonResponse.SourcePages : sourcePages,
            Hadiths = lessonResponse.Hadiths,
            Connections = lessonResponse.Connections,
            ReviewQuestions = lessonResponse.ReviewQuestions,
            AssessmentQuestions = lessonResponse.AssessmentQuestions,
            Metadata = new LessonMetadata
            {
                StartPage = startPage,
                EndPage = endPage,
                TotalPagesProcessed = command.Images.Count,
                UsedOcr = false,
                GeneratedAtUtc = DateTime.UtcNow
            }
        };

        if (_persistenceService != null)
        {
            try
            {
                var extraction = new PdfExtractionResult
                {
                    Pages = command.Images.ToList()
                };
                var persistedLessonId = await _persistenceService.PersistLessonAsync(response, extraction, command.BookId, cancellationToken);
                response.LessonId = persistedLessonId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist image lesson '{Title}' to database: {Message}", response.Title, ex.Message);
            }
        }

        return response;
    }
}
