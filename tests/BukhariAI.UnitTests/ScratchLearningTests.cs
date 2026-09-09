using BukhariAI.Api.Controllers;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.ScratchLearning;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.ScratchLearning;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using Xunit;

namespace BukhariAI.UnitTests;

public class ScratchLearningTests
{
    [Fact]
    public void ScratchDataCatalog_ShouldContainFoundationalCuratedTracks()
    {
        var tracks = ScratchDataCatalog.CuratedTracks;
        tracks.Should().NotBeNullOrEmpty();
        tracks.Count.Should().BeGreaterThanOrEqualTo(5);

        var mustalah = tracks.FirstOrDefault(t => t.Id == "mustalah-hadith");
        mustalah.Should().NotBeNull();
        mustalah!.Title.Should().Contain("مصطلح الحديث");
        mustalah.LevelsCount.Should().Be(4);

        var bukhari = tracks.FirstOrDefault(t => t.Id == "bukhari-methodology");
        bukhari.Should().NotBeNull();
        bukhari!.Title.Should().Contain("صحيح البخاري");

        var rijal = tracks.FirstOrDefault(t => t.Id == "ilm-rijal");
        rijal.Should().NotBeNull();
        rijal!.Title.Should().Contain("علم الرجال");
    }

    [Theory]
    [InlineData("mustalah-hadith")]
    [InlineData("bukhari-methodology")]
    [InlineData("ilm-rijal")]
    [InlineData("usul-tafsir")]
    [InlineData("nahw-turath")]
    public void ScratchDataCatalog_ShouldGenerateValidCuratedRoadmapWith4Levels(string trackId)
    {
        var roadmap = ScratchDataCatalog.GetCuratedRoadmap(trackId);
        roadmap.Should().NotBeNull();
        roadmap.Levels.Should().HaveCount(4);
        roadmap.Levels.SelectMany(l => l.Milestones).Count().Should().Be(8);

        foreach (var level in roadmap.Levels)
        {
            level.LevelNumber.Should().BeInRange(1, 4);
            level.LevelName.Should().NotBeNullOrWhiteSpace();
            level.Milestones.Should().NotBeEmpty();

            foreach (var milestone in level.Milestones)
            {
                milestone.Title.Should().NotBeNullOrWhiteSpace();
                milestone.Explanation.Should().NotBeNull();
                milestone.Explanation!.SimpleConcept.Should().NotBeNullOrWhiteSpace();
                milestone.Explanation.RealWorldAnalogy.Should().NotBeNullOrWhiteSpace();
                milestone.Explanation.Quiz.Should().NotBeNull();
                milestone.Explanation.Quiz!.Options.Should().HaveCountGreaterThanOrEqualTo(2);
                milestone.Explanation.Quiz.CorrectIndex.Should().BeInRange(0, milestone.Explanation.Quiz.Options.Count - 1);
            }
        }
    }

    [Fact]
    public void ScratchPromptBuilder_ShouldConstructStrictPedagogicalPrompts()
    {
        var builder = new ScratchLearningPromptBuilder();
        string systemPrompt = builder.BuildRoadmapSystemPrompt();
        systemPrompt.Should().Contain("المعلم والمؤسس التعليمي الذكي");
        systemPrompt.Should().Contain("المستوى 1: التأسيس والمدخل البديهي");
        systemPrompt.Should().Contain("المستوى 2: البناء والمصطلحات");
        systemPrompt.Should().Contain("المستوى 3: التطبيق ونماذج التراث");
        systemPrompt.Should().Contain("المستوى 4: التعميق والإتقان");
        systemPrompt.Should().Contain("JSON");

        var userPrompt = builder.BuildRoadmapUserPrompt(new ScratchRoadmapRequest
        {
            Topic = "العلل الخفية في الحديث",
            TargetAudience = "مبتدئ",
            DepthLevel = "شامل"
        });
        userPrompt.Should().Contain("العلل الخفية في الحديث");
        userPrompt.Should().Contain("realWorldAnalogy");
        userPrompt.Should().Contain("commonPitfalls");
        userPrompt.Should().Contain("quiz");

        var explainPrompt = builder.BuildExplainStepPrompt(new ExplainScratchStepRequest
        {
            Topic = "مصطلح الحديث",
            LevelNumber = 1,
            LevelName = "التأسيس",
            MilestoneTitle = "السند والمتن",
            KeyTerm = "السند"
        });
        explainPrompt.Should().Contain("السند والمتن");
        explainPrompt.Should().Contain("ELI5");

        var tutorPrompt = builder.BuildAskTutorPrompt(new AskScratchTutorRequest
        {
            Topic = "صحيح البخاري",
            CurrentMilestone = "تراجم الأبواب",
            UserQuestion = "ما معنى التراجم الاستفهامية؟"
        });
        tutorPrompt.Should().Contain("تراجم الأبواب");
        tutorPrompt.Should().Contain("ما معنى التراجم الاستفهامية؟");
    }

    [Fact]
    public async Task ScratchLearningService_ShouldGenerateSyntheticFallbackRoadmapForCustomTopic()
    {
        var httpClient = new HttpClient();
        var aiOptions = Options.Create(new AiOptions { ApiKey = "" }); // No key triggers fallback
        var promptBuilder = new ScratchLearningPromptBuilder();
        var mockSettings = new Mock<ISettingsService>();
        mockSettings.Setup(s => s.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = new ScratchLearningService(
            httpClient,
            aiOptions,
            promptBuilder,
            mockSettings.Object,
            NullLogger<ScratchLearningService>.Instance);

        var response = await service.GenerateRoadmapAsync(new ScratchRoadmapRequest
        {
            Topic = "الناسخ والمنسوخ في القرآن"
        });

        response.Should().NotBeNull();
        response.Topic.Should().Be("الناسخ والمنسوخ في القرآن");
        response.Levels.Should().HaveCount(4);
        response.Levels[0].Milestones.Should().NotBeEmpty();
        response.Levels[0].Milestones[0].Explanation.Should().NotBeNull();
        response.Levels[0].Milestones[0].Explanation!.RealWorldAnalogy.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ScratchLearningService_ExplainStep_ShouldReturnDetailedExplanationWithQuiz()
    {
        var httpClient = new HttpClient();
        var aiOptions = Options.Create(new AiOptions { ApiKey = "" });
        var promptBuilder = new ScratchLearningPromptBuilder();
        var mockSettings = new Mock<ISettingsService>();

        var service = new ScratchLearningService(
            httpClient,
            aiOptions,
            promptBuilder,
            mockSettings.Object,
            NullLogger<ScratchLearningService>.Instance);

        var response = await service.ExplainStepAsync(new ExplainScratchStepRequest
        {
            Topic = "علم الأصول",
            LevelNumber = 2,
            LevelName = "المستوى الثاني",
            MilestoneTitle = "العام والخاص"
        });

        response.Should().NotBeNull();
        response.MilestoneTitle.Should().Be("العام والخاص");
        response.SimpleConcept.Should().NotBeNullOrWhiteSpace();
        response.RealWorldAnalogy.Should().NotBeNullOrWhiteSpace();
        response.Quiz.Should().NotBeNull();
        response.Quiz!.Options.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ScratchLearningService_AskTutor_ShouldReturnFriendlyExplanation()
    {
        var httpClient = new HttpClient();
        var aiOptions = Options.Create(new AiOptions { ApiKey = "" });
        var promptBuilder = new ScratchLearningPromptBuilder();
        var mockSettings = new Mock<ISettingsService>();

        var service = new ScratchLearningService(
            httpClient,
            aiOptions,
            promptBuilder,
            mockSettings.Object,
            NullLogger<ScratchLearningService>.Instance);

        var response = await service.AskTutorAsync(new AskScratchTutorRequest
        {
            Topic = "مصطلح الحديث",
            CurrentMilestone = "العدالة والضبط",
            UserQuestion = "هل نسيان راوٍ لحديث واحد يسقط كل مروياته؟"
        });

        response.Should().NotBeNull();
        response.Answer.Should().NotBeNullOrWhiteSpace();
        response.SimplifiedAnalogy.Should().NotBeNullOrWhiteSpace();
        response.KeyTakeaway.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ScratchLearningController_ShouldValidateRequestAndReturnOk()
    {
        var mockService = new Mock<IScratchLearningService>();
        var sampleRoadmap = ScratchDataCatalog.GetCuratedRoadmap("mustalah-hadith");

        mockService.Setup(s => s.GenerateRoadmapAsync(It.IsAny<ScratchRoadmapRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sampleRoadmap);

        var controller = new ScratchLearningController(
            mockService.Object,
            NullLogger<ScratchLearningController>.Instance);

        var badResult = await controller.GenerateRoadmap(new ScratchRoadmapRequest { Topic = "", TrackId = "" }, CancellationToken.None);
        badResult.Should().BeOfType<BadRequestObjectResult>();

        var okResult = await controller.GenerateRoadmap(new ScratchRoadmapRequest { Topic = "مصطلح الحديث" }, CancellationToken.None);
        okResult.Should().BeOfType<OkObjectResult>();

        var okObj = okResult as OkObjectResult;
        okObj!.Value.Should().BeOfType<ScratchRoadmapResponse>();
    }

    [Fact]
    public void ScratchStudyModels_ShouldSerializeAndDeserializeCorrectly()
    {
        var original = ScratchDataCatalog.GetCuratedRoadmap("mustalah-hadith");
        var json = JsonSerializer.Serialize(original);
        json.Should().NotBeNullOrWhiteSpace();

        var deserialized = JsonSerializer.Deserialize<ScratchRoadmapResponse>(json);
        deserialized.Should().NotBeNull();
        deserialized!.Topic.Should().Be(original.Topic);
        deserialized.Levels.Count.Should().Be(original.Levels.Count);
        deserialized.Levels[0].Milestones[0].Explanation!.Quiz!.Options.Count
            .Should().Be(original.Levels[0].Milestones[0].Explanation!.Quiz!.Options.Count);
    }
}
