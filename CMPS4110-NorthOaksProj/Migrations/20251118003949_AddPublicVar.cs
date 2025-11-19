using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    public partial class AddPublicVar : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The FK and index DO NOT EXIST in the database.
            // Comment out everything to avoid errors.

            // migrationBuilder.DropForeignKey(
            //     name: "FK_ChatSessionContracts_Contracts_ContractId",
            //     table: "ChatSessionContracts");

            // migrationBuilder.DropIndex(
            //     name: "IX_ChatSessionContracts_ContractId",
            //     table: "ChatSessionContracts");

            // migrationBuilder.CreateIndex(
            //     name: "IX_ChatSessionContracts_ContractId",
            //     table: "ChatSessionContracts",
            //     column: "ContractId");

            // migrationBuilder.AddForeignKey(
            //     name: "FK_ChatSessionContracts_Contracts_ContractId",
            //     table: "ChatSessionContracts",
            //     column: "ContractId",
            //     principalTable: "Contracts",
            //     principalColumn: "Id",
            //     onDelete: ReferentialAction.Cascade);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to undo because Up() is empty now
        }
    }
}
