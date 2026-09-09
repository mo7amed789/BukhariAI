using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace BukhariAI.Api.Models;

public sealed class GenerateLessonFormRequest
{
    /// <summary>
    /// The Sahih al-Bukhari PDF file.
    /// </summary>
    [Required]
    public required IFormFile Pdf { get; init; }

    /// <summary>
    /// The starting 1-indexed page number (e.g. 1).
    /// </summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "Start page must be at least 1.")]
    public int StartPage { get; init; }

    /// <summary>
    /// The ending 1-indexed page number (e.g. 20).
    /// </summary>
    [Required]
    [Range(1, int.MaxValue, ErrorMessage = "End page must be at least 1.")]
    public int EndPage { get; init; }

    /// <summary>
    /// Optional target book identifier.
    /// </summary>
    public Guid? BookId { get; init; }

    /// <summary>
    /// Optional serialized JSON of LessonLearningContext from prior lessons.
    /// </summary>
    public string? PreviousContextJson { get; init; }
}

public sealed class GenerateLessonFromTextRequest
{
    [Required]
    public string SourceText { get; init; } = string.Empty;

    public int? StartPage { get; init; }

    public int? EndPage { get; init; }

    public Guid? BookId { get; init; }

    public string? PreviousContextJson { get; init; }
}

public sealed class GenerateLessonFromImagesFormRequest
{
    [Required]
    public List<IFormFile> Images { get; init; } = [];

    public int? StartPage { get; init; }

    public int? EndPage { get; init; }

    public Guid? BookId { get; init; }

    public string? PreviousContextJson { get; init; }
}
