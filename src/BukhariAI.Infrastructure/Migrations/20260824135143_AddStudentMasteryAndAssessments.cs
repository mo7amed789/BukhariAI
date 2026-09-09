using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentMasteryAndAssessments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assessments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assessments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Assessments_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Assessments_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentConceptMasteries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConceptKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    LearningLevel = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ExposureCount = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    AssessmentCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CorrectAnswerCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    MasteryScore = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: false, defaultValue: 0.0),
                    LastAssessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstIntroducedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastAssessedLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentConceptMasteries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentConceptMasteries_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentConceptMasteries_Lessons_FirstIntroducedLessonId",
                        column: x => x.FirstIntroducedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StudentConceptMasteries_Lessons_LastAssessedLessonId",
                        column: x => x.LastAssessedLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentQuestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SourceLessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Question = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    QuestionType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "ConceptualExplanation"),
                    Difficulty = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false, defaultValue: "Intermediate"),
                    ExpectedConceptsCsv = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, defaultValue: ""),
                    SourcePagesCsv = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    EvaluationGuidance = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: ""),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentQuestions_Assessments_AssessmentId",
                        column: x => x.AssessmentId,
                        principalTable: "Assessments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AssessmentQuestions_Lessons_SourceLessonId",
                        column: x => x.SourceLessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StudentAnswers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssessmentQuestionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnswerText = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentAnswers_AssessmentQuestions_AssessmentQuestionId",
                        column: x => x.AssessmentQuestionId,
                        principalTable: "AssessmentQuestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AssessmentResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StudentAnswerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Score = table.Column<double>(type: "float(5)", precision: 5, scale: 4, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    UnderstoodConceptsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: "[]"),
                    MissingConceptsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: "[]"),
                    MisconceptionsJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: "[]"),
                    Feedback = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false, defaultValue: ""),
                    EvaluatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssessmentResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssessmentResults_StudentAnswers_StudentAnswerId",
                        column: x => x.StudentAnswerId,
                        principalTable: "StudentAnswers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQuestions_AssessmentId",
                table: "AssessmentQuestions",
                column: "AssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentQuestions_SourceLessonId",
                table: "AssessmentQuestions",
                column: "SourceLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_AssessmentResults_StudentAnswerId",
                table: "AssessmentResults",
                column: "StudentAnswerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_BookId",
                table: "Assessments",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_Assessments_LessonId",
                table: "Assessments",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_AssessmentQuestionId",
                table: "StudentAnswers",
                column: "AssessmentQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_BookId_ConceptKey",
                table: "StudentConceptMasteries",
                columns: new[] { "BookId", "ConceptKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_FirstIntroducedLessonId",
                table: "StudentConceptMasteries",
                column: "FirstIntroducedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_LastAssessedLessonId",
                table: "StudentConceptMasteries",
                column: "LastAssessedLessonId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_LearningLevel",
                table: "StudentConceptMasteries",
                column: "LearningLevel");

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_MasteryScore",
                table: "StudentConceptMasteries",
                column: "MasteryScore");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssessmentResults");

            migrationBuilder.DropTable(
                name: "StudentConceptMasteries");

            migrationBuilder.DropTable(
                name: "StudentAnswers");

            migrationBuilder.DropTable(
                name: "AssessmentQuestions");

            migrationBuilder.DropTable(
                name: "Assessments");
        }
    }
}
