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
