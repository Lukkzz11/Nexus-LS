using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class ComissaoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static Venda V(string? operador, long total, long acrescimo = 0, bool cancelada = false, DateTime? quando = null) =>
        new() { Operador = operador, TotalCentavos = total, AcrescimoCentavos = acrescimo, Cancelada = cancelada, DataHora = quando ?? new DateTime(2026, 11, 10, 12, 0, 0) };

    [Fact]
    public void Comissao_e_percentual_da_venda_liquida_sem_o_acrescimo_do_cartao()
    {
        var cfg = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 5m };
        // R$ 105,00 com R$ 5,00 de acréscimo do cartão: a base é R$ 100,00 -> 5% = R$ 5,00
        var l = ComissaoCalculo.Calcular(new[] { V("Ana", 10500, acrescimo: 500) }, Array.Empty<(string?, long)>(), cfg).Single();
        Assert.Equal(10000, l.BaseCentavos);
        Assert.Equal(500, l.ComissaoCentavos);
    }

    [Fact]
    public void Percentual_proprio_vale_mais_que_o_padrao_e_ignora_maiusculas()
    {
        var cfg = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 2m };
        cfg.ComissaoPorVendedor["ana"] = 10m;
        var linhas = ComissaoCalculo.Calcular(new[] { V("Ana", 10000), V("Caio", 10000) }, Array.Empty<(string?, long)>(), cfg);
        Assert.Equal(1000, linhas.Single(l => l.Vendedor == "Ana").ComissaoCentavos);
        Assert.Equal(200, linhas.Single(l => l.Vendedor == "Caio").ComissaoCentavos);
    }

    [Fact]
    public void Venda_cancelada_nao_entra_e_devolucao_abate_a_base()
    {
        var cfg = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 10m };
        var linhas = ComissaoCalculo.Calcular(
            new[] { V("Ana", 20000), V("Ana", 99999, cancelada: true) },
            new[] { ((string?)"Ana", 5000L) }, cfg);
        var ana = linhas.Single();
        Assert.Equal(1, ana.Vendas);
        Assert.Equal(15000, ana.BaseCentavos);
        Assert.Equal(1500, ana.ComissaoCentavos);
    }

    [Fact]
    public void Desligar_o_desconto_de_devolucoes_ignora_as_devolucoes()
    {
        var cfg = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 10m, ComissaoDescontaDevolucoes = false };
        var ana = ComissaoCalculo.Calcular(new[] { V("Ana", 20000) }, new[] { ((string?)"Ana", 5000L) }, cfg).Single();
        Assert.Equal(20000, ana.BaseCentavos);
    }

    [Fact]
    public void Devolucao_maior_que_as_vendas_do_mes_nao_deixa_comissao_negativa()
    {
        var cfg = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 10m };
        var linhas = ComissaoCalculo.Calcular(new[] { V("Ana", 1000) }, new[] { ((string?)"Ana", 5000L), ((string?)"Caio", 2000L) }, cfg);
        Assert.All(linhas, l => Assert.Equal(0, l.ComissaoCentavos));
        Assert.Contains(linhas, l => l.Vendedor == "Caio");   // só devolução: aparece com zero
    }

    [Fact]
    public void Venda_sem_operador_vira_sem_vendedor()
    {
        var l = ComissaoCalculo.Calcular(new[] { V(null, 1000) }, Array.Empty<(string?, long)>(), new()).Single();
        Assert.Equal(ComissaoCalculo.SemVendedor, l.Vendedor);
    }

    [Fact]
    public async Task Servico_le_so_o_periodo_pedido_e_devolucao_vai_para_o_vendedor_da_venda_original()
    {
        int vendaAna;
        using (var db = _banco.CriarContexto())
        {
            var dentro = V("Ana", 10000); var fora = V("Ana", 99999, quando: new DateTime(2026, 10, 31, 12, 0, 0));
            var caio = V("Caio", 20000);
            db.Vendas.AddRange(dentro, fora, caio);
            await db.SaveChangesAsync();
            vendaAna = dentro.Id;
            db.Devolucoes.Add(new Devolucao { VendaId = dentro.Id, DataHora = new DateTime(2026, 11, 12, 10, 0, 0), ValorCentavos = 3000, Motivo = "defeito", Operador = "Caio" });
            await db.SaveChangesAsync();
        }

        var cfg = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 10m };
        var linhas = await new ComissaoService(_banco.Fabrica).CalcularAsync(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30), cfg);

        var ana = linhas.Single(l => l.Vendedor == "Ana");
        Assert.Equal(10000, ana.VendidoCentavos);          // a venda de outubro ficou de fora
        Assert.Equal(3000, ana.DevolvidoCentavos);          // a devolução abate quem vendeu, não quem recebeu
        Assert.Equal(700, ana.ComissaoCentavos);
        Assert.Equal(2000, linhas.Single(l => l.Vendedor == "Caio").ComissaoCentavos);
        Assert.True(vendaAna > 0);
    }

    [Fact]
    public void Percentuais_de_comissao_persistem_e_valores_ruins_sao_recusados()
    {
        var arq = Path.Combine(Path.GetTempPath(), "nexus-com-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoSegurancaStore(arq);
            var c = new ConfiguracaoSeguranca { ComissaoPadraoPercent = 3m, ComissaoDescontaDevolucoes = false };
            c.ComissaoPorVendedor["Ana"] = 7.5m;
            store.Salvar(c);
            var lida = new ConfiguracaoSegurancaStore(arq).Carregar();
            Assert.Equal(3m, lida.ComissaoPadraoPercent);
            Assert.False(lida.ComissaoDescontaDevolucoes);
            Assert.Equal(7.5m, lida.ComissaoPorVendedor["ana"]);   // sem diferenciar maiúsculas

            var ruim = new ConfiguracaoSeguranca();
            ruim.ComissaoPorVendedor["Caio"] = 120m;
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(ruim));
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { ComissaoPadraoPercent = -1m }));
        }
        finally { File.Delete(arq); }
    }
}
