using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;
using static NexusLS.Core.Services.DesempenhoVendas;

namespace NexusLS.Tests;

public class DesempenhoVendasTests
{
    private static readonly DateTime Hoje = new(2026, 10, 31);

    private static Venda V(int id, DateTime dia, long total, string? operador = "Ana", FormaPagamento forma = FormaPagamento.Dinheiro,
        params (int produto, string nome, int qtd, long totalItem, long? custo)[] itens) => new()
    {
        Id = id, DataHora = dia.AddHours(10), TotalCentavos = total, FormaPagamento = forma, Operador = operador,
        Itens = itens.Select(i => new ItemVenda { ProdutoId = i.produto, NomeProduto = i.nome, Quantidade = i.qtd, TotalCentavos = i.totalItem, CustoUnitarioCentavos = i.custo }).ToList()
    };

    private static readonly Dictionary<int, (string?, long)> SemCadastro = new();

    [Fact]
    public void Periodos_incluem_as_duas_pontas_e_o_anterior_tem_o_mesmo_tamanho()
    {
        Assert.Equal(new Intervalo(Hoje, Hoje), Resolver(TipoPeriodo.Hoje, Hoje));
        var sete = Resolver(TipoPeriodo.Ultimos7Dias, Hoje);
        Assert.Equal(new DateTime(2026, 10, 25), sete.Inicio);
        Assert.Equal(7, sete.Dias);
        Assert.Equal(new DateTime(2026, 10, 1), Resolver(TipoPeriodo.MesAtual, Hoje).Inicio);
        Assert.Equal(30, Resolver(TipoPeriodo.Ultimos30Dias, Hoje).Dias);

        var ant = Anterior(sete);
        Assert.Equal(new DateTime(2026, 10, 18), ant.Inicio);
        Assert.Equal(new DateTime(2026, 10, 24), ant.Fim);

        // datas trocadas são corrigidas
        var inv = Resolver(TipoPeriodo.Personalizado, Hoje, new DateTime(2026, 10, 20), new DateTime(2026, 10, 10));
        Assert.Equal(new DateTime(2026, 10, 10), inv.Inicio);
        // personalizado sem datas vira hoje
        Assert.Equal(1, Resolver(TipoPeriodo.Personalizado, Hoje).Dias);
    }

    [Fact]
    public void O_total_da_venda_e_rateado_pelos_itens_e_fecha_no_centavo()
    {
        // itens somam 100,00 mas a venda foi de 90,01 (desconto): 3 itens de 33,33 / 33,33 / 33,34
        var venda = V(1, Hoje, 9001, "Ana", FormaPagamento.Pix, (1, "A", 1, 3333, 1000), (2, "B", 1, 3333, 1000), (3, "C", 1, 3334, 1000));

        var linhas = ExpandirLinhas(new[] { venda }, SemCadastro);

        Assert.Equal(3, linhas.Count);
        Assert.Equal(9001, linhas.Sum(l => l.ValorCentavos));
    }

    [Fact]
    public void Venda_cancelada_nao_entra_e_venda_sem_itens_conta_no_faturamento_mas_nao_nas_pecas()
    {
        var cancelada = V(1, Hoje, 5000, itens: (1, "A", 1, 5000, null));
        cancelada.Cancelada = true;
        var semItens = V(2, Hoje, 7000);

        var r = Resumir(ExpandirLinhas(new[] { cancelada, semItens }, SemCadastro));

        Assert.Equal(7000, r.FaturamentoCentavos);
        Assert.Equal(1, r.Vendas);
        Assert.Equal(0, r.Pecas);
        Assert.Null(r.LucroCentavos); // sem itens não há custo conhecido
    }

    [Fact]
    public void Lucro_so_aparece_quando_o_custo_e_conhecido_em_todas_as_pecas()
    {
        var completo = V(1, Hoje, 10000, itens: new[] { (1, "A", 2, 10000L, (long?)3000) });
        var r = Resumir(ExpandirLinhas(new[] { completo }, SemCadastro));
        Assert.Equal(4000, r.LucroCentavos);       // 100,00 − 2 × 30,00
        Assert.Equal(40m, r.MargemPct);
        Assert.Equal(1m, r.CoberturaCusto);

        // um item sem custo (nem guardado, nem no cadastro) tira o lucro da tela
        var parcial = V(2, Hoje, 10000, itens: new[] { (1, "A", 1, 5000L, (long?)3000), (2, "B", 1, 5000L, (long?)null) });
        var p = Resumir(ExpandirLinhas(new[] { parcial }, SemCadastro));
        Assert.Null(p.LucroCentavos);
        Assert.Null(p.MargemPct);
        Assert.Equal(0.5m, p.CoberturaCusto);

        // custo do cadastro entra quando a venda não guardou
        var cadastro = new Dictionary<int, (string?, long)> { [2] = ("Bonés", 2000) };
        Assert.Equal(5000 - 2000 + 5000 - 3000, Resumir(ExpandirLinhas(new[] { parcial }, cadastro)).LucroCentavos);
    }

    [Fact]
    public void Ticket_medio_e_variacao_contra_o_periodo_anterior()
    {
        var vendas = new[] { V(1, Hoje, 3000), V(2, Hoje, 7000) };
        var r = Resumir(ExpandirLinhas(vendas, SemCadastro));
        Assert.Equal(10000, r.FaturamentoCentavos);
        Assert.Equal(5000, r.TicketMedioCentavos);

        Assert.Equal(25m, Variacao(125, 100));
        Assert.Equal(-50m, Variacao(50, 100));
        Assert.Null(Variacao(10, 0));
    }

    [Fact]
    public void Mais_menos_vendidos_e_produtos_sem_venda()
    {
        var vendas = new[]
        {
            V(1, Hoje, 6000, itens: new[] { (1, "Camiseta", 3, 3000L, (long?)null), (2, "Boné", 1, 3000L, (long?)null) }),
            V(2, Hoje, 1000, itens: new[] { (3, "Meia", 1, 1000L, (long?)null) })
        };
        var linhas = ExpandirLinhas(vendas, SemCadastro);

        Assert.Equal("Camiseta", MaisVendidos(linhas, 1).Single().Nome);
        Assert.Equal(new[] { "Meia", "Boné" }, MenosVendidos(linhas, 2).Select(p => p.Nome).ToArray()); // 1 peça cada: o de menor valor vem primeiro (Meia 10,00; Boné 30,00)

        var cadastro = new[] { (1, "Camiseta", (string?)null, 5, true), (4, "Cinto", "Acessórios", 7, true), (5, "Velho", (string?)null, 0, false) };
        var sem = SemVendas(cadastro, linhas);
        var cinto = Assert.Single(sem);
        Assert.Equal("Cinto", cinto.Nome);
        Assert.Equal(7, cinto.Estoque);
    }

    [Fact]
    public void Por_categoria_forma_e_operador_somam_o_mesmo_total()
    {
        var cad = new Dictionary<int, (string?, long)> { [1] = ("Camisetas", 0), [2] = ("Bonés", 0) };
        var vendas = new[]
        {
            V(1, Hoje, 4000, "Ana", FormaPagamento.Pix, (1, "A", 1, 4000, null)),
            V(2, Hoje, 6000, "Bruno", FormaPagamento.CartaoCredito, (2, "B", 2, 6000, null)),
            V(3, Hoje, 1000, "Caixa Geral", FormaPagamento.Pix, (1, "A", 1, 1000, null))
        };
        var linhas = ExpandirLinhas(vendas, cad);

        Assert.Equal(11000, PorCategoria(linhas).Sum(g => g.ValorCentavos));
        Assert.Equal("Bonés", PorCategoria(linhas)[0].Categoria);
        var formas = PorForma(linhas);
        Assert.Equal(FormaPagamento.CartaoCredito, formas[0].Forma);
        Assert.Equal(5000, formas[1].ValorCentavos); // Pix: 40,00 + 10,00

        var ops = PorOperador(linhas)!;
        Assert.Equal(new[] { "Bruno", "Ana", "Não identificado" }, ops.Select(o => o.Operador).ToArray());
    }

    [Fact]
    public void Por_operador_e_nulo_quando_ninguem_e_identificavel()
    {
        var linhas = ExpandirLinhas(new[] { V(1, Hoje, 1000, "Caixa Geral"), V(2, Hoje, 1000, null) }, SemCadastro);
        Assert.Null(PorOperador(linhas));
    }

    [Fact]
    public void Serie_no_tempo_inclui_dias_sem_venda_e_agrupa_por_semana_e_mes()
    {
        var periodo = new Intervalo(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31));
        var vendas = new[]
        {
            V(1, new DateTime(2026, 10, 1), 1000), V(2, new DateTime(2026, 10, 1), 2000), V(3, new DateTime(2026, 10, 15), 4000)
        };
        var linhas = ExpandirLinhas(vendas, SemCadastro);

        var dias = SerieNoTempo(linhas, periodo);
        Assert.Equal(31, dias.Count);
        Assert.Equal(3000, dias[0].FaturamentoCentavos);
        Assert.Equal(2, dias[0].Vendas);
        Assert.Equal(0, dias[1].FaturamentoCentavos);
        Assert.Equal("15/10", dias[14].Rotulo);

        var mes = SerieNoTempo(linhas, periodo, Granularidade.Mes);
        Assert.Single(mes);
        Assert.Equal(7000, mes[0].FaturamentoCentavos);

        var semanas = SerieNoTempo(linhas, periodo, Granularidade.Semana);
        Assert.Equal(7000, semanas.Sum(s => s.FaturamentoCentavos));
        Assert.All(semanas, s => Assert.Equal(DayOfWeek.Monday, s.Inicio.DayOfWeek));
        Assert.Equal(new DateTime(2026, 9, 28), semanas[0].Inicio); // 1/10/2026 é quinta: a semana começa na segunda anterior
    }

    [Fact]
    public async Task Servico_compara_com_o_periodo_anterior_e_ignora_canceladas_e_fora_do_periodo()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Boné", null, 10000, 4000, 1), 50);
        await produtos.CadastrarAsync(new DadosProduto("Encalhado", null, 5000, 2000, 1), 3);

        await using (var db = banco.CriarContexto())
        {
            Venda Nova(DateTime dia, long total, bool cancelada = false) => new()
            {
                DataHora = dia.AddHours(9), TotalCentavos = total, SubtotalCentavos = total, Cancelada = cancelada, Operador = "Ana",
                Itens = { new ItemVenda { ProdutoId = p.Id, NomeProduto = "Boné", Quantidade = 1, PrecoUnitarioCentavos = total, TotalCentavos = total, CustoUnitarioCentavos = 4000 } }
            };
            db.Vendas.AddRange(
                Nova(Hoje, 10000), Nova(Hoje.AddDays(-1), 20000),                // período (2 dias: 30/10 e 31/10)
                Nova(Hoje.AddDays(-2), 5000), Nova(Hoje.AddDays(-3), 5000),      // anterior
                Nova(Hoje, 99999, cancelada: true),                              // cancelada: fora
                Nova(Hoje.AddDays(-20), 77777));                                 // longe: fora
            await db.SaveChangesAsync();
        }

        var r = await new DesempenhoService(banco.Fabrica).GerarAsync(new Intervalo(Hoje.AddDays(-1), Hoje), Granularidade.Dia);

        Assert.Equal(30000, r.Atual.FaturamentoCentavos);
        Assert.Equal(2, r.Atual.Vendas);
        Assert.Equal(10000, r.Antes.FaturamentoCentavos);
        Assert.Equal(200m, Variacao(r.Atual.FaturamentoCentavos, r.Antes.FaturamentoCentavos));
        Assert.Equal(30000 - 8000, r.Atual.LucroCentavos);
        Assert.Equal(2, r.Serie.Count);
        Assert.Equal("Boné", r.MaisVendidos.Single().Nome);
        Assert.Equal("Encalhado", r.SemVendas.Single().Nome);
        Assert.Equal(3, r.SemVendas.Single().Estoque);
        Assert.Equal("Ana", r.PorOperador!.Single().Operador);
    }
}
