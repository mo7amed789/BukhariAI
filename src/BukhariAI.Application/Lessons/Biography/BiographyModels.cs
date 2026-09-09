namespace BukhariAI.Application.Lessons.Biography;

public sealed class PersonBiographyDto
{
    public Guid? PersonId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Kunya { get; set; } = string.Empty;

    public string Era { get; set; } = string.Empty;

    public string DeathYear { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string SiyarBiography { get; set; } = string.Empty;

    public List<string> Teachers { get; set; } = [];

    public List<string> Students { get; set; } = [];

    public List<ScholarlyPraiseDto> ScholarlyPraise { get; set; } = [];

    public string VirtuesAndNarrations { get; set; } = string.Empty;

    public List<string> Sources { get; set; } = [];
}

public sealed class ScholarlyPraiseDto
{
    public string Scholar { get; set; } = string.Empty;

    public string Quote { get; set; } = string.Empty;
}

public sealed class GetPersonBiographyRequest
{
    public string Name { get; set; } = string.Empty;

    public string? ContextDescription { get; set; }

    public Guid? LessonId { get; set; }

    public Guid? BookId { get; set; }
    public bool ForceRefresh { get; set; }
}
