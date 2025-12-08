using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionIsPublic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPublic",
                table: "ChatSessions",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPublic",
                table: "ChatSessions");
        }
    }
}
