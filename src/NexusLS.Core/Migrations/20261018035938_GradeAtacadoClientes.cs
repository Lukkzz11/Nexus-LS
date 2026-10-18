using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class GradeAtacadoClientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClienteId",
                table: "Vendas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Parcelas",
                table: "Vendas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // vendas antigas: 1 parcela

            migrationBuilder.AddColumn<int>(
                name: "TabelaPreco",
                table: "Vendas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1); // vendas antigas: varejo (TabelaPreco.Varejo = 1)

            migrationBuilder.AddColumn<long>(
                name: "PrecoAtacadoCentavos",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Referencia",
                table: "Produtos",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tamanho",
                table: "Movimentacoes",
                type: "TEXT",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tamanho",
                table: "ItensVenda",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Sem tamanho"); // itens de vendas antigas saíram do estoque geral

            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Documento = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Telefone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfiguracoesLoja",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    NomeLoja = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Documento = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Endereco = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true),
                    Telefone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    MensagemCupom = table.Column<string>(type: "TEXT", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfiguracoesLoja", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TamanhosProduto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tamanho = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Estoque = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TamanhosProduto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TamanhosProduto_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserva o estoque que já existe: cada produto ganha a linha "Sem tamanho" com o estoque atual dele.
            migrationBuilder.Sql(
                "INSERT INTO TamanhosProduto (ProdutoId, Tamanho, Estoque) " +
                "SELECT Id, 'Sem tamanho', EstoqueAtual FROM Produtos;");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ClienteId",
                table: "Vendas",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Referencia",
                table: "Produtos",
                column: "Referencia");

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Nome",
                table: "Clientes",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_TamanhosProduto_ProdutoId_Tamanho",
                table: "TamanhosProduto",
                columns: new[] { "ProdutoId", "Tamanho" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendas_Clientes_ClienteId",
                table: "Vendas",
                column: "ClienteId",
                principalTable: "Clientes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vendas_Clientes_ClienteId",
                table: "Vendas");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "ConfiguracoesLoja");

            migrationBuilder.DropTable(
                name: "TamanhosProduto");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_ClienteId",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_Referencia",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ClienteId",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Parcelas",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "TabelaPreco",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "PrecoAtacadoCentavos",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Referencia",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Tamanho",
                table: "Movimentacoes");

            migrationBuilder.DropColumn(
                name: "Tamanho",
                table: "ItensVenda");
        }
    }
}
