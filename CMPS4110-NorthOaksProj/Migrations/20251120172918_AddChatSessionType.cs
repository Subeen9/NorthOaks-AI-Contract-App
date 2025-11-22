using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionType",
                table: "ChatSessions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionType",
                table: "ChatSessions");

          
        }
    }
}
