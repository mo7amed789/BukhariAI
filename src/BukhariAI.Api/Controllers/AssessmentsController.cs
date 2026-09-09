using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Assessments;
using Microsoft.AspNetCore.Mvc;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class AssessmentsController : ControllerBase
{
    private readonly IAssessmentService _assessmentService;
    private readonly ILogger<AssessmentsController> _logger;
    private readonly IStudentLearningService _studentLearning;
    private readonly ICurrentUserService? _currentUserService;

    public AssessmentsController(
        IAssessmentService assessmentService,
        IStudentLearningService studentLearning,
        ILogger<AssessmentsController> logger,
        ICurrentUserService? currentUserService = null)
    {
        _assessmentService = assessmentService;
        _studentLearning = studentLearning;
        _logger = logger;
        _currentUserService = currentUserService;
    }

    private Guid CurrentUserId => _currentUserService?.UserId ?? BukhariAI.Domain.Entities.User.DefaultUserId;

    /// <summary>
    /// Submits a student's answer for an assessment question, evaluates it via AI against conceptual understanding criteria, and updates the student's concept mastery.
    /// </summary>
    /// <param name="request">The submission payload with QuestionId and StudentAnswer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("submit-answer")]
    [ProducesResponseType(typeof(SubmitStudentAnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitAnswer(
        [FromBody] SubmitStudentAnswerRequest request,
        CancellationToken cancellationToken)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.StudentAnswer))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid request",
                Detail = "A non-empty student answer must be provided.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        try
        {
            var response = await _assessmentService.SubmitAnswerAsync(request, CurrentUserId, cancellationToken);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Request Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Question Not Found",
                Detail = ex.Message,
                Status = StatusCodes.Status404NotFound
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing student answer submission: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Assessment Evaluation Error",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }
    }

    /// <summary>
    /// Retrieves an assessment by its unique ID, including questions, answers, and evaluation results.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AssessmentDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAssessmentById(Guid id, CancellationToken cancellationToken)
    {
        var assessment = await _assessmentService.GetAssessmentByIdAsync(id, CurrentUserId, cancellationToken);
        if (assessment == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Assessment Not Found",
                Detail = $"No assessment found with ID '{id}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(assessment);
    }

    /// <summary>
    /// Retrieves all assessments associated with a specific lesson.
    /// </summary>
    [HttpGet("by-lesson/{lessonId:guid}")]
    [ProducesResponseType(typeof(List<AssessmentDetailDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAssessmentsByLesson(Guid lessonId, CancellationToken cancellationToken)
    {
        var assessments = await _assessmentService.GetAssessmentsByLessonIdAsync(lessonId, CurrentUserId, cancellationToken);
        return Ok(assessments);
    }

    /// <summary>
    /// Retrieves the student concept mastery profile for a book.
    /// </summary>
    [HttpGet("mastery/{bookId:guid}")]
    [ProducesResponseType(typeof(List<StudentConceptMasteryDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMasteryProfile(Guid bookId, CancellationToken cancellationToken)
    {
        var masteryList = await _assessmentService.GetMasteryProfileByBookIdAsync(bookId, CurrentUserId, cancellationToken);
        return Ok(masteryList);
    }

    /// <summary>Returns concepts that have exposure or failed-assessment evidence but insufficient demonstrated understanding.</summary>
    [HttpGet("weak-concepts/{bookId:guid}")]
    public async Task<IActionResult> GetWeakConcepts(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _studentLearning.GetWeakConceptsAsync(bookId, CurrentUserId, cancellationToken));

    /// <summary>Returns stored review work generated solely from learning evidence.</summary>
    [HttpGet("review-recommendations/{bookId:guid}")]
    public async Task<IActionResult> GetReviewRecommendations(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _studentLearning.GetReviewRecommendationsAsync(bookId, CurrentUserId, cancellationToken));
}
