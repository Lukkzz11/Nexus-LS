using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class LogoEEnderecoDoCupom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Bairro",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cep",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CupomMostrarLogo",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EnderecoNumero",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "LogoCupom",
                table: "ConfiguracoesLoja",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Segmento",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bairro",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "Cep",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "CupomMostrarLogo",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "EnderecoNumero",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "LogoCupom",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "Segmento",
                table: "ConfiguracoesLoja");
        }
    }
}
