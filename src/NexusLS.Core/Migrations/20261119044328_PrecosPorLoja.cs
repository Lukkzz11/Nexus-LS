using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class PrecosPorLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrecosLoja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProdutoGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    PrecoVarejoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    PrecoAtacadoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    DefinidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RecebidoEmUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrecosLoja", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrecosLoja_ProdutoGuid",
                table: "PrecosLoja",
                column: "ProdutoGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrecosLoja");
        }
    }
}
