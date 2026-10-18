using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Categoria, código interno, EAN-13 interno, duplicidade e calculadora de preço.</summary>
public class ProdutoCodigosETests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;

    public ProdutoCodigosETests() => _produtos = new ProdutoService(_banco.Fabrica);

    public void Dispose() => _banco.Dispose();

    private static DadosProduto Dados(string nome, string? codigo = null, string? referencia = null, string? categoria = null) =>
        new(nome, codigo, 1000, 600, 1, referencia, 0, categoria);

    // ---------- Código interno ----------

    [Fact]
    public async Task Cada_produto_novo_ganha_um_codigo_interno_em_sequencia()
    {
        var a = await _produtos.CadastrarAsync(Dados("Camiseta"));
        var b = await _produtos.CadastrarAsync(Dados("Boné"));

        Assert.Equal("INT-000001", a.CodigoInterno);
        Assert.Equal("INT-000002", b.CodigoInterno);
    }

    [Fact]
    public async Task Codigo_interno_nao_e_reaproveitado_nem_muda_ao_editar()
    {
        var a = await _produtos.CadastrarAsync(Dados("Camiseta"));
        await _produtos.DesativarAsync(a.Id);
        var b = await _produtos.CadastrarAsync(Dados("Boné"));
        await _produtos.EditarAsync(b.Id, Dados("Boné Novo"));

        var lista = await _produtos.BuscarAsync(null, incluirInativos: true);
        Assert.Equal("INT-000002", lista.Single(p => p.Id == b.Id).CodigoInterno);
    }

    [Fact]
    public async Task Busca_e_leitor_encontram_pelo_codigo_interno()
    {
        var a = await _produtos.CadastrarAsync(Dados("Camiseta"));

        Assert.Equal(a.Id, (await _produtos.BuscarAsync("INT-000001")).Single().Id);
        Assert.Equal(a.Id, (await _produtos.ObterPorCodigoBarrasAsync("INT-000001"))?.Id);
    }

    // ---------- Categoria ----------

    [Fact]
    public async Task Categoria_e_guardada_limpa_e_listada_sem_repetir()
    {
        await _produtos.CadastrarAsync(Dados("Camiseta", categoria: "  Roupas "));
        await _produtos.CadastrarAsync(Dados("Calça", categoria: "Roupas"));
        await _produtos.CadastrarAsync(Dados("Boné", categoria: "Acessórios"));
        await _produtos.CadastrarAsync(Dados("Sem categoria", categoria: " "));

        Assert.Equal(new[] { "Acessórios", "Roupas" }, await _produtos.ListarCategoriasAsync());
        Assert.Equal("Roupas", (await _produtos.BuscarAsync("Camiseta")).Single().Categoria);
        Assert.Equal(2, (await _produtos.BuscarAsync("Roupas")).Count); // a busca também olha a categoria
    }

    // ---------- Duplicidade ----------

    [Fact]
    public async Task Mesmo_nome_e_mesma_referencia_e_recusado_sem_olhar_maiusculas()
    {
        await _produtos.CadastrarAsync(Dados("Camiseta Básica", referencia: "R1"));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _produtos.CadastrarAsync(Dados("camiseta básica", referencia: "r1")));
        await _produtos.CadastrarAsync(Dados("Camiseta Básica", referencia: "R2")); // outra referência: pode
    }

    [Fact]
    public async Task Mesmo_nome_sem_referencia_nos_dois_e_recusado()
    {
        await _produtos.CadastrarAsync(Dados("Boné"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _produtos.CadastrarAsync(Dados("Boné")));
    }

    [Fact]
    public async Task Editar_sem_mudar_nada_nao_conta_como_duplicado_e_inativo_nao_bloqueia()
    {
        var a = await _produtos.CadastrarAsync(Dados("Boné"));
        await _produtos.EditarAsync(a.Id, Dados("Boné"));       // não colide consigo mesmo

        await _produtos.DesativarAsync(a.Id);
        await _produtos.CadastrarAsync(Dados("Boné"));          // o antigo está inativo
    }

    // ---------- EAN-13 interno ----------

    [Theory]
    [InlineData("789123456789", 5)]   // exemplo clássico
    [InlineData("200000000001", 5)]
    public void Digito_verificador_do_ean13(string base12, int esperado) =>
        Assert.Equal(esperado, CodigoBarrasInterno.DigitoVerificador(base12));

    [Fact]
    public void Codigo_montado_comeca_com_2_tem_13_digitos_e_e_valido()
    {
        var codigo = CodigoBarrasInterno.Montar(1);

        Assert.Equal("2000000000015", codigo);
        Assert.True(CodigoBarrasInterno.EhEan13Valido(codigo));
        Assert.Equal(1, CodigoBarrasInterno.SequenciaDe(codigo));
        Assert.False(CodigoBarrasInterno.EhEan13Valido("2000000000013")); // verificador errado
        Assert.Null(CodigoBarrasInterno.SequenciaDe("7891234567895"));    // não é interno
    }

    [Fact]
    public async Task Gerador_devolve_o_proximo_livre_e_pula_os_ja_usados()
    {
        Assert.Equal(CodigoBarrasInterno.Montar(1), await _produtos.GerarCodigoBarrasInternoAsync());

        await _produtos.CadastrarAsync(Dados("A", codigo: CodigoBarrasInterno.Montar(1)));
        await _produtos.CadastrarAsync(Dados("B", codigo: CodigoBarrasInterno.Montar(7)));
        await _produtos.CadastrarAsync(Dados("C", codigo: "7891234567895"));   // de fabricante: não conta

        Assert.Equal(CodigoBarrasInterno.Montar(8), await _produtos.GerarCodigoBarrasInternoAsync());
    }

    // ---------- Calculadora de preço ----------

    [Fact]
    public void Margem_e_sobre_o_preco_e_markup_e_sobre_o_custo()
    {
        Assert.Equal(40m, CalculadoraPreco.Margem(10000, 6000));
        Assert.Equal(66.67m, CalculadoraPreco.Markup(10000, 6000));
        Assert.Null(CalculadoraPreco.Margem(0, 500));
        Assert.Null(CalculadoraPreco.Markup(1000, 0));
    }

    [Fact]
    public void Preco_pela_margem_e_pelo_markup_arredonda_ao_centavo()
    {
        Assert.Equal(10000, CalculadoraPreco.PrecoPorMargem(6000, 40));
        Assert.Equal(9000, CalculadoraPreco.PrecoPorMarkup(6000, 50));
        Assert.Equal(1500, CalculadoraPreco.PrecoPorMargem(1000, 33.33m));
        Assert.Equal(1333, CalculadoraPreco.PrecoPorMarkup(1000, 33.33m));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(-1)]
    public void Margem_fora_do_limite_e_recusada(double margem) =>
        Assert.Throws<RegraDeNegocioException>(() => CalculadoraPreco.PrecoPorMargem(1000, (decimal)margem));

    [Fact]
    public void Markup_negativo_e_recusado() =>
        Assert.Throws<RegraDeNegocioException>(() => CalculadoraPreco.PrecoPorMarkup(1000, -5));
}
