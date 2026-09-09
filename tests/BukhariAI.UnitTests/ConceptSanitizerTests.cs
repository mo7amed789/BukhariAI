using BukhariAI.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace BukhariAI.UnitTests;

public class ConceptSanitizerTests
{
    [Theory]
    [InlineData("لله عنه وجود أموال عظيمة من صفراء وبيضاء")]
    [InlineData("سة فلا يجوز نقلها أو قسمتها مطلقاً، وبين")]
    [InlineData("ن إجراء اجتهاده، حتى ذكّره شيبة بن عثمان")]
    [InlineData("ة في المسألة")]
    [InlineData("قال رسول الله صلى الله عليه وسلم")]
    [InlineData("روى البخاري في صحيحه")]
    [InlineData("وهذا يدل على أن")]
    [InlineData("حكم المسألة، وبيان حكمها")]
    [InlineData("أ")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("هذا نص طويل جداً جداً جداً جداً يحتوي على أكثر من ثماني كلمات وعبارات متسلسلة")]
    public void IsValidConcept_ShouldRejectInvalidOrCorruptConcepts(string? concept)
    {
        bool isValid = ConceptSanitizer.IsValidConcept(concept);
        isValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("حكم أموال وخزانة الكعبة")]
    [InlineData("طهارة جلد الميتة بالدباغ")]
    [InlineData("كسوة الكعبة وقسمتها")]
    [InlineData("تخصيص العموم بالدليل الخاص")]
    [InlineData("حكم الصلاة في المسجد الحرام")]
    [InlineData("استعمال جلد الميتة قبل الدباغ")]
    public void IsValidConcept_ShouldAcceptCleanScholarlyTopics(string concept)
    {
        bool isValid = ConceptSanitizer.IsValidConcept(concept);
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("وحكم أموال الكعبة", "حكم أموال الكعبة")]
    [InlineData("«طهارة جلد الميتة بالدباغ»", "طهارة جلد الميتة بالدباغ")]
    [InlineData("فـكسوة الكعبة", "كسوة الكعبة")]
    [InlineData("بـطهارة الجلود", "طهارة الجلود")]
    public void CleanAndValidate_ShouldSanitizeAndValidateValidConcepts(string input, string expected)
    {
        string? result = ConceptSanitizer.CleanAndValidate(input);
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("لله عنه وجود أموال عظيمة")]
    [InlineData("سة فلا يجوز نقلها")]
    [InlineData("ن إجراء اجتهاده، حتى ذكّره")]
    public void CleanAndValidate_ShouldReturnNullForCorruptFragments(string input)
    {
        string? result = ConceptSanitizer.CleanAndValidate(input);
        result.Should().BeNull();
    }

    [Fact]
    public void DeriveTopicTitle_ShouldGenerateConciseScholarlyTitle()
    {
        string problem = "هل يجوز قسمة أموال الكعبة وتجريدها من حليها وخزانتها في مصالح المسلمين؟";
        string title = ConceptSanitizer.DeriveTopicTitle(problem);

        title.Should().NotBeNullOrWhiteSpace();
        title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length.Should().BeInRange(2, 6);
        title.Should().NotContain("؟");
        title.Should().NotContain("هل");
    }

    [Fact]
    public void DeriveTopicTitle_WithNull_ShouldReturnFallback()
    {
        string title = ConceptSanitizer.DeriveTopicTitle(null, "مسألة علمية");
        title.Should().Be("مسألة علمية");
    }
}
