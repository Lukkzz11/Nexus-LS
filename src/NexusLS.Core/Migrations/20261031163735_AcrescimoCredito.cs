using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class AcrescimoCredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AcrescimoCentavos",
                table: "Vendas",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcrescimoCentavos",
                table: "Vendas");
        }
    }
}
