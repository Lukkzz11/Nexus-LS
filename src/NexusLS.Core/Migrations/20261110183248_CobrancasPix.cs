using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class CobrancasPix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CobrancasPix",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChaveVenda = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Gateway = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    IdExterno = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    CopiaECola = table.Column<string>(type: "TEXT", nullable: false),
                    QrCodeBase64 = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadaEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiraEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CobrancasPix", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_ChaveVenda",
                table: "CobrancasPix",
                column: "ChaveVenda",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CobrancasPix_CriadaEm",
                table: "CobrancasPix",
                column: "CriadaEm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CobrancasPix");
        }
    }
}
