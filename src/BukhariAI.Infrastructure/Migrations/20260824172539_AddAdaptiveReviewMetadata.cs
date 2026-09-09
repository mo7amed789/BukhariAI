using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdaptiveReviewMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentLearningLevel",
                table: "ReviewRecommendations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                table: "ReviewRecommendations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RecommendedAction",
                table: "ReviewRecommendations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourcePagesCsv",
                table: "ReviewRecommendations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SuggestedQuestionType",
                table: "ReviewRecommendations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentLearningLevel",
                table: "ReviewRecommendations");

            migrationBuilder.DropColumn(
                name: "ReasonCode",
                table: "ReviewRecommendations");

            migrationBuilder.DropColumn(
                name: "RecommendedAction",
                table: "ReviewRecommendations");

            migrationBuilder.DropColumn(
                name: "SourcePagesCsv",
                table: "ReviewRecommendations");

            migrationBuilder.DropColumn(
                name: "SuggestedQuestionType",
                table: "ReviewRecommendations");
        }
    }
}
