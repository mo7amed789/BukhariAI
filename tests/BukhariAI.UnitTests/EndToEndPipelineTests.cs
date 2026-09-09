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

public class EndToEndPipelineTests
{
    [Fact]
    public async Task CompletePipeline_WithRealPdf_ShouldExtractTextAndGenerateStructuredLesson()
    {
        // 1. Build a real 3-page PDF with PdfPig
        byte[] pdfBytes = CreateSampleSahihBukhariPdf();
        using var pdfStream = new MemoryStream(pdfBytes);

        // 2. Setup Infrastructure & Application dependencies
        var extractionOptions = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20,
            MinimumTextCharacters = 30,
            MinimumMeaningfulCharacterRatio = 0.50,
            OcrEnabled = false
        });

        var pageAnalyzer = new PdfPageAnalyzer(extractionOptions);
        var ocrOptions = Options.Create(new OcrOptions());
        var ocrService = new OcrService(ocrOptions, Microsoft.Extensions.Options.Options.Create(new BukhariAI.Infrastructure.AI.AiOptions()), new System.Net.Http.HttpClient(), NullLogger<OcrService>.Instance);
        var pdfExtractionService = new PdfExtractionService(
            pageAnalyzer,
            ocrService,
            extractionOptions,
            NullLogger<PdfExtractionService>.Instance);

        var mockAiService = new MockAiService(NullLogger<MockAiService>.Instance);
        var handler = new GenerateLessonHandler(
            pdfExtractionService,
            mockAiService,
            NullLogger<GenerateLessonHandler>.Instance);

        var command = new GenerateLessonCommand
        {
            PdfStream = pdfStream,
            FileName = "Sahih_Bukhari_Sample.pdf",
            StartPage = 1,
            EndPage = 3
        };

        // 3. Execute Handler
        var response = await handler.HandleAsync(command, CancellationToken.None);

        // 4. Assert Output
        response.Should().NotBeNull();
        response.Title.Should().NotBeNullOrWhiteSpace();
        response.Overview.Should().NotBeNullOrWhiteSpace();
        response.Hadiths[0].SourcePages.Should().Contain(1);
        response.Hadiths[0].Problem.Should().NotBeNullOrWhiteSpace();
        response.Hadiths[0].EasyExplanation.Should().NotBeNullOrWhiteSpace();
        response.Hadiths[0].Evidence.Should().NotBeEmpty();
        response.Connections.Should().NotBeEmpty();
        response.ReviewQuestions.Should().NotBeEmpty();

        // Metadata verification
        response.Metadata.StartPage.Should().Be(1);
        response.Metadata.EndPage.Should().Be(3);
        response.Metadata.TotalPagesProcessed.Should().Be(3);
        response.Metadata.UsedOcr.Should().BeFalse();
    }

    private static byte[] CreateSampleSahihBukhariPdf()
    {
        var builder = new PdfDocumentBuilder();

        // Page 1
        var page1 = builder.AddPage(PageSize.A4);
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        page1.AddText("Sahih al-Bukhari - Kitab Bad al-Wahy - Page 1: Innamal a'malu bin-niyyat wa innama li-kullimri'in ma nawa", 12, new PdfPoint(50, 750), font);

        // Page 2
        var page2 = builder.AddPage(PageSize.A4);
        page2.AddText("Sahih al-Bukhari - Kitab Bad al-Wahy - Page 2: Kayfa kana badu al-wahy ila rasulillahi sallallahu alayhi wa sallam", 12, new PdfPoint(50, 750), font);

        // Page 3
        var page3 = builder.AddPage(PageSize.A4);
        page3.AddText("Sahih al-Bukhari - Kitab Bad al-Wahy - Page 3: Ru'ya as-sadiqah fi al-manam wa tahannuth fi ghari hira", 12, new PdfPoint(50, 750), font);

        return builder.Build();
    }
}

