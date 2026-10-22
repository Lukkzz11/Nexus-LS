using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class VendaChaveUnica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ChaveUnica",
                table: "Vendas",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ChaveUnica",
                table: "Vendas",
                column: "ChaveUnica",
                unique: true,
                filter: "\"ChaveUnica\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_ChaveUnica",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ChaveUnica",
                table: "Vendas");
        }
    }
}
