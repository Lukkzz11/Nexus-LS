using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class VenderSemBaixaPerguntarTamanho : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "VenderSemBaixa",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "BaixouEstoque",
                table: "ItensVenda",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "PerguntarTamanho",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VenderSemBaixa",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "BaixouEstoque",
                table: "ItensVenda");

            migrationBuilder.DropColumn(
                name: "PerguntarTamanho",
                table: "ConfiguracoesLoja");
        }
    }
}
