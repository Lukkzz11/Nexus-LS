using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ContasFinanceiras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContaId",
                table: "Lancamentos",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChequesRecebidos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Numero = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Banco = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Emitente = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    BomPara = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Situacao = table.Column<int>(type: "INTEGER", nullable: false),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: true),
                    PagamentoClienteId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompensadoEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ContaId = table.Column<int>(type: "INTEGER", nullable: true),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    MotivoDevolucao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChequesRecebidos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContasFinanceiras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    SaldoInicialCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    DataSaldoInicial = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Padrao = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContasFinanceiras", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LiquidacoesCartao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendaId = table.Column<int>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContaId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaxaCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Cancelada = table.Column<bool>(type: "INTEGER", nullable: false),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidacoesCartao", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LiquidacoesCartao_ContasFinanceiras_ContaId",
                        column: x => x.ContaId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LiquidacoesCartao_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Transferencias",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ContaOrigemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ContaDestinoId = table.Column<int>(type: "INTEGER", nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadaEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Cancelada = table.Column<bool>(type: "INTEGER", nullable: false),
                    MotivoCancelamento = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CanceladaEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transferencias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transferencias_ContasFinanceiras_ContaDestinoId",
                        column: x => x.ContaDestinoId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transferencias_ContasFinanceiras_ContaOrigemId",
                        column: x => x.ContaOrigemId,
                        principalTable: "ContasFinanceiras",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChequesRecebidos_BomPara",
                table: "ChequesRecebidos",
                column: "BomPara");

            migrationBuilder.CreateIndex(
                name: "IX_ChequesRecebidos_VendaId",
                table: "ChequesRecebidos",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacoesCartao_ContaId",
                table: "LiquidacoesCartao",
                column: "ContaId");

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacoesCartao_VendaId",
                table: "LiquidacoesCartao",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_ContaDestinoId",
                table: "Transferencias",
                column: "ContaDestinoId");

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_ContaOrigemId",
                table: "Transferencias",
                column: "ContaOrigemId");

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_Data",
                table: "Transferencias",
                column: "Data");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChequesRecebidos");

            migrationBuilder.DropTable(
                name: "LiquidacoesCartao");

            migrationBuilder.DropTable(
                name: "Transferencias");

            migrationBuilder.DropTable(
                name: "ContasFinanceiras");

            migrationBuilder.DropColumn(
                name: "ContaId",
                table: "Lancamentos");
        }
    }
}
