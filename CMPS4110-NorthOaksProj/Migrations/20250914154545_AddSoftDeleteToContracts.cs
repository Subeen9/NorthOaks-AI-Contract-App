using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CMPS4110_NorthOaksProj.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "isDeleted",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "isDeleted",
                table: "Contracts");
        }
    }
}
