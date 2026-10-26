using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class EntidadesSincronizaveis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FilaSincronizacao_ProdutoGuid",
                table: "FilaSincronizacao");

            migrationBuilder.RenameColumn(
                name: "ProdutoGuid",
                table: "FilaSincronizacao",
                newName: "EntidadeGuid");

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Vendas",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Vendas",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "TurnosCaixa",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "TurnosCaixa",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Transferencias",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Transferencias",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Pedidos",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "PagamentosCliente",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "PagamentosCliente",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "MovimentosCaixa",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "MovimentosCaixa",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Movimentacoes",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Movimentacoes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "LiquidacoesCartao",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "LiquidacoesCartao",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Lancamentos",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Lancamentos",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Fornecedores",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Fornecedores",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Entidade",
                table: "FilaSincronizacao",
                type: "TEXT",
                maxLength: 40,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "FechamentosCaixa",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "FechamentosCaixa",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "ContasFinanceiras",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "ContasFinanceiras",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Compras",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Compras",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Clientes",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Clientes",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "ChequesRecebidos",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "ChequesRecebidos",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // ---- Dados que já existem: cada linha ganha um Guid próprio (único) e o carimbo de alteração ----
            // O Guid é gerado aqui em SQL, em maiúsculas com hífens, igual ao formato que o EF grava para Guid no SQLite.
            migrationBuilder.Sql(@"UPDATE ""Vendas"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Clientes"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""PagamentosCliente"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""TurnosCaixa"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""MovimentosCaixa"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""FechamentosCaixa"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Lancamentos"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Fornecedores"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Compras"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Movimentacoes"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""ContasFinanceiras"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Transferencias"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""LiquidacoesCartao"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""ChequesRecebidos"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6))), ""AtualizadoEm"" = strftime('%Y-%m-%d %H:%M:%f', 'now');");
            migrationBuilder.Sql(@"UPDATE ""Pedidos"" SET ""Guid"" = upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)),2) || '-' || substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)),2) || '-' || hex(randomblob(6)));");
            // Venda que já tinha chave de idempotência usa a mesma como id global (a venda repetida cai no mesmo documento).
            migrationBuilder.Sql(@"UPDATE ""Vendas"" SET ""Guid"" = ""ChaveUnica"" WHERE ""ChaveUnica"" IS NOT NULL;");
            // Fila antiga: só tinha produtos (a coluna nova "Entidade" já nasce com o valor padrão "produto").
            migrationBuilder.Sql(@"UPDATE ""FilaSincronizacao"" SET ""Entidade"" = 'produto';");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_Guid",
                table: "Vendas",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TurnosCaixa_Guid",
                table: "TurnosCaixa",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transferencias_Guid",
                table: "Transferencias",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pedidos_Guid",
                table: "Pedidos",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosCliente_Guid",
                table: "PagamentosCliente",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovimentosCaixa_Guid",
                table: "MovimentosCaixa",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Movimentacoes_Guid",
                table: "Movimentacoes",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LiquidacoesCartao_Guid",
                table: "LiquidacoesCartao",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Lancamentos_Guid",
                table: "Lancamentos",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Fornecedores_Guid",
                table: "Fornecedores",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FilaSincronizacao_Entidade_EntidadeGuid",
                table: "FilaSincronizacao",
                columns: new[] { "Entidade", "EntidadeGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FechamentosCaixa_Guid",
                table: "FechamentosCaixa",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContasFinanceiras_Guid",
                table: "ContasFinanceiras",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Compras_Guid",
                table: "Compras",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_Guid",
                table: "Clientes",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChequesRecebidos_Guid",
                table: "ChequesRecebidos",
                column: "Guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vendas_Guid",
                table: "Vendas");

            migrationBuilder.DropIndex(
                name: "IX_TurnosCaixa_Guid",
                table: "TurnosCaixa");

            migrationBuilder.DropIndex(
                name: "IX_Transferencias_Guid",
                table: "Transferencias");

            migrationBuilder.DropIndex(
                name: "IX_Pedidos_Guid",
                table: "Pedidos");

            migrationBuilder.DropIndex(
                name: "IX_PagamentosCliente_Guid",
                table: "PagamentosCliente");

            migrationBuilder.DropIndex(
                name: "IX_MovimentosCaixa_Guid",
                table: "MovimentosCaixa");

            migrationBuilder.DropIndex(
                name: "IX_Movimentacoes_Guid",
                table: "Movimentacoes");

            migrationBuilder.DropIndex(
                name: "IX_LiquidacoesCartao_Guid",
                table: "LiquidacoesCartao");

            migrationBuilder.DropIndex(
                name: "IX_Lancamentos_Guid",
                table: "Lancamentos");

            migrationBuilder.DropIndex(
                name: "IX_Fornecedores_Guid",
                table: "Fornecedores");

            migrationBuilder.DropIndex(
                name: "IX_FilaSincronizacao_Entidade_EntidadeGuid",
                table: "FilaSincronizacao");

            migrationBuilder.DropIndex(
                name: "IX_FechamentosCaixa_Guid",
                table: "FechamentosCaixa");

            migrationBuilder.DropIndex(
                name: "IX_ContasFinanceiras_Guid",
                table: "ContasFinanceiras");

            migrationBuilder.DropIndex(
                name: "IX_Compras_Guid",
                table: "Compras");

            migrationBuilder.DropIndex(
                name: "IX_Clientes_Guid",
                table: "Clientes");

            migrationBuilder.DropIndex(
                name: "IX_ChequesRecebidos_Guid",
                table: "ChequesRecebidos");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Vendas");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "TurnosCaixa");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "TurnosCaixa");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Transferencias");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Pedidos");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "PagamentosCliente");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "PagamentosCliente");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "MovimentosCaixa");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "MovimentosCaixa");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Movimentacoes");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Movimentacoes");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "LiquidacoesCartao");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "LiquidacoesCartao");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Lancamentos");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Lancamentos");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Fornecedores");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Fornecedores");

            migrationBuilder.DropColumn(
                name: "Entidade",
                table: "FilaSincronizacao");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "FechamentosCaixa");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "FechamentosCaixa");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "ContasFinanceiras");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "ContasFinanceiras");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Compras");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Compras");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "ChequesRecebidos");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "ChequesRecebidos");

            migrationBuilder.RenameColumn(
                name: "EntidadeGuid",
                table: "FilaSincronizacao",
                newName: "ProdutoGuid");

            migrationBuilder.CreateIndex(
                name: "IX_FilaSincronizacao_ProdutoGuid",
                table: "FilaSincronizacao",
                column: "ProdutoGuid",
                unique: true);
        }
    }
}
