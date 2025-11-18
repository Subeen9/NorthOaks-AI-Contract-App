using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    /// <inheritdoc />
    public partial class AddPublicVar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Drop FK if it exists
            migrationBuilder.DropForeignKey(
                name: "FK_ChatSessionContracts_Contracts_ContractId",
                table: "ChatSessionContracts");

            // Drop index if it exists
            migrationBuilder.DropIndex(
                name: "IX_ChatSessionContracts_ContractId",
                table: "ChatSessionContracts");

            // Recreate index
            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionContracts_ContractId",
                table: "ChatSessionContracts",
                column: "ContractId");

            // Re-add FK
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
        }
    }
}
