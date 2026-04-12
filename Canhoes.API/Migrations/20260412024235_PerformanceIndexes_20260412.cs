using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Canhoes.Api.Migrations
{
    /// <inheritdoc />
    public partial class PerformanceIndexes_20260412 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AwardCategories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VoteQuestion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VoteRules = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AwardCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CanhoesEventState",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Phase = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NominationsVisible = table.Column<bool>(type: "bit", nullable: false),
                    ResultsVisible = table.Column<bool>(type: "bit", nullable: false),
                    ModuleVisibilityJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "{}")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanhoesEventState", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CategoryProposals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProposedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventMembers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventMembers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventPhases",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    StartDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPhases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostCommentReactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CommentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostCommentReactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostComments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostComments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostLikes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostLikes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostMedia",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ContentType = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContentBytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostMedia", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostPollOptions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostPollOptions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostPolls",
                columns: table => new
                {
                    PostId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Question = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostPolls", x => x.PostId);
                });

            migrationBuilder.CreateTable(
                name: "HubPostPollVotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OptionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostPollVotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPostReactions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PostId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Emoji = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPostReactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HubPosts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    MediaUrl = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    MediaUrlsJson = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValue: "[]"),
                    IsPinned = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HubPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MeasureProposals",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProposedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MeasureProposals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Measures",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Measures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Nominees",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SubmissionKind = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubmittedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nominees", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretSantaAssignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DrawId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    GiverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiverUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretSantaAssignments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretSantaDraws",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretSantaDraws", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsAdmin = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserVotes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    VoterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVotes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Votes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    NomineeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Votes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WishlistItems",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EventId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WishlistItems", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AwardCategories_EventId_SortOrder",
                table: "AwardCategories",
                columns: new[] { "EventId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AwardCategories_IsActive",
                table: "AwardCategories",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_CanhoesEventState_EventId",
                table: "CanhoesEventState",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProposals_EventId_Status",
                table: "CategoryProposals",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProposals_ProposedByUserId",
                table: "CategoryProposals",
                column: "ProposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CategoryProposals_Status",
                table: "CategoryProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_EventMembers_EventId_UserId",
                table: "EventMembers",
                columns: new[] { "EventId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventPhases_EventId_IsActive",
                table: "EventPhases",
                columns: new[] { "EventId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_EventPhases_EventId_Type",
                table: "EventPhases",
                columns: new[] { "EventId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_IsActive",
                table: "Events",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostCommentReactions_CommentId",
                table: "HubPostCommentReactions",
                column: "CommentId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostCommentReactions_CommentId_UserId_Emoji",
                table: "HubPostCommentReactions",
                columns: new[] { "CommentId", "UserId", "Emoji" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HubPostComments_PostId",
                table: "HubPostComments",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostLikes_PostId",
                table: "HubPostLikes",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostLikes_PostId_UserId",
                table: "HubPostLikes",
                columns: new[] { "PostId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HubPostMedia_PostId",
                table: "HubPostMedia",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostMedia_Url",
                table: "HubPostMedia",
                column: "Url",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HubPostPollOptions_PostId_SortOrder",
                table: "HubPostPollOptions",
                columns: new[] { "PostId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_HubPostPollVotes_PostId_UserId",
                table: "HubPostPollVotes",
                columns: new[] { "PostId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HubPostPollVotes_UserId",
                table: "HubPostPollVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostReactions_PostId",
                table: "HubPostReactions",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPostReactions_PostId_UserId_Emoji",
                table: "HubPostReactions",
                columns: new[] { "PostId", "UserId", "Emoji" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HubPosts_AuthorUserId",
                table: "HubPosts",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPosts_CreatedAtUtc",
                table: "HubPosts",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_HubPosts_EventId",
                table: "HubPosts",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_HubPosts_IsPinned",
                table: "HubPosts",
                column: "IsPinned");

            migrationBuilder.CreateIndex(
                name: "IX_MeasureProposals_EventId_Status",
                table: "MeasureProposals",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_MeasureProposals_ProposedByUserId",
                table: "MeasureProposals",
                column: "ProposedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MeasureProposals_Status",
                table: "MeasureProposals",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Measures_IsActive",
                table: "Measures",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Nominees_CategoryId_Status",
                table: "Nominees",
                columns: new[] { "CategoryId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Nominees_EventId_Status",
                table: "Nominees",
                columns: new[] { "EventId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Nominees_SubmittedByUserId",
                table: "Nominees",
                column: "SubmittedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretSantaAssignments_DrawId",
                table: "SecretSantaAssignments",
                column: "DrawId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretSantaAssignments_GiverUserId",
                table: "SecretSantaAssignments",
                column: "GiverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretSantaAssignments_ReceiverUserId",
                table: "SecretSantaAssignments",
                column: "ReceiverUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretSantaDraws_EventCode",
                table: "SecretSantaDraws",
                column: "EventCode");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_ExternalId",
                table: "Users",
                column: "ExternalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserVotes_CategoryId_VoterUserId",
                table: "UserVotes",
                columns: new[] { "CategoryId", "VoterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserVotes_TargetUserId",
                table: "UserVotes",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_CategoryId_UserId",
                table: "Votes",
                columns: new[] { "CategoryId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Votes_NomineeId",
                table: "Votes",
                column: "NomineeId");

            migrationBuilder.CreateIndex(
                name: "IX_Votes_UserId",
                table: "Votes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_EventId",
                table: "WishlistItems",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_WishlistItems_UserId",
                table: "WishlistItems",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AwardCategories");

            migrationBuilder.DropTable(
                name: "CanhoesEventState");

            migrationBuilder.DropTable(
                name: "CategoryProposals");

            migrationBuilder.DropTable(
                name: "EventMembers");

            migrationBuilder.DropTable(
                name: "EventPhases");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "HubPostCommentReactions");

            migrationBuilder.DropTable(
                name: "HubPostComments");

            migrationBuilder.DropTable(
                name: "HubPostLikes");

            migrationBuilder.DropTable(
                name: "HubPostMedia");

            migrationBuilder.DropTable(
                name: "HubPostPollOptions");

            migrationBuilder.DropTable(
                name: "HubPostPolls");

            migrationBuilder.DropTable(
                name: "HubPostPollVotes");

            migrationBuilder.DropTable(
                name: "HubPostReactions");

            migrationBuilder.DropTable(
                name: "HubPosts");

            migrationBuilder.DropTable(
                name: "MeasureProposals");

            migrationBuilder.DropTable(
                name: "Measures");

            migrationBuilder.DropTable(
                name: "Nominees");

            migrationBuilder.DropTable(
                name: "SecretSantaAssignments");

            migrationBuilder.DropTable(
                name: "SecretSantaDraws");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "UserVotes");

            migrationBuilder.DropTable(
                name: "Votes");

            migrationBuilder.DropTable(
                name: "WishlistItems");
        }
    }
}
