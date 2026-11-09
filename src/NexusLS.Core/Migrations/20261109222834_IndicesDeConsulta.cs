using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class IndicesDeConsulta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MovimentosCaixa_TurnoId",
                table: "MovimentosCaixa");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_DataHora_Cancelada",
                table: "Vendas",
                columns: new[] { "DataHora", "Cancelada" });

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCliente_DataHora",
                table: "PagamentosCliente",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_TurnoId_DataHora",
                table: "MovimentosCaixa",
                columns: new[] { "TurnoId", "DataHora" });

            migrationBuilder.CreateIndex(
                name: "IX_FilaSincronizacao_Estado_AtualizadoEm",
                table: "FilaSincronizacao",
                columns: new[] { "Estado", "AtualizadoEm" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_DataHora_Cancelada",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_PagamentosCliente_DataHora",
                table: "PagamentosCliente");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosCaixa_TurnoId_DataHora",
                table: "MovimentosCaixa");

            migrationBuilder.DropIndex(
                name: "IX_FilaSincronizacao_Estado_AtualizadoEm",
                table: "FilaSincronizacao");

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_TurnoId",
                table: "MovimentosCaixa",
                column: "TurnoId");
        }
    }
}
