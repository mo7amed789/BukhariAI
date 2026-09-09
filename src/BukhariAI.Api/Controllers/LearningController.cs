using BukhariAI.Application.Abstractions;
using BukhariAI.Application.AdaptiveLearning;
using BukhariAI.Application.LearningSessions;
using BukhariAI.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/learning/{bookId:guid}")]
[Produces("application/json")]
public sealed class LearningController : ControllerBase
{
    private readonly IAdaptiveLearningService _adaptive;
    private readonly ILearningSessionService _sessions;

    public LearningController(IAdaptiveLearningService adaptive, ILearningSessionService sessions)
    {
        _adaptive = adaptive;
        _sessions = sessions;
    }

    [HttpGet("next")]
    public async Task<IActionResult> GetNext(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _sessions.GetNextAsync(bookId, cancellationToken));

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _sessions.GetDashboardAsync(bookId, cancellationToken));

    [HttpGet("next-action")]
    public async Task<IActionResult> GetNextAction(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _adaptive.GetAdaptiveStateAsync(bookId, LearningIntent.ContinueLearning, cancellationToken));

    [HttpGet("adaptive-state")]
    public async Task<IActionResult> GetAdaptiveState(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _adaptive.GetAdaptiveStateAsync(bookId, LearningIntent.ContinueLearning, cancellationToken));

    [HttpGet("weak-concepts")]
    public async Task<IActionResult> GetWeakConcepts(Guid bookId, CancellationToken cancellationToken)
    {
        var state = await _adaptive.GetAdaptiveStateAsync(bookId, LearningIntent.ReinforceWeakConcepts, cancellationToken);
        return Ok(state.Concepts.Where(c => c.IsWeak));
    }

    [HttpGet("due-reviews")]
    public async Task<IActionResult> GetDueReviews(Guid bookId, CancellationToken cancellationToken) =>
        Ok(await _adaptive.GetDueReviewsAsync(bookId, cancellationToken));

    [HttpPost("continue")]
    public async Task<IActionResult> Continue(Guid bookId, [FromBody] ContinueLearningRequest request, CancellationToken cancellationToken) =>
        Ok(await _sessions.GetNextAsync(bookId, cancellationToken));

    /// <summary>
    /// Generates or fetches an AI-guided reinforcement breakdown for a specific weak or due concept.
    /// </summary>
    [HttpPost("reinforce-concept")]
    public async Task<IActionResult> ReinforceConcept(
        Guid bookId,
        [FromBody] GenerateReinforcementRequest request,
        CancellationToken cancellationToken)
    {
        var session = await _adaptive.GenerateConceptReinforcementAsync(bookId, request, cancellationToken);
        return Ok(session);
    }

    /// <summary>
    /// Evaluates a student's answer to the reinforcement question and atomically updates mastery.
    /// </summary>
    [HttpPost("evaluate-reinforcement")]
    public async Task<IActionResult> EvaluateReinforcement(
        Guid bookId,
        [FromBody] SubmitReinforcementAnswerRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adaptive.EvaluateReinforcementAnswerAsync(bookId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns the active recall & spaced repetition flashcard deck for the book.
    /// </summary>
    [HttpGet("review-deck")]
    public async Task<IActionResult> GetReviewDeck(Guid bookId, CancellationToken cancellationToken)
    {
        var deck = await _adaptive.GetReviewDeckAsync(bookId, cancellationToken);
        return Ok(deck);
    }

    /// <summary>
    /// Records student self-assessment rating on a flashcard during active recall drills.
    /// </summary>
    [HttpPost("rate-flashcard")]
    public async Task<IActionResult> RateFlashcard(
        Guid bookId,
        [FromBody] FlashcardRatingRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _adaptive.RateFlashcardAsync(bookId, request, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Returns the comprehensive Mastery Matrix for all concepts in the book.
    /// </summary>
    [HttpGet("mastery-matrix")]
    public async Task<IActionResult> GetMasteryMatrix(Guid bookId, CancellationToken cancellationToken)
    {
        var matrix = await _adaptive.GetMasteryMatrixAsync(bookId, cancellationToken);
        return Ok(matrix);
    }

    /// <summary>
    /// Generates a rapid multi-concept review quiz session across due and weak concepts.
    /// </summary>
    [HttpGet("quick-review-quiz")]
    public async Task<IActionResult> GetQuickReviewQuiz(
        Guid bookId,
        [FromQuery] int count = 3,
        CancellationToken cancellationToken = default)
    {
        var quiz = await _adaptive.GenerateReviewQuizAsync(bookId, count, cancellationToken);
        return Ok(quiz);
    }
}
