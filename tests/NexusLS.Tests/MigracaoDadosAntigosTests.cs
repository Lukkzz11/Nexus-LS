using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexusLS.Core.Models;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>
/// Simula o banco de quem já usava a versão anterior: cria o banco só até a migration "Inicial",
/// grava dados com SQL puro e depois aplica as migrations novas. Nada pode se perder.
/// </summary>
public class MigracaoDadosAntigosTests
{
    [Fact]
    public void Estoque_vendas_e_itens_antigos_sobrevivem_a_migracao()
    {
        using var banco = new BancoDeTeste(aplicarMigracoes: false);

        using (var db = banco.CriarContexto())
        {
            db.GetService<IMigrator>().Migrate("Inicial");
            db.Database.ExecuteSqlRaw(
                "INSERT INTO Produtos (CodigoBarras, Nome, PrecoVendaCentavos, PrecoCustoCentavos, EstoqueAtual, EstoqueMinimo, Ativo) " +
                "VALUES ('123', 'Café', 1000, 600, 7, 2, 1), (NULL, 'Pão', 250, 100, 0, 5, 1)");
            db.Database.ExecuteSqlRaw(
                "INSERT INTO Vendas (DataHora, SubtotalCentavos, DescontoCentavos, TotalCentavos, FormaPagamento, TrocoCentavos, Cancelada) " +
                "VALUES ('2026-10-17 10:00:00', 1000, 0, 1000, 3, 0, 0)");
            db.Database.ExecuteSqlRaw(
                "INSERT INTO ItensVenda (VendaId, ProdutoId, NomeProduto, PrecoUnitarioCentavos, Quantidade, TotalCentavos) " +
                "VALUES (1, 1, 'Café', 1000, 1, 1000)");
        }

        using (var db = banco.CriarContexto())
            db.Database.Migrate(); // aplica as migrations novas sobre os dados antigos

        using var leitura = banco.CriarContexto();

        var cafe = leitura.Produtos.Include(p => p.Tamanhos).Single(p => p.Nome == "Café");
        var linha = Assert.Single(cafe.Tamanhos);
        Assert.Equal(Tamanhos.SemTamanho, linha.Tamanho);
        Assert.Equal(7, linha.Estoque);          // o estoque antigo foi para "Sem tamanho"
        Assert.Equal(7, cafe.EstoqueAtual);
        Assert.Equal(0, cafe.PrecoAtacadoCentavos);

        var pao = leitura.Produtos.Include(p => p.Tamanhos).Single(p => p.Nome == "Pão");
        Assert.Equal(0, Assert.Single(pao.Tamanhos).Estoque); // produto sem estoque também ganha a linha

        var venda = leitura.Vendas.Include(v => v.Itens).Single();
        Assert.Equal(TabelaPreco.Varejo, venda.TabelaPreco);
        Assert.Equal(1, venda.Parcelas);
        Assert.Equal(FormaPagamento.CartaoDebito, venda.FormaPagamento); // o antigo "Cartão" virou débito
        Assert.Equal(Tamanhos.SemTamanho, Assert.Single(venda.Itens).Tamanho);
    }
}
