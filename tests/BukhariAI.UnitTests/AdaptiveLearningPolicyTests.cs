using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AdaptiveLearning;
using FluentAssertions;
using Xunit;

namespace BukhariAI.UnitTests;

public sealed class AdaptiveLearningPolicyTests
{
    [Theory]
    [InlineData(LearningLevel.Unknown, AdaptiveLearningAction.Introduce)]
    [InlineData(LearningLevel.Introduced, AdaptiveLearningAction.Explain)]
    [InlineData(LearningLevel.Familiar, AdaptiveLearningAction.AskQuestion)]
    [InlineData(LearningLevel.Understood, AdaptiveLearningAction.VerifyMastery)]
    [InlineData(LearningLevel.Mastered, AdaptiveLearningAction.NoAction)]
    public void StateSelectsDeterministicAction(LearningLevel level, AdaptiveLearningAction expected)
    {
        var result = AdaptiveLearningPolicy.Evaluate(new StudentConceptMastery
        {
            ConceptKey = "الدباغ", LearningLevel = level, ExposureCount = 3, AssessmentCount = 3,
            CorrectAnswerCount = 3, DemonstratedContextCount = 3, MasteryScore = .9, LastAssessedAt = DateTime.UtcNow
        }, DateTime.UtcNow);

        result.Action.Should().Be(expected);
    }

    [Fact]
    public void FailedFamiliarConceptReceivesReinforcementAndLowerConfidence()
    {
        var result = AdaptiveLearningPolicy.Evaluate(new StudentConceptMastery
        {
            ConceptKey = "الولاء", LearningLevel = LearningLevel.Familiar, ExposureCount = 4, AssessmentCount = 3,
            CorrectAnswerCount = 0, MasteryScore = .25, LastAssessedAt = DateTime.UtcNow
        }, DateTime.UtcNow);
        result.Action.Should().Be(AdaptiveLearningAction.Reinforce);
        result.IsWeak.Should().BeTrue();
        result.SuggestedQuestionType.Should().Be("Understanding");
    }

    [Fact]
    public void OldUnderstoodConceptIsDueForReview()
    {
        var result = AdaptiveLearningPolicy.Evaluate(new StudentConceptMastery
        {
            ConceptKey = "الإهاب", LearningLevel = LearningLevel.Understood, ExposureCount = 3, AssessmentCount = 2,
            CorrectAnswerCount = 2, DemonstratedContextCount = 2, MasteryScore = .75, LastAssessedAt = DateTime.UtcNow.AddDays(-22)
        }, DateTime.UtcNow);
        result.Action.Should().Be(AdaptiveLearningAction.Review);
        result.IsReviewDue.Should().BeTrue();
    }
}
