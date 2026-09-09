using BukhariAI.Application.Abstractions;
using BukhariAI.Api.Models;
using BukhariAI.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace BukhariAI.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class BooksController : ControllerBase
{
    private readonly ILessonPersistenceService _persistenceService;
    private readonly ICurrentUserService _currentUserService;
    private readonly ILogger<BooksController> _logger;

    public BooksController(
        ILessonPersistenceService persistenceService,
        ICurrentUserService currentUserService,
        ILogger<BooksController> logger)
    {
        _persistenceService = persistenceService;
        _currentUserService = currentUserService;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves all registered books.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<Book>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBooks(CancellationToken cancellationToken)
    {
        var books = await _persistenceService.GetBooksAsync(_currentUserService.UserId, cancellationToken);
        return Ok(books);
    }

    /// <summary>Creates a new book collection for its own pages and lessons.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(Book), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateBook([FromBody] CreateBookRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest(new ProblemDetails { Title = "اسم الكتاب مطلوب." });

        var book = await _persistenceService.CreateBookAsync(request.Title, _currentUserService.UserId, cancellationToken);
        return CreatedAtAction(nameof(GetBooks), new { id = book.Id }, book);
    }

    /// <summary>
    /// Retrieves all lessons associated with a specific book.
    /// </summary>
    /// <param name="bookId">The unique book ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{bookId:guid}/lessons")]
    [ProducesResponseType(typeof(List<Lesson>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLessonsByBookId(Guid bookId, CancellationToken cancellationToken)
    {
        var lessons = await _persistenceService.GetLessonsByBookIdAsync(bookId, _currentUserService.UserId, cancellationToken);
        return Ok(lessons);
    }

    /// <summary>
    /// Retrieves the progressive learning memory context (known people, terms, topics) for a book.
    /// </summary>
    /// <param name="bookId">The unique book ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{bookId:guid}/learning-context")]
    [ProducesResponseType(typeof(LessonLearningContext), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLearningContextByBookId(Guid bookId, CancellationToken cancellationToken)
    {
        var context = await _persistenceService.GetLearningContextByBookIdAsync(bookId, cancellationToken);
        if (context == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Learning Context Not Found",
                Detail = $"No learning context found for book ID '{bookId}'.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(context);
    }

    /// <summary>
    /// Retrieves the student concept mastery profile for a book.
    /// </summary>
    /// <param name="bookId">The unique book ID.</param>
    /// <param name="masteryEngine">The mastery engine service.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{bookId:guid}/mastery")]
    [ProducesResponseType(typeof(List<StudentConceptMastery>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMasteryByBookId(
        Guid bookId,
        [FromServices] IMasteryEngineService masteryEngine,
        CancellationToken cancellationToken)
    {
        var masteries = await masteryEngine.GetStudentMasteryByBookIdAsync(bookId, cancellationToken);
        return Ok(masteries);
    }
}
