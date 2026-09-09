using System.ComponentModel.DataAnnotations;

namespace BukhariAI.Api.Models;

public sealed class CreateBookRequest
{
    [Required]
    [StringLength(200)]
    public string Title { get; init; } = string.Empty;
}
