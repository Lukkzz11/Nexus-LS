using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class CupomReferenciaCaixaGarantia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarAssinaturaCliente",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarReferencia",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CupomTextoGarantia",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomTituloEmCaixa",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CupomMostrarAssinaturaCliente",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarReferencia",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomTextoGarantia",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomTituloEmCaixa",
                table: "ConfiguracoesLoja");
        }
    }
}
