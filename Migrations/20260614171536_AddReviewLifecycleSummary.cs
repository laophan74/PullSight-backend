using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PullSight.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewLifecycleSummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "review_runs",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummaryDetailsJson",
                table: "review_runs",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "review_runs");

            migrationBuilder.DropColumn(
                name: "SummaryDetailsJson",
                table: "review_runs");
        }
    }
}
