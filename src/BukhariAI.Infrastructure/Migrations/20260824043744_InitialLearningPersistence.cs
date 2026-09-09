using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialLearningPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Books",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Books", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UsedOcr = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookPages_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonLearningContexts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonLearningContexts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonLearningContexts_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Lessons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Overview = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HistoricalContext = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartPage = table.Column<int>(type: "int", nullable: false),
                    EndPage = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lessons", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lessons_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReadingSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartPage = table.Column<int>(type: "int", nullable: false),
                    EndPage = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReadingSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReadingSessions_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnownPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearningContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstIntroducedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastReferencedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnownPeople_LessonLearningContexts_LearningContextId",
                        column: x => x.LearningContextId,
                        principalTable: "LessonLearningContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnownPeople_Lessons_FirstIntroducedLessonId",
                        column: x => x.FirstIntroducedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnownPeople_Lessons_LastReferencedLessonId",
                        column: x => x.LastReferencedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnownPeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnownTerms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearningContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Term = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FirstIntroducedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastReferencedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownTerms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnownTerms_LessonLearningContexts_LearningContextId",
                        column: x => x.LearningContextId,
                        principalTable: "LessonLearningContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnownTerms_Lessons_FirstIntroducedLessonId",
                        column: x => x.FirstIntroducedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnownTerms_Lessons_LastReferencedLessonId",
                        column: x => x.LastReferencedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnownTopics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LearningContextId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    FirstIntroducedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastReferencedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnownTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnownTopics_LessonLearningContexts_LearningContextId",
                        column: x => x.LearningContextId,
                        principalTable: "LessonLearningContexts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnownTopics_Lessons_FirstIntroducedLessonId",
                        column: x => x.FirstIntroducedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnownTopics_Lessons_LastReferencedLessonId",
                        column: x => x.LastReferencedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonConnections_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonHadiths",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Summary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Problem = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Reasoning = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScholarlyDiscussion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Conclusion = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EasyExplanation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HistoricalContext = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonHadiths", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonHadiths_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonPages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookPageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PageNumber = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonPages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonPages_BookPages_BookPageId",
                        column: x => x.BookPageId,
                        principalTable: "BookPages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LessonPages_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LessonPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContextDescription = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonPeople_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LessonPeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LessonProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UnderstandingLevel = table.Column<int>(type: "int", nullable: true),
                    LastReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LessonProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LessonProgresses_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewQuestions_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HadithEvidences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HadithId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourcePagesCsv = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HadithEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HadithEvidences_LessonHadiths_HadithId",
                        column: x => x.HadithId,
                        principalTable: "LessonHadiths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HadithLessonPoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HadithId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Point = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HadithLessonPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HadithLessonPoints_LessonHadiths_HadithId",
                        column: x => x.HadithId,
                        principalTable: "LessonHadiths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HadithPeople",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    HadithId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PersonId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HadithPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HadithPeople_LessonHadiths_HadithId",
                        column: x => x.HadithId,
                        principalTable: "LessonHadiths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HadithPeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookPages_BookId_PageNumber",
                table: "BookPages",
                columns: new[] { "BookId", "PageNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Books_Title",
                table: "Books",
                column: "Title");

            migrationBuilder.CreateIndex(
                name: "IX_HadithEvidences_HadithId",
                table: "HadithEvidences",
                column: "HadithId");

            migrationBuilder.CreateIndex(
                name: "IX_HadithLessonPoints_HadithId",
                table: "HadithLessonPoints",
                column: "HadithId");

            migrationBuilder.CreateIndex(
                name: "IX_HadithPeople_HadithId_PersonId",
                table: "HadithPeople",
                columns: new[] { "HadithId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HadithPeople_PersonId",
                table: "HadithPeople",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownPeople_FirstIntroducedLessonId",
                table: "KnownPeople",
                column: "FirstIntroducedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownPeople_LastReferencedLessonId",
                table: "KnownPeople",
                column: "LastReferencedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownPeople_LearningContextId_PersonId",
                table: "KnownPeople",
                columns: new[] { "LearningContextId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KnownPeople_PersonId",
                table: "KnownPeople",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTerms_FirstIntroducedLessonId",
                table: "KnownTerms",
                column: "FirstIntroducedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTerms_LastReferencedLessonId",
                table: "KnownTerms",
                column: "LastReferencedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTerms_LearningContextId_Term",
                table: "KnownTerms",
                columns: new[] { "LearningContextId", "Term" });

            migrationBuilder.CreateIndex(
                name: "IX_KnownTopics_FirstIntroducedLessonId",
                table: "KnownTopics",
                column: "FirstIntroducedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTopics_LastReferencedLessonId",
                table: "KnownTopics",
                column: "LastReferencedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_KnownTopics_LearningContextId_Topic",
                table: "KnownTopics",
                columns: new[] { "LearningContextId", "Topic" });

            migrationBuilder.CreateIndex(
                name: "IX_LessonConnections_LessonId",
                table: "LessonConnections",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonHadiths_LessonId",
                table: "LessonHadiths",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonLearningContexts_BookId",
                table: "LessonLearningContexts",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonPages_BookPageId",
                table: "LessonPages",
                column: "BookPageId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonPages_LessonId_BookPageId",
                table: "LessonPages",
                columns: new[] { "LessonId", "BookPageId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonPeople_LessonId_PersonId",
                table: "LessonPeople",
                columns: new[] { "LessonId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LessonPeople_PersonId",
                table: "LessonPeople",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_LessonId",
                table: "LessonProgresses",
                column: "LessonId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_BookId_StartPage_EndPage",
                table: "Lessons",
                columns: new[] { "BookId", "StartPage", "EndPage" });

            migrationBuilder.CreateIndex(
                name: "IX_Lessons_CreatedAtUtc",
                table: "Lessons",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_People_Name",
                table: "People",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_BookId",
                table: "ReadingSessions",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_StartedAtUtc",
                table: "ReadingSessions",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewQuestions_LessonId",
                table: "ReviewQuestions",
                column: "LessonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HadithEvidences");

            migrationBuilder.DropTable(
                name: "HadithLessonPoints");

            migrationBuilder.DropTable(
                name: "HadithPeople");

            migrationBuilder.DropTable(
                name: "KnownPeople");

            migrationBuilder.DropTable(
                name: "KnownTerms");

            migrationBuilder.DropTable(
                name: "KnownTopics");

            migrationBuilder.DropTable(
                name: "LessonConnections");

            migrationBuilder.DropTable(
                name: "LessonPages");

            migrationBuilder.DropTable(
                name: "LessonPeople");

            migrationBuilder.DropTable(
                name: "LessonProgresses");

            migrationBuilder.DropTable(
                name: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "ReviewQuestions");

            migrationBuilder.DropTable(
                name: "LessonHadiths");

            migrationBuilder.DropTable(
                name: "LessonLearningContexts");

            migrationBuilder.DropTable(
                name: "BookPages");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "Lessons");

            migrationBuilder.DropTable(
                name: "Books");
        }
    }
}
