using System.IO;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class PacoteContadorTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus_contador_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private void Semear()
    {
        using var db = _banco.Fabrica.CreateDbContext();
        db.Vendas.Add(new Venda { DataHora = new DateTime(2026, 10, 15, 10, 0, 0), TotalCentavos = 10000, SubtotalCentavos = 10000, FormaPagamento = FormaPagamento.Dinheiro,
            Itens = { new ItemVenda { NomeProduto = "Camiseta", Quantidade = 1, PrecoUnitarioCentavos = 10000, TotalCentavos = 10000, Tamanho = "M" } } });
        db.Vendas.Add(new Venda { DataHora = new DateTime(2026, 11, 2, 10, 0, 0), TotalCentavos = 5000, SubtotalCentavos = 5000, FormaPagamento = FormaPagamento.Dinheiro,
            Itens = { new ItemVenda { NomeProduto = "Boné", Quantidade = 1, PrecoUnitarioCentavos = 5000, TotalCentavos = 5000, Tamanho = "U" } } });
        db.Lancamentos.Add(new Lancamento { Tipo = TipoLancamento.Despesa, Descricao = "Aluguel outubro", ValorCentavos = 80000, Vencimento = new DateTime(2026, 10, 5) });
        db.Lancamentos.Add(new Lancamento { Tipo = TipoLancamento.Despesa, Descricao = "Aluguel novembro", ValorCentavos = 80000, Vencimento = new DateTime(2026, 11, 5) });
        db.SaveChanges();
    }

    [Fact]
    public async Task Gera_tres_arquivos_so_com_o_mes_pedido()
    {
        Semear();
        var arquivos = await new PacoteContador(_banco.Fabrica).GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), _pasta);

        Assert.Equal(3, arquivos.Count);
        Assert.Equal("vendas-2026-10.csv", Path.GetFileName(arquivos[0]));
        var vendas = File.ReadAllText(arquivos[0]);
        Assert.Contains("100,00", vendas);
        Assert.DoesNotContain("50,00;", vendas.Replace("100,00", ""));   // a venda de novembro não entra
        Assert.Contains("Camiseta", File.ReadAllText(arquivos[1]));
        Assert.DoesNotContain("Boné", File.ReadAllText(arquivos[1]));
        var contas = File.ReadAllText(arquivos[2]);
        Assert.Contains("Aluguel outubro", contas);
        Assert.DoesNotContain("Aluguel novembro", contas);
    }

    [Fact]
    public async Task O_ultimo_dia_do_periodo_entra_inteiro()
    {
        using (var db = _banco.Fabrica.CreateDbContext())
        {
            db.Vendas.Add(new Venda { DataHora = new DateTime(2026, 10, 31, 23, 59, 0), TotalCentavos = 777, SubtotalCentavos = 777, FormaPagamento = FormaPagamento.Dinheiro });
            db.SaveChanges();
        }
        var arquivos = await new PacoteContador(_banco.Fabrica).GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), _pasta);
        Assert.Contains("7,77", File.ReadAllText(arquivos[0]));
    }

    [Fact]
    public async Task Sem_pasta_ou_com_periodo_invalido_da_erro_claro()
    {
        var s = new PacoteContador(_banco.Fabrica);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31), " "));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.GerarAsync(new DateTime(2026, 10, 31), new DateTime(2026, 10, 1), _pasta));
    }
}
