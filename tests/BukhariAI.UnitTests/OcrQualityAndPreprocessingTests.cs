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

public class OcrQualityAndPreprocessingTests
{
    private readonly ITestOutputHelper _output;
    private readonly GenerateLessonHandler _handler;
    private readonly IPdfExtractionService _extractionService;

    public OcrQualityAndPreprocessingTests(ITestOutputHelper output)
    {
        _output = output;

        var pdfOptions = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20,
            MinimumTextCharacters = 50,
            MinimumMeaningfulCharacterRatio = 0.50,
            OcrEnabled = true
        });

        var ocrOptions = Options.Create(new OcrOptions
        {
            Engine = "Tesseract",
            Language = "ara",
            Dpi = 300,
            PageSegmentationMode = "Auto",
            FallbackPsm = "SingleBlock",
            MinimumQualityScore = 0.45
        });

        var pageAnalyzer = new PdfPageAnalyzer(pdfOptions);
        var ocrService = new OcrService(ocrOptions, Microsoft.Extensions.Options.Options.Create(new BukhariAI.Infrastructure.AI.AiOptions()), new System.Net.Http.HttpClient(), NullLogger<OcrService>.Instance);
        _extractionService = new PdfExtractionService(
            pageAnalyzer,
            ocrService,
            pdfOptions,
            NullLogger<PdfExtractionService>.Instance);

        var mockAiService = new MockAiService(NullLogger<MockAiService>.Instance);
        _handler = new GenerateLessonHandler(
            _extractionService,
            mockAiService,
            NullLogger<GenerateLessonHandler>.Instance);
    }

    [Fact]
    public void ArabicTextNormalizer_CleansWhitespaceAndTatweel()
    {
        string raw = "  حرمت   عليكم   الميتـــة    (  المائدة : 3 )  ..   ";
        string normalized = ArabicTextNormalizer.Normalize(raw);

        normalized.Should().Be("حرمت عليكم الميتة (المائدة: 3)..");
    }

    [Fact]
    public void OcrQualityEvaluator_RecognizesHighQualityArabic()
    {
        string arabic = "في هذا الحديث دليل على أن قوله تعالى حرمت عليكم الميتة ليس عاما في جميع وجوه الانتفاع";
        var quality = OcrQualityEvaluator.Evaluate(arabic, confidence: 0.85);

        quality.IsAcceptable.Should().BeTrue();
        quality.ArabicRatio.Should().BeGreaterThan(0.90);
        quality.Score.Should().BeGreaterThan(0.70);
    }

    [Fact]
    public void OcrQualityEvaluator_RejectsGarbage()
    {
        string garbage = "!@#$%^&*() 12345 67890 \uFFFD \uFFFD \uFFFD";
        var quality = OcrQualityEvaluator.Evaluate(garbage, confidence: 0.20);

        quality.IsAcceptable.Should().BeFalse();
        quality.ArabicRatio.Should().BeLessThan(0.10);
    }

    [Fact]
    public async Task RealPages110And111_ExtractHighQualityArabicWithProvenance()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        await using var stream = File.OpenRead(pdfPath);
        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "s.bokhari.5.pdf",
            StartPage = 110,
            EndPage = 111
        };

        var response = await _handler.HandleAsync(command, CancellationToken.None);

        _output.WriteLine($"[Result] Title: {response.Title}");
        _output.WriteLine($"[Result] Overview: {response.Overview}");
        _output.WriteLine($"[Result] Used OCR: {response.Metadata.UsedOcr}");

        response.Metadata.UsedOcr.Should().BeTrue();
        response.SourcePages.Should().BeEquivalentTo([110, 111]);
        response.Hadiths.Should().HaveCount(2);

        // Verify page 110 content
        response.Hadiths[0].Summary.Should().Contain("110");
        response.Hadiths[0].Summary.Should().MatchRegex("الميت|حرم|الانتفاع|البخاري");

        // Verify page 111 content
        response.Hadiths[1].Summary.Should().Contain("111");
        response.Hadiths[1].Summary.Should().MatchRegex("الخفاف|جلود|أكله|مدبوغ|دباغ");
    }
}

