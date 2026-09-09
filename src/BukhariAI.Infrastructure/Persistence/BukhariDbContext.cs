using BukhariAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BukhariAI.Infrastructure.Persistence;

public class BukhariDbContext : DbContext
{
    public BukhariDbContext(DbContextOptions<BukhariDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<Book> Books => Set<Book>();

    public DbSet<BookPage> BookPages => Set<BookPage>();

    public DbSet<Lesson> Lessons => Set<Lesson>();

    public DbSet<LessonPage> LessonPages => Set<LessonPage>();

    public DbSet<LessonHadith> LessonHadiths => Set<LessonHadith>();

    public DbSet<HadithEvidence> HadithEvidences => Set<HadithEvidence>();

    public DbSet<HadithLessonPoint> HadithLessonPoints => Set<HadithLessonPoint>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<LessonPerson> LessonPeople => Set<LessonPerson>();

    public DbSet<HadithPerson> HadithPeople => Set<HadithPerson>();

    public DbSet<LessonConnection> LessonConnections => Set<LessonConnection>();

    public DbSet<ReviewQuestion> ReviewQuestions => Set<ReviewQuestion>();

    public DbSet<LessonLearningContext> LessonLearningContexts => Set<LessonLearningContext>();

    public DbSet<KnownPerson> KnownPeople => Set<KnownPerson>();

    public DbSet<KnownTerm> KnownTerms => Set<KnownTerm>();

    public DbSet<KnownTopic> KnownTopics => Set<KnownTopic>();

    public DbSet<ReadingSession> ReadingSessions => Set<ReadingSession>();

    public DbSet<LessonProgress> LessonProgresses => Set<LessonProgress>();

    public DbSet<StudentConceptMastery> StudentConceptMasteries => Set<StudentConceptMastery>();

    public DbSet<Assessment> Assessments => Set<Assessment>();

    public DbSet<AssessmentQuestion> AssessmentQuestions => Set<AssessmentQuestion>();

    public DbSet<StudentAnswer> StudentAnswers => Set<StudentAnswer>();

    public DbSet<AssessmentResult> AssessmentResults => Set<AssessmentResult>();

    public DbSet<ReviewRecommendation> ReviewRecommendations => Set<ReviewRecommendation>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BukhariDbContext).Assembly);
    }
}
