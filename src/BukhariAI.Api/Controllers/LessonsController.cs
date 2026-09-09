using BukhariAI.Api.Helpers;
using BukhariAI.Api.Models;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Application.Lessons.Chat;
using BukhariAI.Application.Lessons.Biography;
using BukhariAI.Application.Assessments;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.Pdf;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class LessonsController : ControllerBase
{
    private readonly GenerateLessonHandler _handler;
    private readonly ILessonPersistenceService _persistenceService;
    private readonly PdfExtractionOptions _pdfOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<LessonsController> _logger;
    private readonly IStudentLearningService _studentLearning;
    private readonly ILessonChatService _chatService;
    private readonly IPersonBiographyService? _biographyService;
    private readonly ICurrentUserService? _currentUserService;

    // Compatibility constructor used by existing controller tests.
    public LessonsController(
        GenerateLessonHandler handler,
        ILessonPersistenceService persistenceService,
        IOptions<PdfExtractionOptions> pdfOptions,
        IWebHostEnvironment environment,
        ILogger<LessonsController> logger)
        : this(handler, persistenceService, pdfOptions, environment, null!, null!, null!, logger, null)
    {
    }

    public LessonsController(
        GenerateLessonHandler handler,
        ILessonPersistenceService persistenceService,
        IOptions<PdfExtractionOptions> pdfOptions,
        IWebHostEnvironment environment,
        IStudentLearningService studentLearning,
        ILessonChatService chatService,
        ILogger<LessonsController> logger)
        : this(handler, persistenceService, pdfOptions, environment, studentLearning, chatService, null!, logger, null)
    {
    }

    [ActivatorUtilitiesConstructor]
    public LessonsController(
        GenerateLessonHandler handler,
        ILessonPersistenceService persistenceService,
        IOptions<PdfExtractionOptions> pdfOptions,
        IWebHostEnvironment environment,
        IStudentLearningService studentLearning,
        ILessonChatService chatService,
        IPersonBiographyService biographyService,
        ILogger<LessonsController> logger,
        ICurrentUserService? currentUserService = null)
    {
        _handler = handler;
        _persistenceService = persistenceService;
        _pdfOptions = pdfOptions.Value;
        _environment = environment;
        _logger = logger;
        _studentLearning = studentLearning;
        _chatService = chatService;
        _biographyService = biographyService;
        _currentUserService = currentUserService;
    }

    private Guid CurrentUserId => _currentUserService?.UserId ?? BukhariAI.Domain.Entities.User.DefaultUserId;

    /// <summary>
    /// Generates a structured educational lesson from a selected page range of a Sahih al-Bukhari PDF.
    /// </summary>
    /// <param name="request">The multipart form payload containing the PDF file and page range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Structured educational lesson JSON.</returns>
    [HttpPost("generate")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(500 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 500 * 1024 * 1024)]
    [ProducesResponseType(typeof(GenerateLessonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateLesson(
        [FromForm] GenerateLessonFormRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Pdf == null || request.Pdf.Length == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid file",
                Detail = "A valid non-empty PDF file must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        _logger.LogInformation(
            "File received. Name: '{FileName}', Size: {FileSize} bytes.",
            request.Pdf.FileName,
            request.Pdf.Length);

        _logger.LogInformation(
            "Start page: {StartPage}, End page: {EndPage}.",
            request.StartPage,
            request.EndPage);

        string fileExtension = Path.GetExtension(request.Pdf.FileName);
        if (!string.Equals(fileExtension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid file type",
                Detail = "Only files with a .pdf extension are supported.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        using (var pdfStream = request.Pdf.OpenReadStream())
        {
            if (!FileValidationHelper.IsValidPdf(pdfStream))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Invalid PDF file",
                    Detail = "The uploaded file does not have a valid PDF header (%PDF-).",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        if (request.StartPage < 1)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page range",
                Detail = "Start page must be greater than or equal to 1.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (request.EndPage < request.StartPage)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page range",
                Detail = $"End page ({request.EndPage}) cannot be less than start page ({request.StartPage}).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        int requestedPageCount = request.EndPage - request.StartPage + 1;
        if (requestedPageCount > _pdfOptions.MaximumPageRange)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Page range too large",
                Detail = $"Requested range of {requestedPageCount} pages exceeds the maximum allowed limit of {_pdfOptions.MaximumPageRange} pages.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            await using Stream pdfStream = request.Pdf.OpenReadStream();

            BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext? previousContext = null;
            if (!string.IsNullOrWhiteSpace(request.PreviousContextJson))
            {
                try
                {
                    previousContext = System.Text.Json.JsonSerializer.Deserialize<BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext>(
                        request.PreviousContextJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse previousContextJson, proceeding without context.");
                }
            }

            var command = new GenerateLessonCommand
            {
                PdfStream = pdfStream,
                FileName = request.Pdf.FileName,
                StartPage = request.StartPage,
                EndPage = request.EndPage,
                BookId = request.BookId,
                PreviousContext = previousContext
            };

            GenerateLessonResponse response = await _handler.HandleAsync(command, cancellationToken);

            _logger.LogInformation(
                "Successfully generated lesson '{Title}' for pages {StartPage}-{EndPage}.",
                response.Title,
                request.StartPage,
                request.EndPage);

            return Ok(response);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            _logger.LogWarning(ex, "Page range validation error during lesson generation: {Message}", ex.Message);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid page range",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Unprocessable request during lesson generation: {Message}", ex.Message);
            return UnprocessableEntity(new ProblemDetails
            {
                Title = "PDF processing failed",
                Detail = _environment.IsDevelopment() ? $"{ex.Message}\n{ex.StackTrace}" : ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity
            });
        }
        catch (BadHttpRequestException ex)
        {
            _logger.LogWarning(ex, "Bad HTTP request during PDF upload: {Message}", ex.Message);
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid HTTP Request",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Lesson generation was cancelled by the client.");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request Cancelled",
                Detail = "The operation was cancelled.",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unexpected error occurred while generating the lesson: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Internal Server Error",
                Detail = _environment.IsDevelopment()
                    ? $"{ex.Message}\n{ex.StackTrace}"
                    : "An unexpected error occurred while processing the request. Please try again later.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Generates a structured educational lesson from raw text pasted from the clipboard or external sources.
    /// </summary>
    [HttpPost("generate-from-text")]
    [ProducesResponseType(typeof(GenerateLessonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GenerateLessonFromText(
        [FromBody] GenerateLessonFromTextRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.SourceText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "النص مطلوب",
                Detail = "يجب توفير نص صالح لتوليد الدرس التعليمي منه.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext? previousContext = null;
            if (!string.IsNullOrWhiteSpace(request.PreviousContextJson))
            {
                try
                {
                    previousContext = System.Text.Json.JsonSerializer.Deserialize<BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext>(
                        request.PreviousContextJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse previousContextJson in generate-from-text.");
                }
            }

            var command = new GenerateLessonFromTextCommand
            {
                SourceText = request.SourceText.Trim(),
                StartPage = request.StartPage ?? 1,
                EndPage = request.EndPage ?? (request.StartPage ?? 1),
                BookId = request.BookId,
                PreviousContext = previousContext
            };

            GenerateLessonResponse response = await _handler.HandleFromTextAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating lesson from text: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل توليد الدرس من النص",
                Detail = _environment.IsDevelopment() ? $"{ex.Message}\n{ex.StackTrace}" : ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Generates a structured educational lesson from pasted screenshot images.
    /// </summary>
    [HttpPost("generate-from-images")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(100 * 1024 * 1024)]
    [ProducesResponseType(typeof(GenerateLessonResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GenerateLessonFromImages(
        [FromForm] GenerateLessonFromImagesFormRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Images == null || request.Images.Count == 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "الصور مطلوبة",
                Detail = "يجب اختيار أو لصق صورة واحدة على الأقل.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var screenshots = new List<PageScreenshot>();
            int startPage = request.StartPage ?? 1;

            for (int i = 0; i < request.Images.Count; i++)
            {
                var img = request.Images[i];
                if (img.Length > 10 * 1024 * 1024)
                {
                    return BadRequest(new ProblemDetails
                    {
                        Title = "Image file too large",
                        Detail = $"Image {img.FileName} exceeds the 10 MB limit.",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                using (var stream = img.OpenReadStream())
                {
                    if (!FileValidationHelper.IsValidImage(stream))
                    {
                        return BadRequest(new ProblemDetails
                        {
                            Title = "Invalid image file",
                            Detail = $"File {img.FileName} is not a valid JPEG or PNG image.",
                            Status = StatusCodes.Status400BadRequest
                        });
                    }
                }

                using var ms = new MemoryStream();
                await img.CopyToAsync(ms, cancellationToken);
                screenshots.Add(new PageScreenshot
                {
                    PageNumber = startPage + i,
                    ImageBytes = ms.ToArray(),
                    MediaType = string.IsNullOrWhiteSpace(img.ContentType) ? "image/png" : img.ContentType
                });
            }

            BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext? previousContext = null;
            if (!string.IsNullOrWhiteSpace(request.PreviousContextJson))
            {
                try
                {
                    previousContext = System.Text.Json.JsonSerializer.Deserialize<BukhariAI.Application.Lessons.GenerateLesson.LessonLearningContext>(
                        request.PreviousContextJson,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                catch (System.Text.Json.JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse previousContextJson in generate-from-images.");
                }
            }

            var command = new GenerateLessonFromImagesCommand
            {
                Images = screenshots,
                StartPage = startPage,
                EndPage = request.EndPage ?? (startPage + screenshots.Count - 1),
                BookId = request.BookId,
                PreviousContext = previousContext
            };

            GenerateLessonResponse response = await _handler.HandleFromImagesAsync(command, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating lesson from images: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل توليد الدرس من الصور",
                Detail = _environment.IsDevelopment() ? $"{ex.Message}\n{ex.StackTrace}" : ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Retrieves a persisted lesson by its unique identifier.
    /// </summary>
    /// <param name="id">The lesson GUID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(Lesson), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLessonById(Guid id, CancellationToken cancellationToken)
    {
        var lesson = await _persistenceService.GetLessonByIdAsync(id, cancellationToken);
        if (lesson == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Lesson Not Found",
                Detail = $"No lesson found with ID '{id}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(lesson);
    }

    /// <summary>Gets the learner-controlled completion state for a lesson.</summary>
    [HttpGet("{id:guid}/progress")]
    public async Task<IActionResult> GetProgress(Guid id, CancellationToken cancellationToken)
    {
        var progress = await _studentLearning.GetLessonProgressAsync(id, CurrentUserId, cancellationToken);
        return progress is null ? NotFound() : Ok(progress);
    }

    /// <summary>Records that the student started, completed, or needs to revisit a lesson.</summary>
    [HttpPut("{id:guid}/progress")]
    public async Task<IActionResult> UpdateProgress(Guid id, [FromBody] UpdateLessonProgressRequest request, CancellationToken cancellationToken)
    {
        try { return Ok(await _studentLearning.UpdateLessonProgressAsync(id, request, CurrentUserId, cancellationToken)); }
        catch (KeyNotFoundException) { return NotFound(); }
    }

    /// <summary>
    /// Ask a question or inquire with the AI scholarly tutor regarding a specific lesson.
    /// </summary>
    [HttpPost("{id:guid}/chat")]
    [ProducesResponseType(typeof(LessonChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChatWithLessonTutor(
        Guid id,
        [FromBody] LessonChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "السؤال مطلوب",
                Detail = "يجب كتابة سؤال أو استفسار لطرحه على المعلم الذكي.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        request.LessonId = id;
        try
        {
            var response = await _chatService.AskLessonQuestionAsync(request, CurrentUserId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing lesson chat inquiry for lesson {LessonId}: {Message}", id, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل معالجة استفسار المعلم الذكي",
                Detail = _environment.IsDevelopment() ? ex.Message : "حدث خطأ غير متوقع أثناء معالجة الاستفسار.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// General inquiry with the AI scholarly tutor across lessons or book topics.
    /// </summary>
    [HttpPost("chat")]
    [ProducesResponseType(typeof(LessonChatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ChatGeneral(
        [FromBody] LessonChatRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "السؤال مطلوب",
                Detail = "يجب كتابة سؤال أو استفسار لطرحه على المعلم الذكي.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var response = await _chatService.AskLessonQuestionAsync(request, CurrentUserId, cancellationToken);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing general lesson chat inquiry: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل معالجة استفسار المعلم الذكي",
                Detail = _environment.IsDevelopment() ? ex.Message : "حدث خطأ غير متوقع أثناء معالجة الاستفسار.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Gets a list of chat history sessions, optionally filtered by bookId or lessonId.
    /// </summary>
    [HttpGet("chat/sessions")]
    [ProducesResponseType(typeof(List<ChatSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetChatSessions(
        [FromQuery] Guid? bookId,
        [FromQuery] Guid? lessonId,
        CancellationToken cancellationToken)
    {
        var sessions = await _chatService.GetChatSessionsAsync(bookId, lessonId, CurrentUserId, cancellationToken);
        return Ok(sessions);
    }

    /// <summary>
    /// Gets a specific chat session with its full message history.
    /// </summary>
    [HttpGet("chat/sessions/{sessionId:guid}")]
    [ProducesResponseType(typeof(ChatSessionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetChatSessionById(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await _chatService.GetChatSessionByIdAsync(sessionId, CurrentUserId, cancellationToken);
        if (session == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "الجلسة غير موجودة",
                Detail = $"لم يتم العثور على جلسة محادثة بالمعرف '{sessionId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }
        return Ok(session);
    }

    /// <summary>
    /// Creates a new explicit chat session for a lesson or book.
    /// </summary>
    [HttpPost("chat/sessions")]
    [ProducesResponseType(typeof(ChatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateChatSession(
        [FromBody] CreateChatSessionRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _chatService.CreateChatSessionAsync(request ?? new CreateChatSessionRequest(), CurrentUserId, cancellationToken);
        return Ok(session);
    }

    /// <summary>
    /// Deletes a chat session and all its messages.
    /// </summary>
    [HttpDelete("chat/sessions/{sessionId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteChatSession(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        bool deleted = await _chatService.DeleteChatSessionAsync(sessionId, CurrentUserId, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>
    /// Clears chat history for a session, lesson, or book.
    /// </summary>
    [HttpDelete("chat/history")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ClearChatHistory(
        [FromQuery] Guid? sessionId,
        [FromQuery] Guid? lessonId,
        [FromQuery] Guid? bookId,
        CancellationToken cancellationToken)
    {
        await _chatService.ClearChatHistoryAsync(sessionId, lessonId, bookId, CurrentUserId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Retrieves the authentic, detailed biography of a person/scholar from Siyar A'lam al-Nubala.
    /// </summary>
    [HttpGet("people/biography")]
    [ProducesResponseType(typeof(PersonBiographyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetPersonBiography(
        [FromQuery] string name,
        [FromQuery] string? contextDescription,
        [FromQuery] Guid? lessonId,
        [FromQuery] Guid? bookId,
        [FromQuery] bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "اسم الشخصية مطلوب",
                Detail = "يجب تحديد اسم الشخصية أو الراوي لاسترجاع ترجمتها.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (_biographyService == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "الخدمة غير متوفرة",
                Detail = "خدمة التراجم غير مهيأة حالياً.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        try
        {
            var req = new GetPersonBiographyRequest
            {
                Name = name.Trim(),
                ContextDescription = contextDescription,
                LessonId = lessonId,
                BookId = bookId,
                ForceRefresh = forceRefresh
            };
            var result = await _biographyService.GetPersonBiographyAsync(req, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving biography for '{Name}': {Message}", name, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل استرجاع السيرة",
                Detail = _environment.IsDevelopment() ? ex.Message : "حدث خطأ غير متوقع أثناء استرجاع السيرة من سير أعلام النبلاء.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Retrieves the authentic, detailed biography of a person/scholar via POST payload.
    /// </summary>
    [HttpPost("people/biography")]
    [ProducesResponseType(typeof(PersonBiographyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetPersonBiographyPost(
        [FromBody] GetPersonBiographyRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "اسم الشخصية مطلوب",
                Detail = "يجب تحديد اسم الشخصية أو الراوي لاسترجاع ترجمتها.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (_biographyService == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Title = "الخدمة غير متوفرة",
                Detail = "خدمة التراجم غير مهيأة حالياً.",
                Status = StatusCodes.Status503ServiceUnavailable
            });
        }

        try
        {
            var result = await _biographyService.GetPersonBiographyAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving biography for '{Name}': {Message}", request.Name, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل استرجاع السيرة",
                Detail = _environment.IsDevelopment() ? ex.Message : "حدث خطأ غير متوقع أثناء استرجاع السيرة من سير أعلام النبلاء.",
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}
