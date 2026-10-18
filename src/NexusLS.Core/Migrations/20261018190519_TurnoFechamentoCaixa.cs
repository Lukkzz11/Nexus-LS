using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class TurnoFechamentoCaixa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TurnosCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataAbertura = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TrocoInicialCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    DataFechamento = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TurnosCaixa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FechamentosCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TurnoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    EsperadoDinheiroCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    ConferidoDinheiroCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    EsperadoCartaoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    ConferidoCartaoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    EsperadoPixCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    ConferidoPixCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FechamentosCaixa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FechamentosCaixa_TurnosCaixa_TurnoId",
                        column: x => x.TurnoId,
                        principalTable: "TurnosCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MovimentosCaixa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TurnoId = table.Column<int>(type: "INTEGER", nullable: false),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovimentosCaixa", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MovimentosCaixa_TurnosCaixa_TurnoId",
                        column: x => x.TurnoId,
                        principalTable: "TurnosCaixa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FechamentosCaixa_TurnoId_Versao",
                table: "FechamentosCaixa",
                columns: new[] { "TurnoId", "Versao" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_TurnoId",
                table: "MovimentosCaixa",
                column: "TurnoId");

            migrationBuilder.CreateIndex(
                name: "IX_TurnosCaixa_DataAbertura",
                table: "TurnosCaixa",
                column: "DataAbertura");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FechamentosCaixa");

            migrationBuilder.DropTable(
                name: "MovimentosCaixa");

            migrationBuilder.DropTable(
                name: "TurnosCaixa");
        }
    }
}
