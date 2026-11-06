using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ClientesCompletosECredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreditoCentavos",
                table: "Clientes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "Clientes",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RazaoSocial",
                table: "Clientes",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AjustesCreditoCliente",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClienteId = table.Column<int>(type: "INTEGER", nullable: false),
                    SaldoAnteriorCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    SaldoNovoCentavos = table.Column<long>(type: "INTEGER", nullable: false),
                    Motivo = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Operador = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    CriadoEm = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Guid = table.Column<Guid>(type: "TEXT", nullable: false),
                    AtualizadoEm = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AjustesCreditoCliente", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AjustesCreditoCliente_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AjustesCreditoCliente_ClienteId",
                table: "AjustesCreditoCliente",
                column: "ClienteId");

            migrationBuilder.CreateIndex(
                name: "IX_AjustesCreditoCliente_Guid",
                table: "AjustesCreditoCliente",
                column: "Guid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AjustesCreditoCliente");

            migrationBuilder.DropColumn(
                name: "CreditoCentavos",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "Clientes");

            migrationBuilder.DropColumn(
                name: "RazaoSocial",
                table: "Clientes");
        }
    }
}
