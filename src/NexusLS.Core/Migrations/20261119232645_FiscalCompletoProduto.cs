using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class FiscalCompletoProduto : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AliquotaCofinsBp",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AliquotaIcmsBp",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AliquotaPisBp",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Cest",
                table: "Produtos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassTribIbsCbs",
                table: "Produtos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstCofins",
                table: "Produtos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstIbsCbs",
                table: "Produtos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstIcms",
                table: "Produtos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CstPis",
                table: "Produtos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrigemMercadoria",
                table: "Produtos",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UnidadeComercial",
                table: "Produtos",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AliquotaCofinsBp",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "AliquotaIcmsBp",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "AliquotaPisBp",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "Cest",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "ClassTribIbsCbs",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "CstCofins",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "CstIbsCbs",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "CstIcms",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "CstPis",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "OrigemMercadoria",
                table: "Produtos");

            migrationBuilder.DropColumn(
                name: "UnidadeComercial",
                table: "Produtos");
        }
    }
}
