using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class Financeiro : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Lancamentos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Contraparte = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Vencimento = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PagoEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FormaPagamento = table.Column<int>(type: "INTEGER", nullable: true),
                    GrupoId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Parcela = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalParcelas = table.Column<int>(type: "INTEGER", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Cancelado = table.Column<bool>(type: "INTEGER", nullable: false),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CanceladoEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lancamentos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_GrupoId",
                table: "Lancamentos",
                column: "GrupoId");

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_PagoEm",
                table: "Lancamentos",
                column: "PagoEm");

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_Vencimento",
                table: "Lancamentos",
                column: "Vencimento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Lancamentos");
        }
    }
}
