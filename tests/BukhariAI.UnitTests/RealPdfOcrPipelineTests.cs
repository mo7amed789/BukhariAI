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
using Xunit.Abstractions;

namespace BukhariAI.UnitTests;

public class RealPdfOcrPipelineTests
{
    private readonly ITestOutputHelper _output;
    private readonly GenerateLessonHandler _handler;
    private readonly IPdfExtractionService _extractionService;

    public RealPdfOcrPipelineTests(ITestOutputHelper output)
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
            Language = "ara"
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
    public async Task TestA_Pages111To112_RunsOcrAndGeneratesGroundedLesson()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        await using var stream = File.OpenRead(pdfPath);
        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "s.bokhari.5.pdf",
            StartPage = 111,
            EndPage = 112
        };

        var response = await _handler.HandleAsync(command, CancellationToken.None);

        _output.WriteLine($"[Test A] Title: {response.Title}");
        _output.WriteLine($"[Test A] Overview: {response.Overview}");
        _output.WriteLine($"[Test A] Used OCR: {response.Metadata.UsedOcr}");
        _output.WriteLine($"[Test A] Total Pages: {response.Metadata.TotalPagesProcessed}");

        foreach (var hadith in response.Hadiths)
        {
            _output.WriteLine($"\n[Hadith] Ref: {hadith.Reference}, Pages: [{string.Join(",", hadith.SourcePages)}]");
            _output.WriteLine($"Summary: {hadith.Summary}");
        }

        // Assertions
        response.Metadata.UsedOcr.Should().BeTrue();
        response.Metadata.TotalPagesProcessed.Should().Be(2);
        response.SourcePages.Should().BeEquivalentTo([111, 112]);
        response.Hadiths.Should().NotBeEmpty();
        response.Hadiths[0].Summary.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task TestB_NormalTextPdf_UsesDirectTextWithoutOcr()
    {
        byte[] pdfBytes = CreateDirectTextPdf();
        using var stream = new MemoryStream(pdfBytes);

        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "TextPdf.pdf",
            StartPage = 1,
            EndPage = 2
        };

        var response = await _handler.HandleAsync(command, CancellationToken.None);

        _output.WriteLine($"[Test B] Used OCR: {response.Metadata.UsedOcr}");
        response.Metadata.UsedOcr.Should().BeFalse();
        response.SourcePages.Should().BeEquivalentTo([1, 2]);
    }

    [Fact]
    public async Task TestC_Pages500To501_RunsOcrAndGeneratesDistinctLesson()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        await using var stream = File.OpenRead(pdfPath);
        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "s.bokhari.5.pdf",
            StartPage = 500,
            EndPage = 501
        };

        var response = await _handler.HandleAsync(command, CancellationToken.None);

        _output.WriteLine($"[Test C] Title: {response.Title}");
        _output.WriteLine($"[Test C] Used OCR: {response.Metadata.UsedOcr}");

        response.Metadata.UsedOcr.Should().BeTrue();
        response.SourcePages.Should().BeEquivalentTo([500, 501]);
        response.Hadiths.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TestD_InvalidPageRange_ThrowsArgumentOutOfRangeException()
    {
        string pdfPath = @"c:\Users\mohamed\Desktop\s.bokhari.5.pdf";
        if (!File.Exists(pdfPath)) return;

        await using var stream = File.OpenRead(pdfPath);
        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "s.bokhari.5.pdf",
            StartPage = 700, // s.bokhari.5.pdf has 688 pages
            EndPage = 705
        };

        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Start page (700) exceeds total document pages (688)*");
    }

    private static byte[] CreateDirectTextPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        var p1 = builder.AddPage(PageSize.A4);
        p1.AddText("Sahih al-Bukhari Chapter 1: Meaningful searchable direct text on page 1 with more than 50 characters of verifiable data.", 12, new PdfPoint(50, 750), font);

        var p2 = builder.AddPage(PageSize.A4);
        p2.AddText("Sahih al-Bukhari Chapter 2: Meaningful searchable direct text on page 2 with more than 50 characters of verifiable data.", 12, new PdfPoint(50, 750), font);

        return builder.Build();
    }
}

