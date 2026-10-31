using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ImportacaoErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportacaoErp",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Colecao = table.Column<string>(type: "TEXT", nullable: false),
                    ErpId = table.Column<string>(type: "TEXT", nullable: false),
                    EntidadeGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    ImportadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacaoErp", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportacaoErpCursores",
                columns: table => new
                {
                    Chave = table.Column<string>(type: "TEXT", nullable: false),
                    Valor = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportacaoErpCursores", x => x.Chave);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImportacaoErp_Colecao_ErpId",
                table: "ImportacaoErp",
                columns: new[] { "Colecao", "ErpId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportacaoErp_EntidadeGuid",
                table: "ImportacaoErp",
                column: "EntidadeGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportacaoErp");

            migrationBuilder.DropTable(
                name: "ImportacaoErpCursores");
        }
    }
}
