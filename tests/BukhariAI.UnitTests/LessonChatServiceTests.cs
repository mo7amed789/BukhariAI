using System.Net;
using System.Text.Json;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.Chat;
using BukhariAI.Domain.Entities;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BukhariAI.UnitTests;

public class LessonChatServiceTests
{
    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public MockHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private BukhariDbContext CreateDbContext(string dbName)
    {
        var options = new DbContextOptionsBuilder<BukhariDbContext>()
            .UseInMemoryDatabase(databaseName: dbName)
            .Options;
        return new BukhariDbContext(options);
    }

    [Fact]
    public async Task AskLessonQuestionAsync_ParsesValidJsonResponse_AndPersistsHistory()
    {
        var jsonResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = "{\n  \"reply\": \"ذهب جمهور الفقهاء إلى عدم جواز تحلية الكعبة بالذهب بقاءً على أصل النهي العام.\",\n  \"suggestedQuestions\": [\"ما حكم كسوتها بالحرير؟\"],\n  \"sourcesCited\": [\"المغني\"]\n}"
                            }
                        }
                    }
                }
            }
        });

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
            return Task.FromResult(resp);
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AiOptions
        {
            ApiKey = "test-gemini-key",
            Provider = "Gemini"
        });

        using var db = CreateDbContext("ChatTest_1");
        var persistenceService = new LessonPersistenceService(db, NullLogger<LessonPersistenceService>.Instance);
        var settingsService = new SettingsService(db);

        var service = new LessonChatService(
            httpClient,
            options,
            db,
            persistenceService,
            settingsService,
            NullLogger<LessonChatService>.Instance);

        var request = new LessonChatRequest
        {
            Message = "ما حكم تحلية الكعبة بالذهب؟"
        };

        var result = await service.AskLessonQuestionAsync(request);

        Assert.NotNull(result);
        Assert.NotEqual(Guid.Empty, result.SessionId);
        Assert.Contains("ذهب جمهور الفقهاء", result.Reply);
        Assert.Single(result.SuggestedQuestions);
        Assert.Equal("ما حكم كسوتها بالحرير؟", result.SuggestedQuestions[0]);
        Assert.Single(result.SourcesCited);
        Assert.Equal("المغني", result.SourcesCited[0]);

        // Verify that session and messages were persisted in database
        var sessions = await service.GetChatSessionsAsync();
        Assert.Single(sessions);
        Assert.Equal(result.SessionId, sessions[0].Id);
        Assert.Equal(2, sessions[0].MessageCount); // User question + Assistant reply

        var sessionDetail = await service.GetChatSessionByIdAsync(result.SessionId);
        Assert.NotNull(sessionDetail);
        Assert.Equal(2, sessionDetail.Messages.Count);
        Assert.Equal("user", sessionDetail.Messages[0].Role);
        Assert.Equal("ما حكم تحلية الكعبة بالذهب؟", sessionDetail.Messages[0].Content);
        Assert.Equal("assistant", sessionDetail.Messages[1].Role);
    }

    [Fact]
    public async Task AskLessonQuestionAsync_HandlesPlainMarkdownGracefully()
    {
        var markdownText = "الشرح المباشر للمسألة دون تغليف بصيغة JSON.";
        var jsonResponse = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new
                            {
                                text = markdownText
                            }
                        }
                    }
                }
            }
        });

        var mockHandler = new MockHttpMessageHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(jsonResponse)
            };
            return Task.FromResult(resp);
        });

        var httpClient = new HttpClient(mockHandler);
        var options = Options.Create(new AiOptions
        {
            ApiKey = "test-gemini-key",
            Provider = "Gemini"
        });

        using var db = CreateDbContext("ChatTest_2");
        var persistenceService = new LessonPersistenceService(db, NullLogger<LessonPersistenceService>.Instance);
        var settingsService = new SettingsService(db);

        var service = new LessonChatService(
            httpClient,
            options,
            db,
            persistenceService,
            settingsService,
            NullLogger<LessonChatService>.Instance);

        var request = new LessonChatRequest
        {
            Message = "مسألة فقهية"
        };

        var result = await service.AskLessonQuestionAsync(request);

        Assert.NotNull(result);
        Assert.Equal(markdownText, result.Reply);
        Assert.NotEmpty(result.SuggestedQuestions);
    }
}
