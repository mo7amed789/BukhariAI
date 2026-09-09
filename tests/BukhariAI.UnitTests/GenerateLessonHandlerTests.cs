using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace BukhariAI.UnitTests;

public class GenerateLessonHandlerTests
{
    private readonly Mock<IPdfExtractionService> _mockPdfService;
    private readonly Mock<IAiService> _mockAiService;
    private readonly GenerateLessonHandler _handler;

    public GenerateLessonHandlerTests()
    {
        _mockPdfService = new Mock<IPdfExtractionService>();
        _mockAiService = new Mock<IAiService>();

        _handler = new GenerateLessonHandler(
            _mockPdfService.Object,
            _mockAiService.Object,
            NullLogger<GenerateLessonHandler>.Instance);
    }

    [Fact]
    public async Task HandleAsync_WhenValidInput_ShouldExtractAndGenerateLessonWithCorrectMetadata()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[50]);
        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "bukhari_sample.pdf",
            StartPage = 5,
            EndPage = 8
        };

        var extractionResult = new PdfExtractionResult
        {
            Pages =
            [
                new ExtractedPage { PageNumber = 5, Text = "صفحة 5", Method = ExtractionMethod.DirectText },
                new ExtractedPage { PageNumber = 6, Text = "صفحة 6", Method = ExtractionMethod.DirectText },
                new ExtractedPage { PageNumber = 7, Text = "صفحة 7", Method = ExtractionMethod.Ocr },
                new ExtractedPage { PageNumber = 8, Text = "صفحة 8", Method = ExtractionMethod.DirectText }
            ],
            UsedOcr = true
        };

        _mockPdfService.Setup(s => s.ExtractAsync(stream, 5, 8, It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractionResult);

        var aiResponse = new GenerateLessonResponse
        {
            Title = "درس كتاب الصلاة",
            Overview = "ملخص عام",
            HistoricalContext = "سياق تاريخي",
            Hadiths =
            [
                new HadithExplanation
                {
                    Reference = "حديث رقم 10",
                    Summary = "ملخص",
                    Problem = "مسألة الصلاة",
                    Conclusion = "وجوب الصلاة",
                    EasyExplanation = "شرح ميسر لمسألة الصلاة.",
                    Lessons = ["فائدة 1"]
                }
            ],
            Connections = ["رابط"],
            ReviewQuestions = ["سؤال"]
        };

        _mockAiService.Setup(s => s.GenerateLessonAsync(
                It.Is<string>(t => t.Contains("صفحة 5") && t.Contains("صفحة 7")),
                It.IsAny<LessonLearningContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("درس كتاب الصلاة");
        result.Hadiths.Should().HaveCount(1);
        result.Metadata.StartPage.Should().Be(5);
        result.Metadata.EndPage.Should().Be(8);
        result.Metadata.TotalPagesProcessed.Should().Be(4);
        result.Metadata.UsedOcr.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WhenExtractionYieldsNoPages_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var stream = new MemoryStream(new byte[50]);
        var command = new GenerateLessonCommand
        {
            PdfStream = stream,
            FileName = "empty.pdf",
            StartPage = 1,
            EndPage = 2
        };

        _mockPdfService.Setup(s => s.ExtractAsync(stream, 1, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfExtractionResult { Pages = [], UsedOcr = false });

        // Act
        var act = () => _handler.HandleAsync(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("No content could be extracted*");
    }

    [Fact]
    public async Task HandleFromTextAsync_WhenGivenPastedText_ShouldGenerateLessonSuccessfully()
    {
        // Arrange
        var command = new GenerateLessonFromTextCommand
        {
            SourceText = "باب كيف كان بدء الوحي إلى رسول الله صلى الله عليه وسلم.",
            StartPage = 1,
            EndPage = 2
        };

        var aiResponse = new GenerateLessonResponse
        {
            Title = "درس بدء الوحي",
            Overview = "مقدمة في بدء الوحي",
            HistoricalContext = "مكة المكرمة",
            Hadiths =
            [
                new HadithExplanation
                {
                    Reference = "حديث رقم 1",
                    Summary = "إنما الأعمال بالنيات",
                    Problem = "مسألة الإخلاص",
                    Conclusion = "وجوب النية في الأعمال",
                    EasyExplanation = "شرح ميسر لأهمية النية الصادقة.",
                    Lessons = ["الإخلاص أساس قبول العمل"]
                }
            ],
            Connections = ["ربط الإيمان بالعمل"],
            ReviewQuestions = ["ما حكم النية؟"]
        };

        _mockAiService.Setup(s => s.GenerateLessonFromTextAsync(
                command.SourceText,
                It.IsAny<LessonLearningContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.HandleFromTextAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("درس بدء الوحي");
        result.Hadiths.Should().HaveCount(1);
        result.Metadata.StartPage.Should().Be(1);
        result.Metadata.EndPage.Should().Be(2);
        result.Metadata.TotalPagesProcessed.Should().Be(2);
    }

    [Fact]
    public async Task HandleFromImagesAsync_WhenGivenPastedScreenshots_ShouldGenerateLessonSuccessfully()
    {
        // Arrange
        var command = new GenerateLessonFromImagesCommand
        {
            Images =
            [
                new PageScreenshot { PageNumber = 1, ImageBytes = [1, 2, 3], MediaType = "image/png" }
            ],
            StartPage = 1,
            EndPage = 1
        };

        var aiResponse = new GenerateLessonResponse
        {
            Title = "درس من لقطة الشاشة",
            Overview = "ملخص الصورة",
            HistoricalContext = "سياق الصورة",
            Hadiths = [],
            Connections = [],
            ReviewQuestions = []
        };

        _mockAiService.Setup(s => s.GenerateLessonAsync(
                command.Images,
                It.IsAny<LessonLearningContext?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.HandleFromImagesAsync(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Title.Should().Be("درس من لقطة الشاشة");
        result.Metadata.StartPage.Should().Be(1);
        result.Metadata.EndPage.Should().Be(1);
        result.Metadata.TotalPagesProcessed.Should().Be(1);
    }
}
