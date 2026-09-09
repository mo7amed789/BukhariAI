using BukhariAI.Api.Helpers;
using BukhariAI.Api.Services;
using BukhariAI.Application.Abstractions;
using BukhariAI.Application.Lessons.GenerateLesson;
using BukhariAI.Application.Lessons.Biography;
using BukhariAI.Application.Quran;
using BukhariAI.Application.ScratchLearning;
using BukhariAI.Infrastructure.AI;
using BukhariAI.Infrastructure.AdaptiveLearning;
using BukhariAI.Infrastructure.Auth;
using BukhariAI.Infrastructure.Mastery;
using BukhariAI.Infrastructure.Pdf;
using BukhariAI.Infrastructure.Persistence;
using BukhariAI.Infrastructure.Quran;
using BukhariAI.Infrastructure.ScratchLearning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

// Register global crash handlers for unobserved/fatal errors
AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[FATAL UNHANDLED APPDOMAIN EXCEPTION] {eventArgs.ExceptionObject}");
    Console.ResetColor();
};

TaskScheduler.UnobservedTaskException += (sender, eventArgs) =>
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[UNOBSERVED TASK EXCEPTION] {eventArgs.Exception}");
    Console.ResetColor();
    eventArgs.SetObserved();
};

var builder = WebApplication.CreateBuilder(args);

// Cloud deployment port binding (Render, Cloud Run, Heroku pass PORT env var)
var portEnv = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(portEnv))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{portEnv}");
}

// Configure bounded Kestrel limits for uploads (50 MB max)
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxRequestBodySize = 50 * 1024 * 1024; // 50 MB
});

// Configure bounded Form options for multipart uploads
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024; // 50 MB
    options.ValueLengthLimit = 10 * 1024 * 1024;        // 10 MB
    options.MultipartHeadersLengthLimit = 32 * 1024;     // 32 KB
});

// User Identity & Security Services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();

// Authentication & JWT
string jwtSecret = builder.Configuration["Jwt:SecretKey"] 
    ?? "BukhariAI_SuperSecret_Production_Ready_Key_MustBe32BytesLong!";
string jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "BukhariAI";
string jwtAudience = builder.Configuration["Jwt:Audience"] ?? "BukhariAIApp";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    };
});
builder.Services.AddAuthorization();

// Rate Limiting (Partitioned by Authenticated User with IP fallback)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("HeavyAiPolicy", httpContext =>
    {
        var currentUserService = httpContext.RequestServices.GetRequiredService<ICurrentUserService>();
        string partitionKey = currentUserService.IsAuthenticated 
            ? $"user_{currentUserService.UserId}" 
            : $"ip_{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 15,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("StandardPolicy", httpContext =>
    {
        var currentUserService = httpContext.RequestServices.GetRequiredService<ICurrentUserService>();
        string partitionKey = currentUserService.IsAuthenticated 
            ? $"user_{currentUserService.UserId}" 
            : $"ip_{httpContext.Connection.RemoteIpAddress}";

        return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

// Add Controllers
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

// Configure Options
builder.Services.Configure<PdfExtractionOptions>(
    builder.Configuration.GetSection(PdfExtractionOptions.SectionName));
builder.Services.Configure<AiOptions>(
    builder.Configuration.GetSection(AiOptions.SectionName));

// Configure Database & Persistence
builder.Services.AddDbContext<BukhariDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=MyDatabase;Trusted_Connection=True;TrustServerCertificate=True;";
    options.UseSqlServer(connectionString, sqlOptions =>
        sqlOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
});
builder.Services.AddScoped<ILessonPersistenceService, LessonPersistenceService>();
builder.Services.AddScoped<IMasteryEngineService, MasteryEngineService>();
builder.Services.AddScoped<IStudentLearningService, StudentLearningService>();
builder.Services.AddScoped<IAdaptiveLearningService, AdaptiveLearningService>();
builder.Services.AddScoped<ILearningSessionService, LearningSessionService>();
builder.Services.AddScoped<IAssessmentService, AssessmentService>();
builder.Services.AddScoped<ISettingsService, SettingsService>();

// Register Application Services
builder.Services.AddSingleton<LessonPromptBuilder>();
builder.Services.AddSingleton<QuranPromptBuilder>();
builder.Services.AddSingleton<BiographyPromptBuilder>();
builder.Services.AddSingleton<ScratchLearningPromptBuilder>();
builder.Services.AddScoped<GenerateLessonHandler>();

// Register Infrastructure Services
builder.Services.AddScoped<IPdfExtractionService, PdfExtractionService>();
builder.Services.AddHttpClient<IQuranAnalysisService, QuranAnalysisService>(client => client.Timeout = TimeSpan.FromMinutes(4));
builder.Services.AddHttpClient<ILessonChatService, LessonChatService>(client => client.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddHttpClient<IPersonBiographyService, PersonBiographyService>(client => client.Timeout = TimeSpan.FromMinutes(3));
builder.Services.AddHttpClient<IScratchLearningService, ScratchLearningService>(client => client.Timeout = TimeSpan.FromMinutes(4));

// Register a real Vision-capable AI provider. Mock responses are intentionally unsupported.
string aiProvider = builder.Configuration.GetValue<string>("AI:Provider") ?? "Gemini";
if (string.Equals(aiProvider, "Mock", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("AI:Provider=Mock is not supported. Configure a Vision-capable provider.");
}
else if (string.Equals(aiProvider, "OpenAI", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(aiProvider, "OpenCode", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IAiService, AiService>(client => client.Timeout = TimeSpan.FromMinutes(3));
    builder.Services.AddHttpClient<IAssessmentEvaluator, AiAssessmentEvaluator>();
}
else
{
    builder.Services.AddHttpClient<IAiService, GeminiAiService>(client => client.Timeout = TimeSpan.FromMinutes(3));
    builder.Services.AddHttpClient<IAssessmentEvaluator, AiAssessmentEvaluator>();
}

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Bukhari AI API",
        Version = "v1",
        Description = "Backend MVP API for Sahih al-Bukhari educational lesson generation with user authentication and data ownership."
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT"
    });
});

var app = builder.Build();

// Global Exception Diagnostics Middleware
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Global Pipeline Caught Unhandled Exception: {Message}\nStackTrace: {StackTrace}", ex.Message, ex.StackTrace);

        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                title = "Unhandled Server Exception",
                status = 500,
                detail = app.Environment.IsDevelopment() ? ex.ToString() : "An unexpected server error occurred."
            });
        }
    }
});

// Forwarded Headers for reverse proxy setups (Nginx, Docker, Caddy, Cloudflare, Traefik)
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableSwagger", false))
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Bukhari AI API v1");
        c.RoutePrefix = "swagger";
    });
}

if (!app.Configuration.GetValue<bool>("DisableHttpsRedirection", false))
{
    app.UseHttpsRedirection();
}
app.UseDefaultFiles();
app.UseStaticFiles();

// Security Headers (SEC-08)
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Ensure database migrations are applied and sanitize concept data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BukhariDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        if (db.Database.IsRelational())
        {
            db.Database.Migrate();
        }

        // Automatic self-healing: sanitize any corrupted concept records
        await ConceptCleanupService.CleanupBrokenConceptsAsync(db, logger);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Automatic database migration or concept cleanup skipped or encountered an issue during startup: {Message}", ex.Message);
    }
}

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
