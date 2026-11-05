using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ClassificacaoECategoriasFinanceiras : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Classificacao",
                table: "Lancamentos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CategoriasFinanceiras",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Ativa = table.Column<bool>(type: "INTEGER", nullable: false),
                    CriadaEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoriasFinanceiras", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_Guid",
                table: "CategoriasFinanceiras",
                column: "Guid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CategoriasFinanceiras_Tipo_Nome",
                table: "CategoriasFinanceiras",
                columns: new[] { "Tipo", "Nome" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CategoriasFinanceiras");

            migrationBuilder.DropColumn(
                name: "Classificacao",
                table: "Lancamentos");
        }
    }
}
