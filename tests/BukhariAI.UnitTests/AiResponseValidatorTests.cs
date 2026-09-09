using BukhariAI.Infrastructure.AI;
using FluentAssertions;
using Xunit;

namespace BukhariAI.UnitTests;

public class AiResponseValidatorTests
{
    [Fact]
    public void ValidateAndDeserialize_WhenValidJson_ShouldReturnSuccess()
    {
        // Arrange
        string validJson = """
            {
              "Title": "درس في الإخلاص",
              "Overview": "شرح حديث النية",
              "HistoricalContext": "مكة والمدينة",
              "Hadiths": [
                {
                  "Reference": "صحيح البخاري - حديث 1",
                  "SourcePages": [1],
                  "Summary": "إنما الأعمال بالنيات",
                  "Problem": "اشتراط النية في الأعمال",
                  "Evidence": [
                    { "Text": "إنما الأعمال بالنيات", "Role": "دليل أصلي", "SourcePages": [1] }
                  ],
                  "Reasoning": "دلالة إنما على الحصر",
                  "ScholarlyDiscussion": "خلاف في اشتراط النية في الطهارة",
                  "Conclusion": "لا يصح عمل إلا بنية",
                  "EasyExplanation": "النية (القصد والإخلاص) هي شرط قبول كل عمل صالح.",
                  "People": ["عمر بن الخطاب"],
                  "Places": ["المدينة"],
                  "Lessons": ["إخلاص النية"]
                }
              ],
              "Connections": ["مقدمة الكتاب"],
              "ReviewQuestions": ["ما حكم النية؟"]
            }
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(validJson);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("درس في الإخلاص");
        result.Value.Hadiths.Should().HaveCount(1);
        result.Value.Hadiths[0].EasyExplanation.Should().NotBeNullOrWhiteSpace();
        result.Value.Hadiths[0].Evidence[0].SourcePages.Should().Contain(1);
    }

    [Fact]
    public void ValidateAndDeserialize_WhenWrappedInMarkdownCodeFence_ShouldCleanAndParse()
    {
        // Arrange
        string markdownWrappedJson = """
            ```json
            {
              "Title": "درس بدء الوحي",
              "Overview": "مقدمة عامة",
              "HistoricalContext": "غار حراء",
              "Hadiths": [
                {
                  "Reference": "حديث 2",
                  "Summary": "كيفية نزول الوحي",
                  "Problem": "كيف بدأ الوحي إلى رسول الله",
                  "Evidence": [],
                  "Reasoning": "تتابع الرؤيا الصادقة ثم مجيء الملك",
                  "ScholarlyDiscussion": "",
                  "Conclusion": "بدء الوحي كان بالرؤيا الصادقة",
                  "EasyExplanation": "شرح بدء نزول الوحي في غار حراء.",
                  "People": ["عائشة"],
                  "Places": ["مكة"],
                  "Lessons": ["عظمة الوحي"]
                }
              ]
            }
            ```
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(markdownWrappedJson);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("درس بدء الوحي");
    }

    [Fact]
    public void ValidateAndDeserialize_WhenInvalidJson_ShouldReturnControlledFailure()
    {
        // Arrange
        string malformedJson = "{ Title: 'Missing quotes and brackets' ...";

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(malformedJson);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Failed to parse");
        result.Value.Should().BeNull();
    }

    [Fact]
    public void ValidateAndDeserialize_WhenMissingRequiredFields_ShouldReturnControlledFailure()
    {
        // Arrange
        string missingTitleJson = """
            {
              "Title": "",
              "Overview": "نظرة عامة",
              "Hadiths": []
            }
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(missingTitleJson);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Title");
    }

    [Fact]
    public void ValidateAndDeserialize_WhenWrappedInJsonArray_ShouldExtractAndParseSuccessfully()
    {
        // Arrange - reproducing Gemini returning a single-element JSON array
        string arrayWrappedJson = """
            [
              {
                "title": "كتاب البيوع: باب بيع الميتة والأصنام",
                "overview": "شرح أحكام تحريم بيع الميتة والأصنام والخنزير والخمر وما يتعلق بها.",
                "historicalContext": "في عام الفتح بمكة المكرمة",
                "sourcePages": [283, 284],
                "hadiths": [
                  {
                    "reference": "صحيح البخاري - حديث جابر بن عبد الله",
                    "sourcePages": [283],
                    "summary": "تحريم بيع الخمر والميتة والخنزير والأصنام.",
                    "problem": "هل يجوز بيع الميتة أو الانتفاع بشحومها في غير الأكل؟",
                    "evidence": [
                      {
                        "text": "إن الله ورسوله حرم بيع الخمر والميتة والخنزير والأصنام",
                        "role": "دليل أصلي على التحريم",
                        "sourcePages": [283]
                      }
                    ],
                    "reasoning": "دلالة اللفظ الصريح على تحريم البيع مطلقاً.",
                    "scholarlyDiscussion": "مناقشة الانتفاع بدهن الميتة في الاستصباح.",
                    "conclusion": "تحريم البيع وسد الذريعة.",
                    "easyExplanation": "الحديث يبيّن تحريم بيع هذه الأعيان النجسة.",
                    "people": ["جابر بن عبد الله رضي الله عنهما"],
                    "places": ["مكة المكرمة"],
                    "lessons": ["تحريم بيع المحرمات والانتفاع بأثمانها"],
                    "connections": ["ارتباطه بباب الأطعمة"]
                  }
                ],
                "connections": ["الربط العام بين أحكام البيوع والأطعمة"],
                "reviewQuestions": ["ما هي الأعيان التي نص الحديث على تحريم بيعها؟"],
                "assessmentQuestions": [
                  {
                    "question": "بيّن وجه استدلال العلماء بحديث جابر على تحريم شحوم الميتة.",
                    "questionType": "EvidenceAnalysis",
                    "difficulty": "Intermediate",
                    "expectedConcepts": ["تحريم بيع الميتة", "الانتفاع بالشحوم"],
                    "sourcePages": [283],
                    "evaluationGuidance": "أن يوضح الطالب دلالة النهي."
                  }
                ]
              }
            ]
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(arrayWrappedJson);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("كتاب البيوع: باب بيع الميتة والأصنام");
        result.Value.Hadiths.Should().HaveCount(1);
        result.Value.Hadiths[0].Evidence.Should().HaveCount(1);
        result.Value.AssessmentQuestions.Should().HaveCount(1);
        result.Value.AssessmentQuestions[0].Question.Should().Contain("وجه استدلال");
    }

    [Fact]
    public void ValidateAndDeserialize_WhenWrappedInLessonProperty_ShouldUnwrapAndParse()
    {
        // Arrange
        string wrappedObjectJson = """
            {
              "lesson": {
                "title": "باب البيع بالخيار",
                "overview": "شرح خيار المجلس",
                "historicalContext": "المدينة المنورة",
                "hadiths": [
                  {
                    "reference": "حديث ابن عمر",
                    "summary": "المتبايعان بالخيار ما لم يتفرقا",
                    "problem": "ثبوت خيار المجلس للعاقدين",
                    "evidence": [],
                    "reasoning": "صريح اللفظ في إثبات الخيار",
                    "scholarlyDiscussion": "خلاف المالكية والحنفية مع الشافعية والحنابلة",
                    "conclusion": "ثبوت خيار المجلس حتى التفرق",
                    "easyExplanation": "لكل من البائع والمشتري حق التراجع قبل أن يفترقا بأبدانهما.",
                    "people": ["عبد الله بن عمر"],
                    "places": ["المدينة"],
                    "lessons": ["مشروعية التروي في البيع"]
                  }
                ]
              }
            }
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(wrappedObjectJson);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("باب البيع بالخيار");
        result.Value.Hadiths.Should().HaveCount(1);
    }

    [Fact]
    public void ValidateAndDeserialize_WhenSurroundedByConversationalText_ShouldExtractJsonAndParse()
    {
        // Arrange
        string textWithPreamble = """
            Here is the requested lesson in JSON format:
            ```json
            {
              "title": "باب الشفعة",
              "overview": "شرح أحكام الشفعة للجار والشريك",
              "historicalContext": "العصر النبوي",
              "hadiths": [
                {
                  "reference": "حديث جابر",
                  "summary": "قضى النبي بالشفعة فيما لم يقسم",
                  "problem": "متى تثبت الشفعة؟",
                  "evidence": [],
                  "reasoning": "دلالة الحكم على نفي الضرر",
                  "scholarlyDiscussion": "شفعة الجار الملاصق",
                  "conclusion": "الشفعة في المشاع قبل القسمة",
                  "easyExplanation": "حق الشريك في شراء نصيب شريكه قبل الغريب دفعاً للضرر.",
                  "people": ["جابر بن عبد الله"],
                  "places": ["المدينة"],
                  "lessons": ["دفع الضرر عن الشريك"]
                }
              ]
            }
            ```
            I hope this explanation meets your requirements.
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(textWithPreamble);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Title.Should().Be("باب الشفعة");
    }

    [Fact]
    public void ValidateAndDeserialize_WhenMissingEasyExplanation_ShouldReturnControlledFailure()
    {
        // Arrange
        string missingEasyExplanationJson = """
            {
              "Title": "درس تجريبي",
              "Overview": "نظرة عامة",
              "Hadiths": [
                {
                  "Reference": "حديث 1",
                  "Problem": "مسألة معينة",
                  "Conclusion": "خاتمة معينة",
                  "EasyExplanation": ""
                }
              ]
            }
            """;

        // Act
        var result = AiResponseValidator.ValidateAndDeserialize(missingEasyExplanationJson);

        // Assert
        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("EasyExplanation");
    }
}
