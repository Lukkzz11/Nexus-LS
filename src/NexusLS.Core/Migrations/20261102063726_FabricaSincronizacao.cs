using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class FabricaSincronizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AtualizadoEm",
                table: "PerdasProducao",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "PerdasProducao",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "OrdensProducao",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "Guid",
                table: "MateriasPrimas",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Registros que já existiam ganham um Guid próprio (antes do índice único) e entram na fila como "alterados agora".
            foreach (var tabela in new[] { "PerdasProducao", "OrdensProducao", "MateriasPrimas" })
            {
                migrationBuilder.Sql($@"UPDATE {tabela} SET Guid = upper(
                    hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-4' || substr(hex(randomblob(2)), 2) || '-' ||
                    substr('89AB', abs(random()) % 4 + 1, 1) || substr(hex(randomblob(2)), 2) || '-' || hex(randomblob(6)))");
            }
            migrationBuilder.Sql("UPDATE PerdasProducao SET AtualizadoEm = strftime('%Y-%m-%d %H:%M:%f', 'now')");

            migrationBuilder.CreateIndex(
                name: "IX_PerdasProducao_Guid",
                table: "PerdasProducao",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrdensProducao_Guid",
                table: "OrdensProducao",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MateriasPrimas_Guid",
                table: "MateriasPrimas",
                column: "Guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PerdasProducao_Guid",
                table: "PerdasProducao");

            migrationBuilder.DropIndex(
                name: "IX_OrdensProducao_Guid",
                table: "OrdensProducao");

            migrationBuilder.DropIndex(
                name: "IX_MateriasPrimas_Guid",
                table: "MateriasPrimas");

            migrationBuilder.DropColumn(
                name: "AtualizadoEm",
                table: "PerdasProducao");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "PerdasProducao");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "OrdensProducao");

            migrationBuilder.DropColumn(
                name: "Guid",
                table: "MateriasPrimas");
        }
    }
}
