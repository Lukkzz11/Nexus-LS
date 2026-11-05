using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ChequesEmitidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChequesEmitidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LancamentoId = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Banco = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Favorecido = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    BomPara = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Situacao = table.Column<int>(type: "INTEGER", nullable: false),
                    CompensadoEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChequesEmitidos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChequesEmitidos_ContasFinanceiras_ContaId",
                        column: x => x.ContaId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChequesEmitidos_Lancamentos_LancamentoId",
                        column: x => x.LancamentoId,
                        principalTable: "Lancamentos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChequesEmitidos_BomPara",
                table: "ChequesEmitidos",
                column: "BomPara");

            migrationBuilder.CreateIndex(
                name: "IX_ChequesEmitidos_ContaId",
                table: "ChequesEmitidos",
                column: "ContaId");

            migrationBuilder.CreateIndex(
                name: "IX_ChequesEmitidos_Guid",
                table: "ChequesEmitidos",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChequesEmitidos_LancamentoId",
                table: "ChequesEmitidos",
                column: "LancamentoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChequesEmitidos");
        }
    }
}
