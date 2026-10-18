using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ConfiguracaoCupom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImpressoraCupom",
                table: "ConfiguracoesLoja",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ImprimirCupomAoFinalizar",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true); // quem já tinha configuração salva passa a imprimir ao finalizar (o padrão novo)

            migrationBuilder.AddColumn<int>(
                name: "LarguraCupom",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: 42);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImpressoraCupom",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "ImprimirCupomAoFinalizar",
                table: "ConfiguracoesLoja");

            migrationBuilder.DropColumn(
                name: "LarguraCupom",
                table: "ConfiguracoesLoja");
        }
    }
}
