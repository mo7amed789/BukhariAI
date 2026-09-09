using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssessmentEvidenceProgressAndReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DemonstratedContextCount",
                table: "StudentConceptMasteries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SubmissionId",
                table: "StudentAnswers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LessonId",
                table: "ReadingSessions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAtUtc",
                table: "LessonProgresses",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "LastAssessmentId",
                table: "LessonProgresses",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AnswerStatus",
                table: "AssessmentResults",
                type: "int",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "ReviewRecommendations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LessonId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ConceptKey = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewRecommendations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewRecommendations_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewRecommendations_Lessons_LessonId",
                        column: x => x.LessonId,
                        principalTable: "Lessons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_AssessmentQuestionId_SubmissionId",
                table: "StudentAnswers",
                columns: new[] { "AssessmentQuestionId", "SubmissionId" },
                unique: true,
                filter: "[SubmissionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingSessions_LessonId",
                table: "ReadingSessions",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_LastAssessmentId",
                table: "LessonProgresses",
                column: "LastAssessmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRecommendations_BookId_ConceptKey_ReviewedAtUtc",
                table: "ReviewRecommendations",
                columns: new[] { "BookId", "ConceptKey", "ReviewedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewRecommendations_LessonId",
                table: "ReviewRecommendations",
                column: "LessonId");

            migrationBuilder.AddForeignKey(
                name: "FK_LessonProgresses_Assessments_LastAssessmentId",
                table: "LessonProgresses",
                column: "LastAssessmentId",
                principalTable: "Assessments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReadingSessions_Lessons_LessonId",
                table: "ReadingSessions",
                column: "LessonId",
                principalTable: "Lessons",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LessonProgresses_Assessments_LastAssessmentId",
                table: "LessonProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_ReadingSessions_Lessons_LessonId",
                table: "ReadingSessions");

            migrationBuilder.DropTable(
                name: "ReviewRecommendations");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_AssessmentQuestionId_SubmissionId",
                table: "StudentAnswers");

            migrationBuilder.DropIndex(
                name: "IX_ReadingSessions_LessonId",
                table: "ReadingSessions");

            migrationBuilder.DropIndex(
                name: "IX_LessonProgresses_LastAssessmentId",
                table: "LessonProgresses");

            migrationBuilder.DropColumn(
                name: "DemonstratedContextCount",
                table: "StudentConceptMasteries");

            migrationBuilder.DropColumn(
                name: "SubmissionId",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "LessonId",
                table: "ReadingSessions");

            migrationBuilder.DropColumn(
                name: "CompletedAtUtc",
                table: "LessonProgresses");

            migrationBuilder.DropColumn(
                name: "LastAssessmentId",
                table: "LessonProgresses");

            migrationBuilder.DropColumn(
                name: "AnswerStatus",
                table: "AssessmentResults");
        }
    }
}
