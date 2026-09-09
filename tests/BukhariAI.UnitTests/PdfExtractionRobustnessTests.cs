using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Ocr;
using BukhariAI.Infrastructure.Pdf;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace BukhariAI.UnitTests;

public class PdfExtractionRobustnessTests
{
    private readonly PdfExtractionService _pdfExtractionService;

    public PdfExtractionRobustnessTests()
    {
        var extractionOptions = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20,
            MinimumTextCharacters = 30,
            MinimumMeaningfulCharacterRatio = 0.50,
            OcrEnabled = true
        });

        var pageAnalyzer = new PdfPageAnalyzer(extractionOptions);
        var ocrOptions = Options.Create(new OcrOptions { Engine = "Tesseract" });
        var ocrService = new OcrService(ocrOptions, Microsoft.Extensions.Options.Options.Create(new BukhariAI.Infrastructure.AI.AiOptions()), new System.Net.Http.HttpClient(), NullLogger<OcrService>.Instance);

        _pdfExtractionService = new PdfExtractionService(
            pageAnalyzer,
            ocrService,
            extractionOptions,
            NullLogger<PdfExtractionService>.Instance);
    }

    [Fact]
    public async Task ExtractAsync_WhenStartPageExceedsDocumentPages_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange (1-page PDF, request page 5)
        byte[] pdfBytes = CreateOnePagePdf();
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var act = () => _pdfExtractionService.ExtractAsync(stream, 5, 5, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Start page (5) exceeds total document pages (1)*");
    }

    [Fact]
    public async Task ExtractAsync_WhenEndPageExceedsDocumentPages_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange (1-page PDF, request pages 1-5)
        byte[] pdfBytes = CreateOnePagePdf();
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var act = () => _pdfExtractionService.ExtractAsync(stream, 1, 5, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*End page (5) exceeds total document pages (1)*");
    }

    [Fact]
    public async Task ExtractAsync_WhenCorruptedPdfStreamProvided_ShouldThrowInvalidOperationException()
    {
        // Arrange (corrupted random bytes)
        byte[] corruptedBytes = "NOT A VALID PDF FILE CONTENT HEADER"u8.ToArray();
        using var stream = new MemoryStream(corruptedBytes);

        // Act
        var act = () => _pdfExtractionService.ExtractAsync(stream, 1, 1, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*invalid or could not be parsed*");
    }

    [Fact]
    public async Task ExtractAsync_WhenPdfHasValidDirectText_ShouldExtractDirectTextCleanly()
    {
        // Arrange
        byte[] pdfBytes = CreateOnePagePdf();
        using var stream = new MemoryStream(pdfBytes);

        // Act
        var result = await _pdfExtractionService.ExtractAsync(stream, 1, 1, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Pages.Should().HaveCount(1);
        result.Pages[0].PageNumber.Should().Be(1);
        result.Pages[0].Method.Should().Be(ExtractionMethod.DirectText);
        result.Pages[0].Text.Should().Contain("Sahih al-Bukhari");
    }

    private static byte[] CreateOnePagePdf()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page.AddText("Sahih al-Bukhari - Innamal a'malu bin-niyyat - Chapter on Sincerity", 12, new PdfPoint(50, 750), font);
        return builder.Build();
    }
}

