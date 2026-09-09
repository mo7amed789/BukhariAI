namespace BukhariAI.Application.Abstractions;

/// <summary>Renders selected PDF pages as images for a multimodal AI request.</summary>
public interface IPdfExtractionService
{
    Task<PdfExtractionResult> ExtractAsync(
        Stream pdfStream,
        int startPage,
        int endPage,
        CancellationToken cancellationToken = default);
}

public sealed class PdfExtractionResult
{
    public List<PageScreenshot> Pages { get; init; } = [];
}

public sealed class PageScreenshot
{
    public int PageNumber { get; init; }

    /// <summary>Encoded image bytes representing the rendered PDF page; never locally transcribed.</summary>
    public byte[] ImageBytes { get; init; } = [];

    public string MediaType { get; init; } = "image/jpeg";
}
