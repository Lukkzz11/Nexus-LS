using NexusLS.Core.Services;

namespace NexusLS.Tests;

public sealed class PreferenciasTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "pref-" + Guid.NewGuid().ToString("N") + ".json");
    public void Dispose() { if (File.Exists(_arquivo)) File.Delete(_arquivo); }

    [Fact]
    public void Sem_arquivo_vale_o_padrao_e_o_que_foi_salvo_volta()
    {
        var store = new PreferenciasStore(_arquivo);
        var p = store.Carregar();
        Assert.Equal(100, p.ZoomPercentual);
        Assert.Equal(200, p.DescontoPorPecaCentavos);

        p.ZoomPercentual = 125;
        p.DescontoPorPecaCentavos = 350;
        store.Salvar(p);
        var lido = new PreferenciasStore(_arquivo).Carregar();
        Assert.Equal(125, lido.ZoomPercentual);
        Assert.Equal(350, lido.DescontoPorPecaCentavos);
    }

    [Fact]
    public void Arquivo_estragado_ou_com_valores_absurdos_volta_ao_padrao_e_salvar_recusa_desconto_invalido()
    {
        var store = new PreferenciasStore(_arquivo);
        File.WriteAllText(_arquivo, "{ nao e json");
        Assert.Equal(100, store.Carregar().ZoomPercentual);

        File.WriteAllText(_arquivo, "{\"ZoomPercentual\": 900, \"DescontoPorPecaCentavos\": -5}");
        var p = store.Carregar();
        Assert.Equal(100, p.ZoomPercentual);
        Assert.Equal(200, p.DescontoPorPecaCentavos);

        Assert.Throws<RegraDeNegocioException>(() => store.Salvar(new Preferencias { DescontoPorPecaCentavos = 999_999_999 }));
    }
}
