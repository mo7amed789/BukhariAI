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

public class SourceGroundingTests
{
    private readonly GenerateLessonHandler _handler;
    private readonly IPdfExtractionService _pdfExtractionService;

    public SourceGroundingTests()
    {
        var extractionOptions = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20,
            MinimumTextCharacters = 20,
            MinimumMeaningfulCharacterRatio = 0.50,
            OcrEnabled = false
        });

        var pageAnalyzer = new PdfPageAnalyzer(extractionOptions);
        var ocrOptions = Options.Create(new OcrOptions());
        var ocrService = new OcrService(ocrOptions, Microsoft.Extensions.Options.Options.Create(new BukhariAI.Infrastructure.AI.AiOptions()), new System.Net.Http.HttpClient(), NullLogger<OcrService>.Instance);
        _pdfExtractionService = new PdfExtractionService(
            pageAnalyzer,
            ocrService,
            extractionOptions,
            NullLogger<PdfExtractionService>.Instance);

        var mockAiService = new MockAiService(NullLogger<MockAiService>.Instance);
        _handler = new GenerateLessonHandler(
            _pdfExtractionService,
            mockAiService,
            NullLogger<GenerateLessonHandler>.Instance);
    }

    [Fact]
    public async Task DifferentPages_ProduceDifferentAndGroundedLessons()
    {
        // 1. Create a 4-page PDF with distinct text on pages 500-501 vs 502-503
        byte[] pdfBytes = CreateMultiPageBukhariPdf();

        // 2. Request pages 500-501 (Kitab al-Adhan / Prayer Call)
        using var stream1 = new MemoryStream(pdfBytes);
        var command1 = new GenerateLessonCommand
        {
            PdfStream = stream1,
            FileName = "Bukhari_Vol2.pdf",
            StartPage = 1, // Represents range 1-2
            EndPage = 2
        };
        var lesson1 = await _handler.HandleAsync(command1, CancellationToken.None);

        // 3. Request pages 502-503 (Kitab al-Jana'iz / Funerals)
        using var stream2 = new MemoryStream(pdfBytes);
        var command2 = new GenerateLessonCommand
        {
            PdfStream = stream2,
            FileName = "Bukhari_Vol2.pdf",
            StartPage = 3, // Represents range 3-4
            EndPage = 4
        };
        var lesson2 = await _handler.HandleAsync(command2, CancellationToken.None);

        // 4. Assert that lessons are completely distinct and grounded in their respective pages
        lesson1.Title.Should().NotBe(lesson2.Title);
        lesson1.Overview.Should().NotBe(lesson2.Overview);
        lesson1.SourcePages.Should().BeEquivalentTo([1, 2]);
        lesson2.SourcePages.Should().BeEquivalentTo([3, 4]);

        lesson1.Hadiths[0].Summary.Should().Contain("Adhan");
        lesson2.Hadiths[0].Summary.Should().Contain("Jana'iz");

        lesson1.Hadiths[0].SourcePages.Should().Contain(1);
        lesson2.Hadiths[0].SourcePages.Should().Contain(3);
    }

    [Fact]
    public async Task EmptyOrScannedPagesWithoutOcr_ReturnsClearDiagnosticMockNotice()
    {
        // 1. Create an empty/scanned PDF without text
        byte[] pdfBytes = CreateBlankPagesPdf();
        using var stream = new MemoryStream(pdfBytes);

        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "Scanned_Bukhari.pdf",
            StartPage = 1,
            EndPage = 2
        };

        // 2. Assert that handler throws InvalidOperationException when OCR cannot extract text from scanned pages
        var act = () => _handler.HandleAsync(command, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Unable to extract usable text*");
    }

    private static byte[] CreateMultiPageBukhariPdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        // Page 1: Kitab al-Adhan
        var p1 = builder.AddPage(PageSize.A4);
        p1.AddText("Kitab al-Adhan: Salatu al-jama'ah tafdulu salat al-fadh bi sab'in wa 'ishrina darajah.", 12, new PdfPoint(50, 750), font);

        // Page 2: Kitab al-Adhan continued
        var p2 = builder.AddPage(PageSize.A4);
        p2.AddText("Kitab al-Adhan Bab Fadl Salat al-Fajr fi Jama'ah: Tafdulu salat al-jami' 'ala salat ahadikum wahdahu.", 12, new PdfPoint(50, 750), font);

        // Page 3: Kitab al-Jana'iz
        var p3 = builder.AddPage(PageSize.A4);
        p3.AddText("Kitab al-Jana'iz: Man tabi'a janazatan hatta yusalla 'alayha falahu qiratun min al-ajr.", 12, new PdfPoint(50, 750), font);

        // Page 4: Kitab al-Jana'iz continued
        var p4 = builder.AddPage(PageSize.A4);
        p4.AddText("Kitab al-Jana'iz Bab Ghasl al-Mayyit: Dakhala 'alayna rasulullahi sallallahu alayhi wa sallam.", 12, new PdfPoint(50, 750), font);

        return builder.Build();
    }

    private static byte[] CreateBlankPagesPdf()
    {
        var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4);
        builder.AddPage(PageSize.A4);
        return builder.Build();
    }
}

