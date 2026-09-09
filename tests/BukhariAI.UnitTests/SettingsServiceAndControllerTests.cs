using BukhariAI.Api.Controllers;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace BukhariAI.UnitTests;

public class SettingsServiceAndControllerTests
{
    private BukhariDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new BukhariDbContext(options);
    }

    [Fact]
    public async Task SettingsService_SaveAndGet_WorksCorrectly()
    {
        using var db = CreateDbContext("SettingsService_Test");
        var service = new SettingsService(db);

        var initial = await service.GetAllAsync();
        Assert.Empty(initial);

        var toSave = new Dictionary<string, string>
        {
            ["AI:FallbackApiKey"] = "sk-test-key",
            ["AI:FallbackModel"] = "claude-sonnet-4-5",
            ["AI:AssessmentSystemPrompt"] = "Custom Assessment Prompt"
        };

        await service.SaveAsync(toSave);

        var stored = await service.GetAllAsync();
        Assert.Equal(3, stored.Count);
        Assert.Equal("sk-test-key", stored["AI:FallbackApiKey"]);
        Assert.Equal("claude-sonnet-4-5", stored["AI:FallbackModel"]);
        Assert.Equal("Custom Assessment Prompt", stored["AI:AssessmentSystemPrompt"]);

        // Update existing key
        await service.SaveAsync(new Dictionary<string, string>
        {
            ["AI:FallbackModel"] = "gpt-4o"
        });

        var updatedModel = await service.GetAsync("AI:FallbackModel");
        Assert.Equal("gpt-4o", updatedModel);
    }

    [Fact]
    public async Task SettingsController_GetAndPut_WorksCorrectly()
    {
        using var db = CreateDbContext("SettingsController_Test");
        var service = new SettingsService(db);
        var promptBuilder = new LessonPromptBuilder();
        var aiOptions = Options.Create(new AiOptions
        {
            FallbackApiKey = "sk-default-options-key",
            FallbackModel = "claude-sonnet-4-5"
        });

        var controller = new SettingsController(service, promptBuilder, aiOptions);

        // Get default settings
        var getResult = await controller.GetSettings(CancellationToken.None);
        var okResult = Assert.IsType<OkObjectResult>(getResult);
        var settingsDict = Assert.IsAssignableFrom<Dictionary<string, string>>(okResult.Value);

        Assert.Contains("AI:AssessmentSystemPrompt", settingsDict.Keys);
        Assert.Contains("AI:LessonSystemPrompt", settingsDict.Keys);
        Assert.Equal("sk-****-key", settingsDict["AI:FallbackApiKey"]);
        Assert.Equal("claude-sonnet-4-5", settingsDict["AI:FallbackModel"]);

        // Update settings via controller
        var updatePayload = new Dictionary<string, string>
        {
            ["AI:CustomResponseInstructions"] = "اشرح لي بأسلوب مبسط",
            ["AI:FallbackApiKey"] = "sk-updated-key",
            ["AI:AssessmentSystemPrompt"] = "My Custom System Prompt"
        };

        var putResult = await controller.SaveSettings(updatePayload, CancellationToken.None);
        Assert.IsType<OkObjectResult>(putResult);

        // Verify updated get
        var getUpdatedResult = await controller.GetSettings(CancellationToken.None);
        var okUpdated = Assert.IsType<OkObjectResult>(getUpdatedResult);
        var updatedDict = Assert.IsAssignableFrom<Dictionary<string, string>>(okUpdated.Value);

        Assert.Equal("اشرح لي بأسلوب مبسط", updatedDict["AI:CustomResponseInstructions"]);
        Assert.Equal("sk-****-key", updatedDict["AI:FallbackApiKey"]);
        Assert.Equal("My Custom System Prompt", updatedDict["AI:AssessmentSystemPrompt"]);
    }
}