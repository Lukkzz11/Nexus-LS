using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class SincronizacaoPreparacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "Produtos",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "Produtos",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Produtos que já existem ganham um identificador global PRÓPRIO cada um (UUID versão 4) e a data de
            // alteração de agora. Escrito em MAIÚSCULAS, que é como o EF grava e compara Guid no SQLite.
            // Isto roda ANTES do índice único, que é criado mais abaixo. Nenhum dado existente é alterado ou apagado.
            migrationBuilder.Sql(@"UPDATE Produtos SET Guid = upper(
                hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' ||
                substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)))");
            migrationBuilder.Sql("UPDATE Produtos SET AtualizadoEm = strftime('%Y-%m-%d %H:%M:%f', 'now')");

            migrationBuilder.CreateTable(
                name: "SincronizacaoLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IniciadaEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConcluidaEm = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Criados = table.Column<int>(type: "INTEGER", nullable: false),
                    Alterados = table.Column<int>(type: "INTEGER", nullable: false),
                    Desativados = table.Column<int>(type: "INTEGER", nullable: false),
                    Inalterados = table.Column<int>(type: "INTEGER", nullable: false),
                    Ignorados = table.Column<int>(type: "INTEGER", nullable: false),
                    Erros = table.Column<int>(type: "INTEGER", nullable: false),
                    Resultado = table.Column<int>(type: "INTEGER", nullable: false),
                    Mensagem = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SincronizacaoLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SincronizacaoProdutos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProdutoGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    HashEnviado = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AtivoNoRemoto = table.Column<bool>(type: "INTEGER", nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SincronizacaoProdutos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_Guid",
                table: "Produtos",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SincronizacaoLogs_IniciadaEm",
                table: "SincronizacaoLogs",
                column: "IniciadaEm");

            migrationBuilder.CreateIndex(
                name: "IX_SincronizacaoProdutos_ProdutoGuid",
                table: "SincronizacaoProdutos",
                column: "ProdutoGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SincronizacaoLogs");

            migrationBuilder.DropTable(
                name: "SincronizacaoProdutos");

            migrationBuilder.DropIndex(
                name: "IX_Produtos_Guid",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "Produtos");
        }
    }
}
