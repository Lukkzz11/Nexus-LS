using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class LancamentosEmpresaEAnexos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LancamentosEmpresa",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    Data = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tipo = table.Column<int>(type: "INTEGER", nullable: false),
                    Categoria = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Descricao = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ValorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Forma = table.Column<int>(type: "INTEGER", nullable: true),
                    Observacao = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AlteradoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LancamentosEmpresa", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnexosLancamento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LancamentoEmpresaId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomeOriginal = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CaminhoRelativo = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    TipoMime = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TamanhoBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnexosLancamento", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnexosLancamento_LancamentosEmpresa_LancamentoEmpresaId",
                        column: x => x.LancamentoEmpresaId,
                        principalTable: "LancamentosEmpresa",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnexosLancamento_LancamentoEmpresaId",
                table: "AnexosLancamento",
                column: "LancamentoEmpresaId");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosEmpresa_Data",
                table: "LancamentosEmpresa",
                column: "Data");

            migrationBuilder.CreateIndex(
                name: "IX_LancamentosEmpresa_Guid",
                table: "LancamentosEmpresa",
                column: "Guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnexosLancamento");

            migrationBuilder.DropTable(
                name: "LancamentosEmpresa");
        }
    }
}
