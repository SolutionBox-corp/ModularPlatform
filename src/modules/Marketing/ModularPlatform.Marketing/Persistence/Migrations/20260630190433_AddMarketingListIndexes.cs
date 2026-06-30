using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ModularPlatform.Marketing.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMarketingListIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vibe_conversations_UserId_LastMessageAt_CreatedAt",
                table: "vibe_conversations");

            migrationBuilder.DropIndex(
                name: "IX_metric_snapshots_UserId_Source_RecordedAt",
                table: "metric_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_marketing_analyses_UserId_AnalyzedAt",
                table: "marketing_analyses");

            migrationBuilder.CreateIndex(
                name: "IX_vibe_conversations_UserId_LastMessageAt_CreatedAt_Id",
                table: "vibe_conversations",
                columns: new[] { "UserId", "LastMessageAt", "CreatedAt", "Id" },
                descending: new[] { false, true, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_metric_snapshots_UserId_RecordedAt_Id",
                table: "metric_snapshots",
                columns: new[] { "UserId", "RecordedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_metric_snapshots_UserId_Source_RecordedAt_Id",
                table: "metric_snapshots",
                columns: new[] { "UserId", "Source", "RecordedAt", "Id" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_analyses_UserId_AnalyzedAt_Id",
                table: "marketing_analyses",
                columns: new[] { "UserId", "AnalyzedAt", "Id" },
                descending: new[] { false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_data_pulls_UserId_CreatedAt_Id",
                table: "data_pulls",
                columns: new[] { "UserId", "CreatedAt", "Id" },
                descending: new[] { false, true, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_vibe_conversations_UserId_LastMessageAt_CreatedAt_Id",
                table: "vibe_conversations");

            migrationBuilder.DropIndex(
                name: "IX_metric_snapshots_UserId_RecordedAt_Id",
                table: "metric_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_metric_snapshots_UserId_Source_RecordedAt_Id",
                table: "metric_snapshots");

            migrationBuilder.DropIndex(
                name: "IX_marketing_analyses_UserId_AnalyzedAt_Id",
                table: "marketing_analyses");

            migrationBuilder.DropIndex(
                name: "IX_data_pulls_UserId_CreatedAt_Id",
                table: "data_pulls");

            migrationBuilder.CreateIndex(
                name: "IX_vibe_conversations_UserId_LastMessageAt_CreatedAt",
                table: "vibe_conversations",
                columns: new[] { "UserId", "LastMessageAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_metric_snapshots_UserId_Source_RecordedAt",
                table: "metric_snapshots",
                columns: new[] { "UserId", "Source", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_marketing_analyses_UserId_AnalyzedAt",
                table: "marketing_analyses",
                columns: new[] { "UserId", "AnalyzedAt" });
        }
    }
}
