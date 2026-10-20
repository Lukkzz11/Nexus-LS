using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class Funcionarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Funcionarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Cargo = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Telefone = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Perfil = table.Column<int>(type: "INTEGER", nullable: false),
                    PermissoesPersonalizadas = table.Column<int>(type: "INTEGER", nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RecuperacaoHash = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Ativo = table.Column<bool>(type: "INTEGER", nullable: false),
                    DataCadastro = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funcionarios", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_Nome",
                table: "Funcionarios",
                column: "Nome");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Funcionarios");
        }
    }
}
