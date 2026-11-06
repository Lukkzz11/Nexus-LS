using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class CupomOpcoesDoErp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CupomMensagemCabecalho",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarAssinatura",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarCidade",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarCliente",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarEmail",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarInscricao",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarOperador",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarPagamento",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            // Quem já tinha escondido o endereço ou o documento continua com cidade e inscrição escondidas.
            migrationBuilder.Sql("UPDATE ConfiguracoesLoja SET CupomMostrarCidade = CupomMostrarEndereco, CupomMostrarInscricao = CupomMostrarDocumento;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CupomMensagemCabecalho",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarAssinatura",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarCidade",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarCliente",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarEmail",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarInscricao",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarOperador",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarPagamento",
                table: "ConfiguracoesLoja");
        }
    }
}
