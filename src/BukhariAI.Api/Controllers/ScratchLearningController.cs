using BukhariAI.Application.Abstractions;
using BukhariAI.Application.ScratchLearning;
using Microsoft.AspNetCore.Mvc;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/scratch")]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class ScratchLearningController : ControllerBase
{
    private readonly IScratchLearningService _scratchService;
    private readonly ILogger<ScratchLearningController> _logger;

    public ScratchLearningController(
        IScratchLearningService scratchService,
        ILogger<ScratchLearningController> logger)
    {
        _scratchService = scratchService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all pre-defined curated foundational learning tracks.
    /// </summary>
    [HttpGet("tracks")]
    [ProducesResponseType(typeof(IReadOnlyList<CuratedScratchTrack>), StatusCodes.Status200OK)]
    public IActionResult GetTracks()
    {
        var tracks = _scratchService.GetCuratedTracks();
        return Ok(tracks);
    }

    /// <summary>
    /// Generates or retrieves a full multi-level roadmap for learning a topic from scratch.
    /// </summary>
    [HttpPost("roadmap")]
    [ProducesResponseType(typeof(ScratchRoadmapResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GenerateRoadmap(
        [FromBody] ScratchRoadmapRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Topic) && string.IsNullOrWhiteSpace(request.TrackId))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "الموضوع مطلوب",
                Detail = "يرجى كتابة موضوع للتعلم أو اختيار مسار تأسيسي جاهز.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            _logger.LogInformation("Generating Scratch Roadmap for topic: '{Topic}', trackId: '{TrackId}'", request.Topic, request.TrackId);
            var result = await _scratchService.GenerateRoadmapAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Scratch Roadmap request was cancelled by client.");
            return StatusCode(499, new ProblemDetails
            {
                Title = "Request Cancelled",
                Detail = "The operation was cancelled.",
                Status = 499
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Scratch Roadmap: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل في توليد خارطة الطريق التأسيسية",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Generates or retrieves deep step-by-step explanation with analogies and quiz for a milestone.
    /// </summary>
    [HttpPost("explain-step")]
    [ProducesResponseType(typeof(ScratchStepExplanation), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExplainStep(
        [FromBody] ExplainScratchStepRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.MilestoneTitle))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "عنوان المحطة مطلوب",
                Detail = "يرجى تحديد عنوان المحطة المراد شرحها بالتفصيل.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var result = await _scratchService.ExplainStepAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ProblemDetails { Title = "Request Cancelled", Status = 499 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to explain scratch milestone: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل في إعداد الشرح التفصيلي للمحطة",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Ask the scratch tutor a clarifying question in super-simplified mode.
    /// </summary>
    [HttpPost("ask-tutor")]
    [ProducesResponseType(typeof(AskScratchTutorResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> AskTutor(
        [FromBody] AskScratchTutorRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserQuestion))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "السؤال مطلوب",
                Detail = "يرجى كتابة السؤال أو الاستفسار المراد توضيحه.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var result = await _scratchService.AskTutorAsync(request, cancellationToken);
            return Ok(result);
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new ProblemDetails { Title = "Request Cancelled", Status = 499 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ask scratch tutor: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "فشل في الرد من المعلم التأسيسي",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }
}
