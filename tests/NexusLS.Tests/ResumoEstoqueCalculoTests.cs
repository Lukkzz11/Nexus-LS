using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class ResumoEstoqueCalculoTests
{
    private static Produto P(string nome, int estoque, long custo, long venda, string? cat = null, int minimo = 0,
        bool semBaixa = false, bool sobra = false, bool ativo = true, params (string, int)[] tams) => new()
    {
        Nome = nome, EstoqueAtual = estoque, PrecoCustoCentavos = custo, PrecoVendaCentavos = venda, Categoria = cat,
        EstoqueMinimo = minimo, VenderSemBaixa = semBaixa, Sobra = sobra, Ativo = ativo,
        Tamanhos = tams.Select(t => new ProdutoTamanho { Tamanho = t.Item1, Estoque = t.Item2 }).ToList()
    };

    [Fact]
    public void Totais_somam_pecas_valores_e_lucro_potencial_ignorando_sem_baixa_inativos_e_negativos()
    {
        var produtos = new List<Produto>
        {
            P("A", 10, 1000, 2500, minimo: 5),
            P("B", 2, 500, 900, minimo: 5),          // baixo
            P("C", 0, 700, 1000, minimo: 1),         // esgotado
            P("D", -3, 700, 1000),                   // negativo conta 0 nos valores e é esgotado
            P("Serviço", 99999, 100, 5000, semBaixa: true),
            P("Velho", 50, 100, 200, ativo: false),
        };
        var t = ResumoEstoqueCalculo.Totais(produtos);

        Assert.Equal(5, t.ProdutosAtivos);
        Assert.Equal(12, t.Pecas);
        Assert.Equal(10 * 1000 + 2 * 500, t.CustoCentavos);
        Assert.Equal(10 * 2500 + 2 * 900, t.VendaCentavos);
        Assert.Equal(t.VendaCentavos - t.CustoCentavos, t.LucroPotencialCentavos);
        Assert.Equal(1, t.Baixos);
        Assert.Equal(2, t.Esgotados);
        Assert.Equal(3, t.AbaixoDoMinimo);
    }

    [Fact]
    public void Por_categoria_agrupa_ordena_por_pecas_e_usa_Sem_categoria()
    {
        var g = ResumoEstoqueCalculo.PorCategoria(new[]
        {
            P("a", 3, 100, 200, "Bonés"), P("b", 4, 100, 300, " Bonés "), P("c", 20, 50, 80, "Camisetas"), P("d", 1, 10, 20, null),
        });
        Assert.Equal(new[] { "Camisetas", "Bonés", "Sem categoria" }, g.Select(x => x.Categoria));
        var bones = g[1];
        Assert.Equal(7, bones.Pecas);
        Assert.Equal(700, bones.CustoCentavos);
        Assert.Equal(3 * 200 + 4 * 300, bones.VendaCentavos);
    }

    [Fact]
    public void Por_tamanho_segue_a_ordem_da_grade_e_Sem_tamanho_vai_por_ultimo()
    {
        var g = ResumoEstoqueCalculo.PorTamanho(new[]
        {
            P("a", 0, 0, 0, tams: new[] { ("G", 2), ("P", 3), (Tamanhos.SemTamanho, 4), ("Único", 1) }),
            P("b", 0, 0, 0, tams: new[] { ("P", 1), ("M", 0), ("GG", 5) }),
        });
        Assert.Equal(new[] { "P", "G", "GG", "Único", Tamanhos.SemTamanho }, g.Select(x => x.Tamanho));
        Assert.Equal(4, g[0].Pecas);
    }

    [Fact]
    public void Maiores_por_valor_ordenam_pelo_custo_total_e_respeitam_o_limite()
    {
        var itens = ResumoEstoqueCalculo.MaioresPorValor(new[]
        {
            P("barato", 100, 10, 20), P("caro", 2, 5000, 9000), P("zero", 5, 0, 10), P("serv", 9, 9999, 1, semBaixa: true),
        }, limite: 2);
        Assert.Equal(new[] { "caro", "barato" }, itens.Select(i => i.Nome));
        Assert.Equal(10_000, itens[0].CustoCentavos);
    }

    [Fact]
    public void Sobras_x_normal_separa_pecas_e_custo()
    {
        var p = ResumoEstoqueCalculo.SobrasXNormal(new[]
        {
            P("a", 4, 100, 0), P("b", 3, 200, 0, sobra: true), P("c", 1, 50, 0, sobra: true),
        });
        Assert.Equal(new PartesEstoque(4, 400, 4, 650), p);
    }

    private static MovimentacaoEstoque M(DateTime quando, int qtd) => new() { DataHora = quando, Quantidade = qtd };

    [Fact]
    public void Movimentacoes_por_dia_preenchem_dias_sem_movimento_e_separam_entradas_de_saidas()
    {
        var de = new DateTime(2026, 5, 1);
        var pontos = ResumoEstoqueCalculo.Movimentacoes(new[]
        {
            M(new DateTime(2026, 5, 1, 9, 0, 0), 10), M(new DateTime(2026, 5, 1, 18, 0, 0), -3),
            M(new DateTime(2026, 5, 3, 12, 0, 0), -2), M(new DateTime(2026, 5, 3, 13, 0, 0), 0),
            M(new DateTime(2026, 4, 30, 23, 0, 0), 99),   // fora
            M(new DateTime(2026, 5, 4, 0, 0, 1), 99),     // fora
        }, de, new DateTime(2026, 5, 3));

        Assert.Equal(3, pontos.Count);
        Assert.Equal((10, 3), (pontos[0].Entradas, pontos[0].Saidas));
        Assert.Equal((0, 0), (pontos[1].Entradas, pontos[1].Saidas));
        Assert.Equal((0, 2), (pontos[2].Entradas, pontos[2].Saidas));
        Assert.Equal("01/05", pontos[0].Rotulo);
    }

    [Fact]
    public void Movimentacoes_de_periodo_longo_agrupam_por_semana_comecando_na_segunda()
    {
        // 2026-05-04 é segunda-feira
        var pontos = ResumoEstoqueCalculo.Movimentacoes(new[]
        {
            M(new DateTime(2026, 5, 6), 5), M(new DateTime(2026, 5, 10), -1), M(new DateTime(2026, 5, 11), 7),
        }, new DateTime(2026, 5, 4), new DateTime(2026, 5, 4).AddDays(59));

        Assert.All(pontos, p => Assert.Equal(DayOfWeek.Monday, p.Inicio.DayOfWeek));
        Assert.Equal((5, 1), (pontos[0].Entradas, pontos[0].Saidas));
        Assert.Equal((7, 0), (pontos[1].Entradas, pontos[1].Saidas));
    }

    [Fact]
    public async Task Servico_lista_as_movimentacoes_do_periodo_com_o_dia_final_incluido()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var estoque = new EstoqueService(banco.Fabrica);
        var prod = await produtos.CadastrarAsync(new DadosProduto("X", null, 100, 50, 0), 5);
        await estoque.AjustarAsync(prod.Id, TipoMovimentacao.Entrada, 3, "teste");

        var hoje = DateTime.Today;
        var lista = await estoque.ListarMovimentacoesPeriodoAsync(hoje, hoje);
        Assert.NotEmpty(lista);
        Assert.Empty(await estoque.ListarMovimentacoesPeriodoAsync(hoje.AddDays(-5), hoje.AddDays(-2)));
    }
}
