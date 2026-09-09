using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIdentityAndResourceOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_StudentConceptMasteries_BookId_ConceptKey",
                table: "StudentConceptMasteries");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_AssessmentQuestionId_SubmissionId",
                table: "StudentAnswers");

            migrationBuilder.DropIndex(
                name: "IX_LessonProgresses_LessonId",
                table: "LessonProgresses");

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "StudentConceptMasteries",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "StudentAnswers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LifecycleStatus",
                table: "StudentAnswers",
                type: "int",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "StudentAnswers",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "StudentAnswers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "LessonProgresses",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "LessonProgresses",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.AddColumn<Guid>(
                name: "UserId",
                table: "ChatSessions",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("11111111-1111-1111-1111-111111111111"));

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAtUtc", "Email", "IsActive", "PasswordHash", "Role", "Username" },
                values: new object[] { new Guid("11111111-1111-1111-1111-111111111111"), new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "student@bukhari.ai", true, "100000.v4Wn93m8zT2y+q7J.W4Lg1f+3p8e4d2B1k8v9t0m3=", 1, "default_student" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_BookId_ConceptKey",
                table: "StudentConceptMasteries",
                columns: new[] { "BookId", "ConceptKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_UserId",
                table: "StudentConceptMasteries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_UserId_BookId_ConceptKey",
                table: "StudentConceptMasteries",
                columns: new[] { "UserId", "BookId", "ConceptKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_UserId",
                table: "StudentAnswers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_UserId_AssessmentQuestionId_SubmissionId",
                table: "StudentAnswers",
                columns: new[] { "UserId", "AssessmentQuestionId", "SubmissionId" },
                unique: true,
                filter: "[SubmissionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_LessonId",
                table: "LessonProgresses",
                column: "LessonId");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_UserId_LessonId",
                table: "LessonProgresses",
                columns: new[] { "UserId", "LessonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId",
                table: "ChatSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessions_Users_UserId",
                table: "ChatSessions",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_LessonProgresses_Users_UserId",
                table: "LessonProgresses",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentAnswers_Users_UserId",
                table: "StudentAnswers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_StudentConceptMasteries_Users_UserId",
                table: "StudentConceptMasteries",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessions_Users_UserId",
                table: "ChatSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_LessonProgresses_Users_UserId",
                table: "LessonProgresses");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentAnswers_Users_UserId",
                table: "StudentAnswers");

            migrationBuilder.DropForeignKey(
                name: "FK_StudentConceptMasteries_Users_UserId",
                table: "StudentConceptMasteries");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropIndex(
                name: "IX_StudentConceptMasteries_BookId_ConceptKey",
                table: "StudentConceptMasteries");

            migrationBuilder.DropIndex(
                name: "IX_StudentConceptMasteries_UserId",
                table: "StudentConceptMasteries");

            migrationBuilder.DropIndex(
                name: "IX_StudentConceptMasteries_UserId_BookId_ConceptKey",
                table: "StudentConceptMasteries");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_UserId",
                table: "StudentAnswers");

            migrationBuilder.DropIndex(
                name: "IX_StudentAnswers_UserId_AssessmentQuestionId_SubmissionId",
                table: "StudentAnswers");

            migrationBuilder.DropIndex(
                name: "IX_LessonProgresses_LessonId",
                table: "LessonProgresses");

            migrationBuilder.DropIndex(
                name: "IX_LessonProgresses_UserId_LessonId",
                table: "LessonProgresses");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_UserId",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "StudentConceptMasteries");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "LifecycleStatus",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "StudentAnswers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "LessonProgresses");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "LessonProgresses");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "ChatSessions");

            migrationBuilder.CreateIndex(
                name: "IX_StudentConceptMasteries_BookId_ConceptKey",
                table: "StudentConceptMasteries",
                columns: new[] { "BookId", "ConceptKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StudentAnswers_AssessmentQuestionId_SubmissionId",
                table: "StudentAnswers",
                columns: new[] { "AssessmentQuestionId", "SubmissionId" },
                unique: true,
                filter: "[SubmissionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LessonProgresses_LessonId",
                table: "LessonProgresses",
                column: "LessonId",
                unique: true);
        }
    }
}
