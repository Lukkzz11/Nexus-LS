using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class SegurancaAcessoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-seg-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(0, 600, false)]    // desligado: nunca bloqueia
    [InlineData(10, 9, false)]
    [InlineData(10, 10, true)]
    [InlineData(10, 45, true)]
    public void Ocioso_depende_dos_minutos_parado(int minutos, int parado, bool esperado)
    {
        var agora = new DateTime(2026, 11, 17, 12, 0, 0);
        Assert.Equal(esperado, RegrasSeguranca.Ocioso(agora.AddMinutes(-parado), agora, minutos));
    }

    [Fact]
    public void Configuracao_vem_desligada_e_persiste()
    {
        var arq = Path.Combine(_pasta, "config.json");
        var store = new ConfiguracaoSegurancaStore(arq);
        var padrao = store.Carregar();
        Assert.Equal(0, padrao.MinutosInatividade);
        Assert.False(padrao.EsconderGestaoSemAcesso);

        store.Salvar(new ConfiguracaoSeguranca { MinutosInatividade = 15, EsconderGestaoSemAcesso = true });
        var lida = new ConfiguracaoSegurancaStore(arq).Carregar();
        Assert.Equal(15, lida.MinutosInatividade);
        Assert.True(lida.EsconderGestaoSemAcesso);
    }

    [Fact]
    public void Minutos_fora_do_limite_sao_recusados()
    {
        var store = new ConfiguracaoSegurancaStore(Path.Combine(_pasta, "c.json"));
        Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { MinutosInatividade = -1 }));
        Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { MinutosInatividade = 241 }));
    }

    [Fact]
    public void Arquivo_estragado_volta_ao_padrao()
    {
        Directory.CreateDirectory(_pasta);
        var arq = Path.Combine(_pasta, "ruim.json");
        File.WriteAllText(arq, "{ nao e json");
        Assert.Equal(0, new ConfiguracaoSegurancaStore(arq).Carregar().MinutosInatividade);
    }

    [Fact]
    public async Task Entrada_e_pin_errado_ficam_registrados_nas_atividades()
    {
        var servico = new FuncionarioService(_banco.Fabrica, () => new DateTime(2026, 11, 17, 9, 0, 0),
            new TentativasLoginStore(Path.Combine(_pasta, "t.json")));
        var (adm, _) = await servico.CriarPrimeiroAdministradorAsync("Ana", "1234");

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.AutenticarAsync(adm.Id, "9999"));
        await servico.AutenticarAsync(adm.Id, "1234");

        await using var db = _banco.CriarContexto();
        var lista = await db.Atividades.Where(a => a.Tipo == TipoAtividade.LoginFalhou || a.Tipo == TipoAtividade.LoginEfetuado).ToListAsync();
        Assert.Contains(lista, a => a.Tipo == TipoAtividade.LoginFalhou && a.Operador == "Ana");
        Assert.Contains(lista, a => a.Tipo == TipoAtividade.LoginEfetuado && a.Operador == "Ana");
    }
}

public sealed class AlcadaRegrasTests
{
    [Theory]
    [InlineData(30000, 0, false)]       // sem limite
    [InlineData(30000, 30000, false)]   // no limite ainda pode
    [InlineData(30001, 30000, true)]
    public void Retirada_acima_do_limite_precisa_de_gerente(long valor, long limite, bool esperado) =>
        Assert.Equal(esperado, RegrasSeguranca.RetiradaPrecisaDeGerente(valor, limite));

    [Theory]
    [InlineData(1000, 10000, 0, false)]    // sem limite
    [InlineData(1000, 10000, 10, false)]   // 10% exatos podem
    [InlineData(1001, 10000, 10, true)]
    [InlineData(500, 0, 10, false)]        // carrinho vazio
    public void Desconto_acima_do_percentual_precisa_de_gerente(long desconto, long subtotal, int limite, bool esperado) =>
        Assert.Equal(esperado, RegrasSeguranca.DescontoPrecisaDeGerente(desconto, subtotal, limite));

    [Fact]
    public void Regras_novas_persistem_e_valores_ruins_sao_recusados()
    {
        var arq = Path.Combine(Path.GetTempPath(), "nexus-alc-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoSegurancaStore(arq);
            store.Salvar(new ConfiguracaoSeguranca { SangriaMaximaSemGerenteCentavos = 30000, DescontoMaximoSemGerentePercent = 15, PinGerenteParaCancelar = true });
            var c = new ConfiguracaoSegurancaStore(arq).Carregar();
            Assert.Equal(30000, c.SangriaMaximaSemGerenteCentavos);
            Assert.Equal(15, c.DescontoMaximoSemGerentePercent);
            Assert.True(c.PinGerenteParaCancelar);
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { DescontoMaximoSemGerentePercent = 101 }));
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { SangriaMaximaSemGerenteCentavos = -1 }));
        }
        finally { File.Delete(arq); }
    }
}

public class EncargosAtrasoTests
{
    private static readonly DateTime Hoje = new(2026, 11, 17);

    private static TituloAReceber Titulo(DateTime vencimento, long total, long pago = 0) =>
        new(1, vencimento.AddDays(-30), vencimento, total, pago);

    [Fact]
    public void Titulo_em_dia_ou_quitado_nao_gera_encargos()
    {
        var e = EncargosAtraso.Calcular(new[] { Titulo(Hoje.AddDays(5), 10000), Titulo(Hoje.AddDays(-10), 10000, pago: 10000) }, Hoje, 2m, 1m);
        Assert.Equal(0, e.TotalCentavos);
    }

    [Fact]
    public void Vencido_cobra_multa_uma_vez_e_juros_por_dia()
    {
        // saldo R$ 100,00, 15 dias de atraso, multa 2%, juros 3% ao mês: multa 2,00; juros 100 * 3% * 15/30 = 1,50
        var e = EncargosAtraso.Calcular(new[] { Titulo(Hoje.AddDays(-15), 10000) }, Hoje, 2m, 3m);
        Assert.Equal(200, e.MultaCentavos);
        Assert.Equal(150, e.JurosCentavos);
        Assert.Equal(350, e.TotalCentavos);
    }

    [Fact]
    public void Encargos_incidem_so_sobre_o_saldo_que_falta()
    {
        var e = EncargosAtraso.Calcular(new[] { Titulo(Hoje.AddDays(-30), 10000, pago: 6000) }, Hoje, 10m, 0m);
        Assert.Equal(400, e.MultaCentavos);   // 10% de R$ 40,00
    }

    [Fact]
    public void Sem_taxa_nao_cobra_nada()
    {
        Assert.Equal(0, EncargosAtraso.Calcular(new[] { Titulo(Hoje.AddDays(-90), 50000) }, Hoje, 0m, 0m).TotalCentavos);
    }

    [Fact]
    public void Taxas_do_crediario_persistem_e_valores_ruins_sao_recusados()
    {
        var arq = Path.Combine(Path.GetTempPath(), "nexus-cred-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoSegurancaStore(arq);
            store.Salvar(new ConfiguracaoSeguranca { MultaAtrasoPercent = 2m, JurosMesPercent = 1.5m, LimiteCreditoPadraoCentavos = 50000 });
            var c = new ConfiguracaoSegurancaStore(arq).Carregar();
            Assert.Equal(2m, c.MultaAtrasoPercent);
            Assert.Equal(1.5m, c.JurosMesPercent);
            Assert.Equal(50000, c.LimiteCreditoPadraoCentavos);
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { MultaAtrasoPercent = 101m }));
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { JurosMesPercent = -1m }));
            Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new ConfiguracaoSeguranca { LimiteCreditoPadraoCentavos = -1 }));
        }
        finally { File.Delete(arq); }
    }
}
