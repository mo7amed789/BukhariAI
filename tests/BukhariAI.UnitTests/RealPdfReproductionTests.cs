using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Ocr;
using BukhariAI.Infrastructure.Pdf;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class RealPdfReproductionTests
{
    private readonly ITestOutputHelper _output;

    public RealPdfReproductionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task InspectPages500To503_RealBukhariPdf()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath))
        {
            return;
        }

        var options = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20,
            MinimumTextCharacters = 100,
            MinimumMeaningfulCharacterRatio = 0.60,
            OcrEnabled = true
        });

        var analyzer = new PdfPageAnalyzer(options);
        var ocrOptions = Options.Create(new OcrOptions { Engine = "Tesseract", Language = "ara" });
        var ocrService = new OcrService(ocrOptions, Microsoft.Extensions.Options.Options.Create(new BukhariAI.Infrastructure.AI.AiOptions()), new System.Net.Http.HttpClient(), NullLogger<OcrService>.Instance);
        var extractionService = new PdfExtractionService(analyzer, ocrService, options, NullLogger<PdfExtractionService>.Instance);

        await using var stream = File.OpenRead(pdfPath);
        var result = await extractionService.ExtractAsync(stream, 500, 501, CancellationToken.None);

        _output.WriteLine($"=== TOTAL EXTRACTED PAGES: {result.Pages.Count} ===");

        foreach (var page in result.Pages)
        {
            _output.WriteLine($"\n==================== [PDF PAGE {page.PageNumber}] (Length: {page.Text.Length} chars) ====================");
            string preview = page.Text.Length > 400 ? page.Text[..400] : page.Text;
            _output.WriteLine(preview);
            _output.WriteLine("=========================================================================");
        }

        result.Pages.Should().HaveCount(2);
    }
}

