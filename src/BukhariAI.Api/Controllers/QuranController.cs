using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Quran;
using BukhariAI.Infrastructure.Quran;
using Microsoft.AspNetCore.Mvc;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class QuranController : ControllerBase
{
    private readonly IQuranAnalysisService _quranService;
    private readonly ILessonPersistenceService _persistenceService;
    private readonly ILogger<QuranController> _logger;

    public QuranController(
        IQuranAnalysisService quranService,
        ILessonPersistenceService persistenceService,
        ILogger<QuranController> logger)
    {
        _quranService = quranService;
        _persistenceService = persistenceService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves the list of all 114 Surahs with their metadata.
    /// </summary>
    [HttpGet("surahs")]
    [ProducesResponseType(typeof(IReadOnlyList<SurahMeta>), StatusCodes.Status200OK)]
    public IActionResult GetSurahs()
    {
        return Ok(QuranDataCatalog.AllSurahs);
    }

    /// <summary>
    /// Generates a comprehensive thematic and memorization study for a Quranic Surah or Ayah range,
    /// and saves or updates it as an educational Lesson in the system.
    /// </summary>
    [HttpPost("analyze")]
    [ProducesResponseType(typeof(QuranSurahAnalysisResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> Analyze(
        [FromBody] AnalyzeQuranRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.SurahNumber.HasValue &&
            string.IsNullOrWhiteSpace(request.SurahName) &&
            string.IsNullOrWhiteSpace(request.CustomText))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "البيانات غير مكتملة",
                Detail = "يرجى تحديد رقم أو اسم السورة، أو إدخال نص الآيات المطلوب تحليلها.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            _logger.LogInformation(
                "Starting Quran memorization analysis for Surah: {SurahNumber} / {SurahName}, Range: {Start}-{End}.",
                request.SurahNumber,
                request.SurahName,
                request.StartAyah,
                request.EndAyah);

            var result = await _quranService.AnalyzeSurahAsync(request, cancellationToken);

            // Automatically persist or update as a Lesson in the database
            try
            {
                var lessonId = await _persistenceService.PersistOrUpdateQuranSurahLessonAsync(result, cancellationToken);
                result.LessonId = lessonId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to persist Quran analysis as a lesson: {Message}", ex.Message);
            }

            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Quran analysis request was cancelled by client.");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request Cancelled",
                Detail = "The operation was cancelled.",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing Quran analysis: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل في إعداد دراسة السورة",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}
