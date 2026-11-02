using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ParametrosDaLoja : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChavePix",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CidadeUf",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CupomLinhasFinais",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarDocumento",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarEndereco",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarPix",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarRazaoSocial",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarTelefone",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EstoqueMinimoPadrao",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "FundoTrocoPadraoCentavos",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "InscricaoEstadual",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MargemPadraoPercentual",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RazaoSocial",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            // Quem já usa o programa mantém o cupom como estava: tudo visível e uma linha em branco no fim.
            migrationBuilder.Sql("UPDATE ConfiguracoesLoja SET CupomMostrarRazaoSocial = 1, CupomMostrarDocumento = 1, CupomMostrarEndereco = 1, CupomMostrarTelefone = 1, CupomLinhasFinais = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChavePix",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CidadeUf",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomLinhasFinais",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarDocumento",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarEndereco",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarPix",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarRazaoSocial",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarTelefone",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "EstoqueMinimoPadrao",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "FundoTrocoPadraoCentavos",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "InscricaoEstadual",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "MargemPadraoPercentual",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "RazaoSocial",
                table: "ConfiguracoesLoja");
        }
    }
}
