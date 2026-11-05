using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class TitulosPrazoELancamentosCorrigidos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanceladoPor",
                table: "Lancamentos",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CorrigeLancamentoId",
                table: "Lancamentos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AjustesTitulosPrazo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorAnteriorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    VencimentoAnterior = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValorNovoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    VencimentoNovo = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjustesTitulosPrazo", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AjustesTitulosPrazo_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_CorrigeLancamentoId",
                table: "Lancamentos",
                column: "CorrigeLancamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesTitulosPrazo_Guid",
                table: "AjustesTitulosPrazo",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AjustesTitulosPrazo_VendaId",
                table: "AjustesTitulosPrazo",
                column: "VendaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AjustesTitulosPrazo");

            migrationBuilder.DropIndex(
                name: "IX_Lancamentos_CorrigeLancamentoId",
                table: "Lancamentos");

            migrationBuilder.DropColumn(
                name: "CanceladoPor",
                table: "Lancamentos");

            migrationBuilder.DropColumn(
                name: "CorrigeLancamentoId",
                table: "Lancamentos");
        }
    }
}
