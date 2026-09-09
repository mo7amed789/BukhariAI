using BukhariAI.Api.Controllers;
using BukhariAI.Api.Models;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Infrastructure.Pdf;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace BukhariAI.UnitTests;

public class PageRangeValidationTests
{
    private readonly LessonsController _controller;
    private readonly Mock<IPdfExtractionService> _mockPdfService;
    private readonly Mock<IAiService> _mockAiService;

    public PageRangeValidationTests()
    {
        _mockPdfService = new Mock<IPdfExtractionService>();
        _mockAiService = new Mock<IAiService>();

        var handler = new GenerateLessonHandler(
            _mockPdfService.Object,
            _mockAiService.Object,
            NullLogger<GenerateLessonHandler>.Instance);

        var options = Options.Create(new PdfExtractionOptions
        {
            MaximumPageRange = 20
        });

        var mockPersistence = new Mock<ILessonPersistenceService>();
        var mockEnv = new Mock<IWebHostEnvironment>();
        mockEnv.Setup(e => e.EnvironmentName).Returns("Development");

        _controller = new LessonsController(
            handler,
            mockPersistence.Object,
            options,
            mockEnv.Object,
            NullLogger<LessonsController>.Instance);
    }

    private static IFormFile CreateMockPdfFile(string fileName = "test.pdf", long length = 1024)
    {
        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns(fileName);
        fileMock.Setup(f => f.Length).Returns(length);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[100]));
        return fileMock.Object;
    }

    [Fact]
    public async Task GenerateLesson_WhenRangeIs1To20_ShouldBeValidAndCallHandler()
    {
        // Arrange
        var form = new GenerateLessonFormRequest
        {
            Pdf = CreateMockPdfFile(),
            StartPage = 1,
            EndPage = 20
        };

        _mockPdfService.Setup(p => p.ExtractAsync(It.IsAny<Stream>(), 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PdfExtractionResult
            {
                Pages = [new ExtractedPage { PageNumber = 1, Text = "نص تجريبي", Method = ExtractionMethod.DirectText }],
                UsedOcr = false
            });

        _mockAiService.Setup(a => a.GenerateLessonAsync(It.IsAny<string>(), It.IsAny<LessonLearningContext?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GenerateLessonResponse
            {
                Title = "عنوان تجريبي",
                Overview = "نظرة عامة",
                Hadiths = [new HadithExplanation { Reference = "حديث 1", Problem = "مسألة", Conclusion = "خاتمة", EasyExplanation = "شرح ميسر" }]
            });

        // Act
        var result = await _controller.GenerateLesson(form, CancellationToken.None);

        // Assert
        var okResult = result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeOfType<GenerateLessonResponse>().Subject;
        response.Metadata.StartPage.Should().Be(1);
        response.Metadata.EndPage.Should().Be(20);
        response.Metadata.TotalPagesProcessed.Should().Be(1);
    }

    [Fact]
    public async Task GenerateLesson_WhenStartPageIs0_ShouldReturnBadRequest()
    {
        // Arrange (0 -> 10 is invalid)
        var form = new GenerateLessonFormRequest
        {
            Pdf = CreateMockPdfFile(),
            StartPage = 0,
            EndPage = 10
        };

        // Act
        var result = await _controller.GenerateLesson(form, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain("Start page must be greater than or equal to 1");
    }

    [Fact]
    public async Task GenerateLesson_WhenEndPageIsLessThanStartPage_ShouldReturnBadRequest()
    {
        // Arrange (20 -> 1 is invalid)
        var form = new GenerateLessonFormRequest
        {
            Pdf = CreateMockPdfFile(),
            StartPage = 20,
            EndPage = 1
        };

        // Act
        var result = await _controller.GenerateLesson(form, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain("cannot be less than start page");
    }

    [Fact]
    public async Task GenerateLesson_WhenPageRangeExceedsMaximumLimit_ShouldReturnBadRequest()
    {
        // Arrange (1 -> 25 is 25 pages, limit is 20)
        var form = new GenerateLessonFormRequest
        {
            Pdf = CreateMockPdfFile(),
            StartPage = 1,
            EndPage = 25
        };

        // Act
        var result = await _controller.GenerateLesson(form, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain("exceeds the maximum allowed limit of 20 pages");
    }

    [Fact]
    public async Task GenerateLesson_WhenFileIsNotPdf_ShouldReturnBadRequest()
    {
        // Arrange
        var form = new GenerateLessonFormRequest
        {
            Pdf = CreateMockPdfFile("document.docx"),
            StartPage = 1,
            EndPage = 10
        };

        // Act
        var result = await _controller.GenerateLesson(form, CancellationToken.None);

        // Assert
        var badRequest = result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var problem = badRequest.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Detail.Should().Contain(".pdf");
    }
}
