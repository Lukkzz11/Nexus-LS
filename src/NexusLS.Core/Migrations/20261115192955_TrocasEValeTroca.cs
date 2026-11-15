using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class TrocasEValeTroca : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ValeTrocaCentavos",
                table: "Vendas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ValeTrocaId",
                table: "Vendas",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Devolucoes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClienteId = table.Column<int>(type: "INTEGER", nullable: true),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Cancelada = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataCancelamento = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CanceladoPor = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Devolucoes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Devolucoes_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ValesTroca",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Codigo = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    DevolucaoId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClienteId = table.Column<int>(type: "INTEGER", nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Cancelado = table.Column<bool>(type: "INTEGER", nullable: false),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValesTroca", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItensDevolucao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DevolucaoId = table.Column<int>(type: "INTEGER", nullable: false),
                    ItemVendaId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomeProduto = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Tamanho = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Quantidade = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    CustoUnitarioCentavos = table.Column<long>(type: "INTEGER", nullable: true),
                    DevolveuEstoque = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensDevolucao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensDevolucao_Devolucoes_DevolucaoId",
                        column: x => x.DevolucaoId,
                        principalTable: "Devolucoes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ValeTrocaId",
                table: "Vendas",
                column: "ValeTrocaId");

            migrationBuilder.CreateIndex(
                name: "IX_Devolucoes_DataHora",
                table: "Devolucoes",
                column: "DataHora");

            migrationBuilder.CreateIndex(
                name: "IX_Devolucoes_Guid",
                table: "Devolucoes",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devolucoes_VendaId",
                table: "Devolucoes",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensDevolucao_DevolucaoId",
                table: "ItensDevolucao",
                column: "DevolucaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensDevolucao_ItemVendaId",
                table: "ItensDevolucao",
                column: "ItemVendaId");

            migrationBuilder.CreateIndex(
                name: "IX_ValesTroca_Codigo",
                table: "ValesTroca",
                column: "Codigo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValesTroca_DevolucaoId",
                table: "ValesTroca",
                column: "DevolucaoId");

            migrationBuilder.CreateIndex(
                name: "IX_ValesTroca_Guid",
                table: "ValesTroca",
                column: "Guid",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Vendas_ValesTroca_ValeTrocaId",
                table: "Vendas",
                column: "ValeTrocaId",
                principalTable: "ValesTroca",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Vendas_ValesTroca_ValeTrocaId",
                table: "Vendas");

            migrationBuilder.DropTable(
                name: "ItensDevolucao");

            migrationBuilder.DropTable(
                name: "ValesTroca");

            migrationBuilder.DropTable(
                name: "Devolucoes");

            migrationBuilder.DropIndex(
                name: "IX_Vendas_ValeTrocaId",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ValeTrocaCentavos",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "ValeTrocaId",
                table: "Vendas");
        }
    }
}
