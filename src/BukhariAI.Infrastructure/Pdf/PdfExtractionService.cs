using BukhariAI.Application.Abstractions;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace BukhariAI.Infrastructure.Pdf;

/// <summary>Renders pages as images; Vision is the only component that reads them.</summary>
public sealed class PdfExtractionService : IPdfExtractionService
{
    private readonly PdfExtractionOptions _options;
    private readonly ILogger<PdfExtractionService> _logger;

    public PdfExtractionService(IOptions<PdfExtractionOptions> options, ILogger<PdfExtractionService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PdfExtractionResult> ExtractAsync(Stream pdfStream, int startPage, int endPage, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pdfStream);
        ValidatePageRange(startPage, endPage);

        await using var copy = new MemoryStream();
        if (pdfStream.CanSeek) pdfStream.Position = 0;
        await pdfStream.CopyToAsync(copy, cancellationToken);

        try
        {
            using var document = DocLib.Instance.GetDocReader(copy.ToArray(), new PageDimensions(_options.RenderWidth, _options.RenderHeight));
            int totalPages = document.GetPageCount();
            if (startPage > totalPages || endPage > totalPages)
                throw new ArgumentOutOfRangeException(nameof(endPage), $"Requested pages {startPage}-{endPage} are outside this {totalPages}-page document.");

            var pages = new List<PageScreenshot>(endPage - startPage + 1);
            for (int pageNumber = startPage; pageNumber <= endPage; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var page = document.GetPageReader(pageNumber - 1);
                pages.Add(new PageScreenshot
                {
                    PageNumber = pageNumber,
                    ImageBytes = EncodeJpeg(page.GetImage(), page.GetPageWidth(), page.GetPageHeight(), _options.VisionImageQuality),
                    MediaType = "image/jpeg"
                });
            }

            _logger.LogInformation("Rendered {Count} page screenshots for Vision: {StartPage}-{EndPage}.", pages.Count, startPage, endPage);
            return new PdfExtractionResult { Pages = pages };
        }
        catch (ArgumentOutOfRangeException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to render requested PDF pages as screenshots.");
            throw new InvalidOperationException("The selected PDF pages could not be rendered as images.", ex);
        }
    }

    private void ValidatePageRange(int startPage, int endPage)
    {
        if (startPage < 1) throw new ArgumentOutOfRangeException(nameof(startPage), "Start page must be at least 1.");
        if (endPage < startPage) throw new ArgumentOutOfRangeException(nameof(endPage), "End page must not precede start page.");
        if (endPage - startPage + 1 > _options.MaximumPageRange)
            throw new ArgumentOutOfRangeException(nameof(endPage), $"The maximum Vision page range is {_options.MaximumPageRange} pages.");
    }

    private static byte[] EncodeJpeg(byte[] bgra, int width, int height, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        using Image<Bgra32> image = Image.LoadPixelData<Bgra32>(bgra, width, height);
        using var output = new MemoryStream();
        image.Save(output, new JpegEncoder { Quality = quality });
        return output.ToArray();
    }
}
