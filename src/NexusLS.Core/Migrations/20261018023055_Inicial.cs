using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Produtos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CodigoBarras = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PrecoVendaCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    PrecoCustoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    EstoqueAtual = table.Column<int>(type: "INTEGER", nullable: false),
                    EstoqueMinimo = table.Column<int>(type: "INTEGER", nullable: false),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produtos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vendas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SubtotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    DescontoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    FormaPagamento = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorRecebidoCentavos = table.Column<long>(type: "INTEGER", nullable: true),
                    TrocoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Cancelada = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataCancelamento = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Movimentacoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movimentacoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movimentacoes_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItensVenda",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomeProduto = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PrecoUnitarioCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalCentavos = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensVenda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensVenda_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_Movimentacoes_ProdutoId",
                table: "Movimentacoes",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CodigoBarras",
                table: "Produtos",
                column: "CodigoBarras",
                unique: true,
                filter: "\"CodigoBarras\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_DataHora",
                table: "Vendas",
                column: "DataHora");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ItensVenda");

            migrationBuilder.DropTable(
                name: "Movimentacoes");

            migrationBuilder.DropTable(
                name: "Vendas");

            migrationBuilder.DropTable(
                name: "Produtos");
        }
    }
}
