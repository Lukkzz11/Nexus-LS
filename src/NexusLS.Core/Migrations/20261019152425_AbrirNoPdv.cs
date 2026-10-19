using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class AbrirNoPdv : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AbrirNoPdv",
                table: "ConfiguracoesLoja",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbrirNoPdv",
                table: "ConfiguracoesLoja");
        }
    }
}
