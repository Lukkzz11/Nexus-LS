using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProdutoCategoriaCodigoInterno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Categoria",
                table: "Produtos",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CodigoInterno",
                table: "Produtos",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_CodigoInterno",
                table: "Produtos",
                column: "CodigoInterno",
                unique: true,
                filter: "\"CodigoInterno\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Produtos_CodigoInterno",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Categoria",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "CodigoInterno",
                table: "Produtos");
        }
    }
}
