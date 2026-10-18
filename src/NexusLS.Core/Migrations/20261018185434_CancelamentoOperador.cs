using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class CancelamentoOperador : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanceladoPor",
                table: "Vendas",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EstoqueDevolvido",
                table: "Vendas",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MotivoCancelamento",
                table: "Vendas",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Operador",
                table: "Vendas",
                type: "TEXT",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomeOperador",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                maxLength: 80,
                nullable: false,
                defaultValue: "Caixa");

            // Vendas já canceladas antes desta versão devolveram o estoque.
            migrationBuilder.Sql("UPDATE Vendas SET EstoqueDevolvido = 1 WHERE Cancelada = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanceladoPor",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "EstoqueDevolvido",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "MotivoCancelamento",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Operador",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "NomeOperador",
                table: "ConfiguracoesLoja");
        }
    }
}
