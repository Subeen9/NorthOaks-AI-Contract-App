using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    /// <inheritdoc />
    public partial class originalchunk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalChunkText",
                table: "ContractEmbeddings",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionContracts_ContractId",
                table: "ChatSessionContracts",
                column: "ContractId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatSessionContracts_Contracts_ContractId",
                table: "ChatSessionContracts",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessionContracts_Contracts_ContractId",
                table: "ChatSessionContracts");

            migrationBuilder.DropIndex(
                name: "IX_ChatSessionContracts_ContractId",
                table: "ChatSessionContracts");

            migrationBuilder.DropColumn(
                name: "OriginalChunkText",
                table: "ContractEmbeddings");
        }
    }
}
