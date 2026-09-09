using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BukhariAI.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonDetailedBiography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailedBiographyJson",
                table: "People",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailedBiographyJson",
                table: "People");
        }
    }
}
