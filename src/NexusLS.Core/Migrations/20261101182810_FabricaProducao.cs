using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class FabricaProducao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MateriasPrimas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Unidade = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EstoqueAtual = table.Column<decimal>(type: "TEXT", nullable: false),
                    EstoqueMinimo = table.Column<decimal>(type: "TEXT", nullable: false),
                    CustoUnitarioCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Fornecedor = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Localizacao = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MateriasPrimas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrdensProducao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Modelo = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Referencia = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PrevisaoTermino = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TecidoUtilizado = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Responsavel = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CustoEstimadoPecaCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Observacoes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TransferidaEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProdutoDestinoId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrdensProducao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PerdasProducao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OrdemProducaoId = table.Column<int>(type: "INTEGER", nullable: true),
                    OrdemCodigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Motivo = table.Column<int>(type: "INTEGER", nullable: false),
                    MateriaPrimaId = table.Column<int>(type: "INTEGER", nullable: true),
                    MaterialNome = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", nullable: false),
                    Unidade = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CustoEstimadoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    BaixouEstoque = table.Column<bool>(type: "INTEGER", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    RegistradoPor = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PerdasProducao", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GradesOrdemProducao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrdemProducaoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tamanho = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GradesOrdemProducao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GradesOrdemProducao_OrdensProducao_OrdemProducaoId",
                        column: x => x.OrdemProducaoId,
                        principalTable: "OrdensProducao",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GradesOrdemProducao_OrdemProducaoId",
                table: "GradesOrdemProducao",
                column: "OrdemProducaoId");

            migrationBuilder.CreateIndex(
                name: "IX_MateriasPrimas_Nome",
                table: "MateriasPrimas",
                column: "Nome");

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_Codigo",
                table: "OrdensProducao",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PerdasProducao_DataHora",
                table: "PerdasProducao",
                column: "DataHora");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GradesOrdemProducao");

            migrationBuilder.DropTable(
                name: "MateriasPrimas");

            migrationBuilder.DropTable(
                name: "PerdasProducao");

            migrationBuilder.DropTable(
                name: "OrdensProducao");
        }
    }
}
