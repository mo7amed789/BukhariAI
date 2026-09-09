using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ActiveEducationalMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LearningLevel",
                table: "KnownTerms",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TimesSeen",
                table: "KnownTerms",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "LearningLevel",
                table: "KnownPeople",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "ShortBiography",
                table: "KnownPeople",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TimesSeen",
                table: "KnownPeople",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LearningLevel",
                table: "KnownTerms");

            migrationBuilder.DropColumn(
                name: "TimesSeen",
                table: "KnownTerms");

            migrationBuilder.DropColumn(
                name: "LearningLevel",
                table: "KnownPeople");

            migrationBuilder.DropColumn(
                name: "ShortBiography",
                table: "KnownPeople");

            migrationBuilder.DropColumn(
                name: "TimesSeen",
                table: "KnownPeople");
        }
    }
}
