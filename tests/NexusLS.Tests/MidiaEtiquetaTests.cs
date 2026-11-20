using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;

namespace NexusLS.Tests;

public class MidiaEtiquetaTests
{
    private static Produto BermudaCargo() => new()
    {
        Id = 45,
        Nome = "BERMUDA CARGO",
        Referencia = "1402",
        PrecoVendaCentavos = 7000,
        Tamanhos = new List<ProdutoTamanho>
        {
            new() { Tamanho = "Sem tamanho", Ordem = 0, Estoque = 0 },
            new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
            new() { Tamanho = "44", Ordem = 2, Estoque = 8 },
            new() { Tamanho = "46", Ordem = 3, Estoque = 8 },
            new() { Tamanho = "48", Ordem = 4, Estoque = 12 },
            new() { Tamanho = "50", Ordem = 5, Estoque = 2 }
        }
    };

    [Fact]
    public void Selecao_da_bermuda_cargo_soma_30_e_38_e_mantem_todos_os_tamanhos()
    {
        var selecao = SelecaoEtiquetas.DoProduto(BermudaCargo(), new Dictionary<string, int>
        {
            ["42"] = 30,
            ["44"] = 38,
            ["46"] = 0,
            ["48"] = 0,
            ["50"] = 0
        });

        Assert.Equal(6, selecao.Linhas.Count);
        Assert.Equal(68, selecao.Total);
        Assert.Equal(2, selecao.ComQuantidade.Count);
        Assert.Equal(30, selecao.ComQuantidade[0].Quantidade);
        Assert.Equal("42", selecao.ComQuantidade[0].Tamanho);
        Assert.Equal(38, selecao.ComQuantidade[1].Quantidade);
        Assert.Equal("44", selecao.ComQuantidade[1].Tamanho);
        Assert.False(selecao.Vazia);

        // A referência do produto não é alterada pela seleção.
        Assert.Equal("1402", selecao.Produto.Referencia);
    }

    [Fact]
    public void Todos_os_tamanhos_aparecem_mesmo_sem_estoque()
    {
        var produto = BermudaCargo();
        foreach (var t in produto.Tamanhos!) t.Estoque = 0;

        var selecao = SelecaoEtiquetas.DoProduto(produto);

        // Produção: o saldo não decide quais tamanhos podem ser impressos.
        Assert.Equal(new[] { "42", "44", "46", "48", "50", "" }, selecao.Linhas.Select(l => l.Tamanho));
        Assert.All(selecao.Linhas, l => Assert.Equal(0, l.Quantidade));
    }

    [Fact]
    public void Itens_da_selecao_levam_codigo_referencia_mais_ordem_do_tamanho()
    {
        var selecao = SelecaoEtiquetas.DoProduto(BermudaCargo(), new Dictionary<string, int>
        {
            ["42"] = 30,
            ["44"] = 38
        });

        var itens = selecao.ParaItens();

        Assert.Equal(2, itens.Count);
        Assert.Equal("14021", itens[0].CodigoBarras);
        Assert.Equal(30, itens[0].Quantidade);
        Assert.Equal("14022", itens[1].CodigoBarras);
        Assert.Equal(38, itens[1].Quantidade);
        Assert.All(itens, i => Assert.Equal(45, i.ProdutoId));
    }

    [Fact]
    public void Quantidade_negativa_e_recusada()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SelecaoEtiquetas.DoProduto(BermudaCargo(), new Dictionary<string, int> { ["42"] = -1 }));
    }

    [Fact]
    public void Todos_os_tamanhos_zerados_deixam_a_selecao_vazia_mas_editavel()
    {
        var selecao = SelecaoEtiquetas.DoProduto(BermudaCargo());

        Assert.Equal(6, selecao.Linhas.Count);
        Assert.Equal(0, selecao.Total);
        Assert.True(selecao.Vazia);
        Assert.Empty(selecao.ComQuantidade);
        Assert.Empty(selecao.ParaItens());
        Assert.Equal(0, selecao.TotalDePaginas(PerfisMidia.Rolo50x30Gap.Formato));
    }

    [Fact]
    public void Produto_sem_grade_usa_uma_linha_unica()
    {
        var produto = new Produto
        {
            Nome = "BONE NEXUS",
            Referencia = "9001",
            CodigoInterno = "INT-9",
            PrecoVendaCentavos = 3500
        };

        var selecao = SelecaoEtiquetas.DoProduto(produto, new Dictionary<string, int> { [""] = 4 });

        var linha = Assert.Single(selecao.Linhas);
        Assert.True(linha.EhSemTamanho);
        Assert.Equal("Único", linha.TamanhoExibicao);
        Assert.Equal(4, selecao.Total);

        var item = Assert.Single(selecao.ParaItens());
        Assert.Equal("", item.Tamanho);
        Assert.Equal("9001", item.CodigoBarras);
    }

    [Fact]
    public void Paginacao_respeita_o_total_por_tamanho_e_o_formato_de_midia()
    {
        var selecao = SelecaoEtiquetas.DoProduto(BermudaCargo(), new Dictionary<string, int>
        {
            ["42"] = 30,
            ["44"] = 38
        });

        // Rolo: uma etiqueta por página.
        Assert.Equal(68, selecao.TotalDePaginas(PerfisMidia.Rolo50x30Gap.Formato));

        // Folha A4 3 x 7: 21 por folha -> 4 folhas para 68 etiquetas.
        var folha = selecao.Paginar(PerfisMidia.FolhaA4TresPorSete.Formato);
        Assert.Equal(4, folha.Count);
        Assert.Equal(21, folha[0].Count);
        Assert.Equal(5, folha[^1].Count); // 68 = 21 + 21 + 21 + 5

        // As 30 primeiras posições são do 42 e as 38 seguintes do 44.
        var etiquetas = selecao.ParaItens();
        Assert.Equal("42", etiquetas[0].Tamanho);
        Assert.Equal("44", etiquetas[^1].Tamanho);
    }

    [Fact]
    public void Folha_a4_tem_21_etiquetas_e_medidas_do_gabarito_pimaco()
    {
        var folha = PerfisMidia.FolhaA4TresPorSete;

        Assert.Equal(TipoMidiaEtiqueta.Folha, folha.Tipo);
        Assert.Equal(21, folha.EtiquetasPorPagina);
        Assert.Equal(210, folha.PaginaLarguraMm);
        Assert.Equal(297, folha.PaginaAlturaMm);
        Assert.Equal(63.5, folha.LarguraEtiquetaMm);
        Assert.Equal(38.1, folha.AlturaEtiquetaMm);
    }

    [Theory]
    [InlineData(TipoMidiaEtiqueta.RoloComGap)]
    [InlineData(TipoMidiaEtiqueta.RoloMarcaPreta)]
    [InlineData(TipoMidiaEtiqueta.RoloContinuo)]
    public void Midia_de_rolo_valida_em_impressora_de_4_polegadas(TipoMidiaEtiqueta tipo)
    {
        var midia = new PerfilMidia("Teste", tipo, 50, 30, 0, tipo == TipoMidiaEtiqueta.RoloComGap ? 3 : 0,
            1, 1, 50, 30, 0, 0);

        Assert.Empty(midia.Validar(104));
    }

    [Fact]
    public void Papel_mais_largo_que_a_impressora_e_recusado()
    {
        var midia = PerfisMidia.Rolo100x50Gap;
        var erros = midia.Validar(56);

        Assert.Contains(erros, e => e.Contains("mais largo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Midia_com_gap_exige_espaco_entre_etiquetas()
    {
        var midia = PerfisMidia.Rolo50x30Gap with { EspacoVerticalMm = 0 };

        var erros = midia.Validar(104);

        Assert.Contains(erros, e => e.Contains("gap", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Perfis_de_impressora_nao_prometem_midia_incompativel()
    {
        var termica = PerfilImpressora.Perfis.First(p => p.Nome.StartsWith("Térmica 4", StringComparison.Ordinal));
        var folhas = PerfilImpressora.Perfis.First(p => p.Nome.StartsWith("Impressora de folhas", StringComparison.Ordinal));

        Assert.True(termica.Aceita(TipoMidiaEtiqueta.RoloComGap));
        Assert.True(termica.Aceita(TipoMidiaEtiqueta.RoloMarcaPreta));
        Assert.True(termica.Aceita(TipoMidiaEtiqueta.RoloContinuo));
        Assert.False(termica.Aceita(TipoMidiaEtiqueta.Folha));

        Assert.True(folhas.Aceita(TipoMidiaEtiqueta.Folha));
        Assert.False(folhas.Aceita(TipoMidiaEtiqueta.RoloComGap));
        Assert.Contains("manual", termica.Observacao, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Midia_personalizada_pode_ter_medidas_proprias()
    {
        var midia = PerfisMidia.Personalizado with
        {
            LarguraEtiquetaMm = 45,
            AlturaEtiquetaMm = 22,
            EspacoVerticalMm = 2.5,
            PaginaLarguraMm = 45,
            PaginaAlturaMm = 22
        };

        Assert.Empty(midia.Validar(104));
        Assert.Equal(1, midia.EtiquetasPorPagina);
        Assert.Contains("sensor de gap", midia.Resumo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Produto_com_um_tamanho_nao_ganha_tamanhos_inventados()
    {
        var bermuda = new Produto
        {
            Id = 45, Nome = "BERMUDA CARGO", Referencia = "1402", Categoria = "Bermuda", PrecoVendaCentavos = 7000,
            Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = "42", Ordem = 1, Estoque = 0 } }
        };

        var selecao = SelecaoEtiquetas.DoProduto(bermuda);
        Assert.Equal(new[] { "42" }, selecao.Linhas.Select(l => l.Tamanho));
        Assert.Single(bermuda.Tamanhos);
    }
}
