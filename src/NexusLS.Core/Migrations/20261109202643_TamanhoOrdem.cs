using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusLS.Core.Migrations
{
    /// <inheritdoc />
    public partial class TamanhoOrdem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Ordem",
                table: "TamanhosProduto",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Tamanhos que já existem ganham o número 1, 2, 3... na ordem em que foram cadastrados em cada produto.
            migrationBuilder.Sql(
                "UPDATE TamanhosProduto SET Ordem = (SELECT COUNT(*) FROM TamanhosProduto t2 " +
                "WHERE t2.ProdutoId = TamanhosProduto.ProdutoId AND t2.Tamanho <> 'Sem tamanho' AND t2.Id <= TamanhosProduto.Id) " +
                "WHERE Tamanho <> 'Sem tamanho';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ordem",
                table: "TamanhosProduto");
        }
    }
}
