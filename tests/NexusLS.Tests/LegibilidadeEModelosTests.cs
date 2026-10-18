using NexusLS.Core.Etiquetas;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>Trava de legibilidade do código de barras e modelos de bobina salvos pelo usuário.</summary>
public class LegibilidadeEModelosTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-modelos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private ModeloEtiquetaStore NovoStore() => new(Path.Combine(_pasta, "etiquetas.json"));

    // ---------- Legibilidade ----------

    [Fact]
    public void Ean13_cabe_com_folga_nas_bobinas_de_fabrica()
    {
        foreach (var formato in FormatoEtiqueta.Todos)
            Assert.True(LegibilidadeCodigo.Legivel("2000000000015", formato.EtiquetaLarguraMm), formato.Nome);
    }

    [Fact]
    public void Codigo_comprido_numa_etiqueta_estreita_fica_ilegivel()
    {
        var comprido = new string('A', 25);

        Assert.False(LegibilidadeCodigo.Legivel(comprido, 40));
        Assert.True(LegibilidadeCodigo.ModuloMm(comprido, 40) < LegibilidadeCodigo.ModuloMinimoMm);
    }

    [Fact]
    public void A_largura_minima_calculada_torna_o_codigo_legivel()
    {
        var codigo = new string('A', 25);
        var minima = LegibilidadeCodigo.LarguraMinimaMm(codigo);

        Assert.True(LegibilidadeCodigo.Legivel(codigo, minima + 0.01));
        Assert.False(LegibilidadeCodigo.Legivel(codigo, minima - 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("café")]   // acento: o Code 128 não aceita; a etiqueta sai sem barras, então não há o que ilegibilizar
    public void Sem_codigo_ou_codigo_nao_codificavel_nao_conta_como_ilegivel(string? codigo) =>
        Assert.True(LegibilidadeCodigo.Legivel(codigo, 20));

    // ---------- Modelos salvos ----------

    [Fact]
    public void Sem_arquivo_nao_ha_modelos_e_arquivo_estragado_vira_lista_vazia()
    {
        var store = NovoStore();
        Assert.Empty(store.Carregar());

        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Path.Combine(_pasta, "etiquetas.json"), "{ isto não é json");
        Assert.Empty(store.Carregar());
    }

    [Fact]
    public void Modelo_salvo_volta_igual_ao_reabrir_o_programa()
    {
        var modelo = NovoStore().AdicionarBobina("  Joias ", 60, 40);

        var recarregado = NovoStore().Carregar().Single();

        Assert.Equal(modelo, recarregado);
        Assert.Equal("Joias (60 × 40 mm)", recarregado.Nome);
        Assert.Equal((1, 1, 60.0, 40.0), (recarregado.Colunas, recarregado.Linhas, recarregado.EtiquetaLarguraMm, recarregado.EtiquetaAlturaMm));
    }

    [Fact]
    public void Modelo_invalido_ou_repetido_e_recusado()
    {
        var store = NovoStore();
        store.AdicionarBobina("Joias", 60, 40);

        Assert.Throws<RegraDeNegocioException>(() => store.AdicionarBobina("", 60, 40));
        Assert.Throws<RegraDeNegocioException>(() => store.AdicionarBobina("Pequena", 10, 40));
        Assert.Throws<RegraDeNegocioException>(() => store.AdicionarBobina("Grande", 60, 500));
        Assert.Throws<RegraDeNegocioException>(() => store.AdicionarBobina("joias", 60, 40));   // mesmo nome e medidas
        store.AdicionarBobina("Joias", 70, 40);                                                 // outras medidas: pode
    }

    [Fact]
    public void Remover_tira_so_o_modelo_escolhido()
    {
        var store = NovoStore();
        var a = store.AdicionarBobina("A", 60, 40);
        store.AdicionarBobina("B", 60, 40);

        store.Remover(a);

        Assert.Equal("B (60 × 40 mm)", store.Carregar().Single().Nome);
    }

    [Fact]
    public void Modelo_salvo_diagrama_como_bobina_normal()
    {
        var modelo = NovoStore().AdicionarBobina("Joias", 60, 40);
        var item = new ItemEtiqueta(1, "Anel", "", 1000, null, 2);

        var paginas = DiagramadorEtiquetas.Paginar(new[] { item }, modelo);

        Assert.Equal(2, paginas.Count);   // uma etiqueta por página
    }
}
