using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Canhoes.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceHotspots_20260420 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Measures_IsActive",
                table: "Measures");

            migrationBuilder.DropIndex(
                name: "IX_MeasureProposals_EventId_Status",
                table: "MeasureProposals");

            migrationBuilder.DropIndex(
                name: "IX_HubPosts_EventId",
                table: "HubPosts");

            migrationBuilder.DropIndex(
                name: "IX_CategoryProposals_EventId_Status",
                table: "CategoryProposals");

            migrationBuilder.DropColumn(
                name: "ContentBytes",
                table: "HubPostMedia");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "Measures",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "HubPostPollVotes",
                type: "nvarchar(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "HubPostPolls",
                type: "nvarchar(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "HubPostPollOptions",
                type: "nvarchar(64)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.CreateTable(
                name: "HubPostDownvotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostDownvotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HubPostDownvotes_HubPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "HubPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Nominees_EventId_Status_CategoryId",
                table: "Nominees",
                columns: new[] { "EventId", "Status", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_Measures_EventId_IsActive",
                table: "Measures",
                columns: new[] { "EventId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasureProposals_EventId_Status_CreatedAtUtc",
                table: "MeasureProposals",
                columns: new[] { "EventId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_HubPosts_EventId_IsPinned_CreatedAtUtc",
                table: "HubPosts",
                columns: new[] { "EventId", "IsPinned", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProposals_EventId_Status_CreatedAtUtc",
                table: "CategoryProposals",
                columns: new[] { "EventId", "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardCategories_EventId_IsActive",
                table: "AwardCategories",
                columns: new[] { "EventId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_HubPostDownvotes_PostId",
                table: "HubPostDownvotes",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostDownvotes_PostId_UserId",
                table: "HubPostDownvotes",
                columns: new[] { "PostId", "UserId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostCommentReactions_HubPostComments_CommentId",
                table: "HubPostCommentReactions",
                column: "CommentId",
                principalTable: "HubPostComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostComments_HubPosts_PostId",
                table: "HubPostComments",
                column: "PostId",
                principalTable: "HubPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostLikes_HubPosts_PostId",
                table: "HubPostLikes",
                column: "PostId",
                principalTable: "HubPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostMedia_HubPosts_PostId",
                table: "HubPostMedia",
                column: "PostId",
                principalTable: "HubPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostPollOptions_HubPostPolls_PostId",
                table: "HubPostPollOptions",
                column: "PostId",
                principalTable: "HubPostPolls",
                principalColumn: "PostId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostPolls_HubPosts_PostId",
                table: "HubPostPolls",
                column: "PostId",
                principalTable: "HubPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostPollVotes_HubPostPolls_PostId",
                table: "HubPostPollVotes",
                column: "PostId",
                principalTable: "HubPostPolls",
                principalColumn: "PostId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HubPostReactions_HubPosts_PostId",
                table: "HubPostReactions",
                column: "PostId",
                principalTable: "HubPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HubPostCommentReactions_HubPostComments_CommentId",
                table: "HubPostCommentReactions");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostComments_HubPosts_PostId",
                table: "HubPostComments");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostLikes_HubPosts_PostId",
                table: "HubPostLikes");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostMedia_HubPosts_PostId",
                table: "HubPostMedia");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostPollOptions_HubPostPolls_PostId",
                table: "HubPostPollOptions");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostPolls_HubPosts_PostId",
                table: "HubPostPolls");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostPollVotes_HubPostPolls_PostId",
                table: "HubPostPollVotes");

            migrationBuilder.DropForeignKey(
                name: "FK_HubPostReactions_HubPosts_PostId",
                table: "HubPostReactions");

            migrationBuilder.DropTable(
                name: "HubPostDownvotes");

            migrationBuilder.DropIndex(
                name: "IX_Nominees_EventId_Status_CategoryId",
                table: "Nominees");

            migrationBuilder.DropIndex(
                name: "IX_Measures_EventId_IsActive",
                table: "Measures");

            migrationBuilder.DropIndex(
                name: "IX_MeasureProposals_EventId_Status_CreatedAtUtc",
                table: "MeasureProposals");

            migrationBuilder.DropIndex(
                name: "IX_HubPosts_EventId_IsPinned_CreatedAtUtc",
                table: "HubPosts");

            migrationBuilder.DropIndex(
                name: "IX_CategoryProposals_EventId_Status_CreatedAtUtc",
                table: "CategoryProposals");

            migrationBuilder.DropIndex(
                name: "IX_AwardCategories_EventId_IsActive",
                table: "AwardCategories");

            migrationBuilder.AlterColumn<string>(
                name: "EventId",
                table: "Measures",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "HubPostPollVotes",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "HubPostPolls",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)");

            migrationBuilder.AlterColumn<string>(
                name: "PostId",
                table: "HubPostPollOptions",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)");

            migrationBuilder.AddColumn<byte[]>(
                name: "ContentBytes",
                table: "HubPostMedia",
                type: "varbinary(max)",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_Measures_IsActive",
                table: "Measures",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_MeasureProposals_EventId_Status",
                table: "MeasureProposals",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_HubPosts_EventId",
                table: "HubPosts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProposals_EventId_Status",
                table: "CategoryProposals",
                columns: new[] { "EventId", "Status" });
        }
    }
}
