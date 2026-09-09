using BukhariAI.Infrastructure.Pdf;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BukhariAI.UnitTests;

public class PdfPageAnalyzerTests
{
    private readonly PdfPageAnalyzer _analyzer;

    public PdfPageAnalyzerTests()
    {
        var options = Options.Create(new PdfExtractionOptions
        {
            MinimumTextCharacters = 50,
            MinimumMeaningfulCharacterRatio = 0.60
        });
        _analyzer = new PdfPageAnalyzer(options);
    }

    [Fact]
    public void Analyze_WhenTextIsEmptyOrWhitespace_ShouldReturnNotUsable()
    {
        // Act
        var resultEmpty = _analyzer.Analyze(string.Empty);
        var resultWhitespace = _analyzer.Analyze("   \n\t  ");
        var resultNull = _analyzer.Analyze(null);

        // Assert
        resultEmpty.IsUsable.Should().BeFalse();
        resultWhitespace.IsUsable.Should().BeFalse();
        resultNull.IsUsable.Should().BeFalse();
    }

    [Fact]
    public void Analyze_WhenTextIsShorterThanMinimumLength_ShouldReturnNotUsable()
    {
        // Arrange
        string shortArabicText = "كتاب بدء الوحي"; // Less than 50 characters

        // Act
        var result = _analyzer.Analyze(shortArabicText);

        // Assert
        result.IsUsable.Should().BeFalse();
        result.Reason.Should().Contain("below minimum threshold");
    }

    [Fact]
    public void Analyze_WhenTextHasMeaningfulArabicContent_ShouldReturnUsable()
    {
        // Arrange
        string arabicContent = """
            حدثنا الحميدي عبد الله بن الزبير قال حدثنا سفيان قال حدثنا يحيى بن سعيد الأنصاري قال أخبرني محمد بن إبراهيم التيمي أنه سمع علقمة بن وقاص الليثي يقول سمعت عمر بن الخطاب رضي الله عنه على المنبر قال سمعت رسول الله صلى الله عليه وسلم يقول إنما الأعمال بالنيات وإنما لكل امرئ ما نوى
            """;

        // Act
        var result = _analyzer.Analyze(arabicContent);

        // Assert
        result.IsUsable.Should().BeTrue();
        result.Reason.Should().Be("Direct text is usable.");
        result.MeaningfulRatio.Should().BeGreaterThan(0.9);
    }

    [Fact]
    public void Analyze_WhenTextIsGarbledOrFullOfControlCharacters_ShouldReturnNotUsable()
    {
        // Arrange
        string garbledContent = new string('\uFFFD', 40) + new string('\0', 30);

        // Act
        var result = _analyzer.Analyze(garbledContent);

        // Assert
        result.IsUsable.Should().BeFalse();
    }
}
