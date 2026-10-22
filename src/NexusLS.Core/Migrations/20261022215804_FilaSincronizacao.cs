using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class FilaSincronizacao : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FilaSincronizacao",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProdutoGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    Versao = table.Column<int>(type: "INTEGER", nullable: false),
                    Estado = table.Column<int>(type: "INTEGER", nullable: false),
                    Tentativas = table.Column<int>(type: "INTEGER", nullable: false),
                    ProximaTentativa = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UltimoErro = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UsuarioUid = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EnviadoEm = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FilaSincronizacao", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FilaSincronizacao_Estado_ProximaTentativa",
                table: "FilaSincronizacao",
                columns: new[] { "Estado", "ProximaTentativa" });

            migrationBuilder.CreateIndex(
                name: "IX_FilaSincronizacao_ProdutoGuid",
                table: "FilaSincronizacao",
                column: "ProdutoGuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FilaSincronizacao");
        }
    }
}
