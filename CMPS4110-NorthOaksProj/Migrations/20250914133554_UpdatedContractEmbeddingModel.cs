using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    /// <inheritdoc />
    public partial class UpdatedContractEmbeddingModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractEmbedding_Contracts_ContractId",
                table: "ContractEmbedding");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ContractEmbedding",
                table: "ContractEmbedding");

            migrationBuilder.RenameTable(
                name: "ContractEmbedding",
                newName: "ContractEmbeddings");

            migrationBuilder.RenameIndex(
                name: "IX_ContractEmbedding_ContractId",
                table: "ContractEmbeddings",
                newName: "IX_ContractEmbeddings_ContractId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContractEmbeddings",
                table: "ContractEmbeddings",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ContractEmbeddings_Contracts_ContractId",
                table: "ContractEmbeddings",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContractEmbeddings_Contracts_ContractId",
                table: "ContractEmbeddings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ContractEmbeddings",
                table: "ContractEmbeddings");

            migrationBuilder.RenameTable(
                name: "ContractEmbeddings",
                newName: "ContractEmbedding");

            migrationBuilder.RenameIndex(
                name: "IX_ContractEmbeddings_ContractId",
                table: "ContractEmbedding",
                newName: "IX_ContractEmbedding_ContractId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ContractEmbedding",
                table: "ContractEmbedding",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ContractEmbedding_Contracts_ContractId",
                table: "ContractEmbedding",
                column: "ContractId",
                principalTable: "Contracts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
