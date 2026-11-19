using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests.Gestao;

public class RelatorioProdutosTests
{
    private static readonly DesempenhoVendas.Intervalo Nov = new(new DateTime(2026, 11, 1), new DateTime(2026, 11, 30));
    private static int _seq = 1;

    private static Produto P(int id, string nome, string? cat, long custo = 0, int estoque = 0, bool ativo = true, string? referencia = null) => new()
    {
        Id = id, Nome = nome, Categoria = cat, PrecoCustoCentavos = custo, EstoqueAtual = estoque, Ativo = ativo, Referencia = referencia
    };

    private static ItemVenda I(int produto, string nome, string tam, int qtd, long unit, long? custo = null) => new()
    {
        ProdutoId = produto, NomeProduto = nome, Tamanho = tam, Quantidade = qtd, PrecoUnitarioCentavos = unit, TotalCentavos = qtd * unit, CustoUnitarioCentavos = custo
    };

    private static Venda V(int dia, bool cancelada = false, long desconto = 0, params ItemVenda[] itens)
    {
        var sub = itens.Sum(i => i.TotalCentavos);
        return new Venda { Id = _seq++, DataHora = new DateTime(2026, 11, dia, 10, 0, 0), SubtotalCentavos = sub, DescontoCentavos = desconto, TotalCentavos = sub - desconto, Cancelada = cancelada, Itens = itens.ToList() };
    }

    private static readonly Produto[] Cadastro =
    {
        P(1, "Camiseta", "Roupas", custo: 2000, referencia: "CAM1"),
        P(2, "Boné", "Acessórios", custo: 1000, referencia: "BON1"),
        P(3, "Meia", null, custo: 0)
    };

    [Fact]
    public void Mais_vendidos_por_quantidade_com_percentual_e_posicao()
    {
        var vendas = new[]
        {
            V(1, false, 0, I(1, "Camiseta", "M", 3, 5000, 2000), I(2, "Boné", Tamanhos.SemTamanho, 1, 4000, 1000)),
            V(2, false, 0, I(1, "Camiseta", "G", 2, 5000, 2000)),
            V(3, true, 0, I(2, "Boné", Tamanhos.SemTamanho, 50, 4000)),      // cancelada: fora
            V(4, false, 0, I(2, "Boné", Tamanhos.SemTamanho, 1, 4000, 1000))
        };
        var r = RelatorioProdutos.Gerar(vendas, Cadastro, Nov);

        Assert.Equal(33000, r.FaturamentoCentavos);
        Assert.Equal(7, r.Pecas);
        Assert.Equal(new[] { "Camiseta", "Boné" }, r.MaisVendidos.Select(p => p.Nome).ToArray());
        var cam = r.MaisVendidos[0];
        Assert.Equal((1, 5, 25000, 2), (cam.Posicao, cam.Pecas, (int)cam.ValorCentavos, cam.Vendas));
        Assert.Equal(75.76m, cam.PercentualDoFaturamento);
        Assert.Equal("CAM1", cam.Referencia);
        Assert.Equal(15000, cam.LucroCentavos);       // 25.000 - 5 x 2.000
        Assert.Equal(60m, cam.MargemPct);
    }

    [Fact]
    public void Desconto_da_venda_e_rateado_nos_valores()
    {
        var r = RelatorioProdutos.Gerar(new[] { V(1, false, 1000, I(1, "Camiseta", "M", 1, 5000, 2000), I(2, "Boné", "U", 1, 5000, 1000)) }, Cadastro, Nov);
        Assert.Equal(9000, r.FaturamentoCentavos);
        Assert.All(r.MaisVendidos, p => Assert.Equal(4500, p.ValorCentavos));
    }

    [Fact]
    public void Margem_so_aparece_quando_todo_custo_e_conhecido()
    {
        // Meia: sem custo no item nem no cadastro -> "sem dados" (nulo), não zero.
        var r = RelatorioProdutos.Gerar(new[] { V(1, false, 0, I(3, "Meia", "U", 2, 1000), I(1, "Camiseta", "M", 1, 5000)) }, Cadastro, Nov);
        Assert.Null(r.MaisVendidos.Single(p => p.Nome == "Meia").MargemPct);
        Assert.Null(r.LucroCentavos);
        Assert.Null(r.MargemPct);
        Assert.NotNull(r.MaisVendidos.Single(p => p.Nome == "Camiseta").MargemPct); // custo veio do cadastro (20,00)
        Assert.Null(r.PorCategoria.Single(c => c.Categoria == RelatorioService.SemCategoria).MargemPct);
    }

    [Fact]
    public void Custo_do_item_vale_mais_que_o_do_cadastro()
    {
        var r = RelatorioProdutos.Gerar(new[] { V(1, false, 0, I(1, "Camiseta", "M", 1, 5000, 3000)) }, Cadastro, Nov);
        Assert.Equal(2000, r.MaisVendidos[0].LucroCentavos);
    }

    [Fact]
    public void Por_categoria_e_por_tamanho()
    {
        var vendas = new[]
        {
            V(1, false, 0, I(1, "Camiseta", "M", 3, 5000, 2000), I(2, "Boné", Tamanhos.SemTamanho, 2, 4000, 1000)),
            V(2, false, 0, I(1, "Camiseta", "M", 1, 5000, 2000), I(1, "Camiseta", "G", 1, 5000, 2000), I(3, "Meia", "M", 5, 1000))
        };
        var r = RelatorioProdutos.Gerar(vendas, Cadastro, Nov);

        Assert.Equal(new[] { "Roupas", "Acessórios", RelatorioService.SemCategoria }, r.PorCategoria.Select(c => c.Categoria).ToArray());
        Assert.Equal(25000, r.PorCategoria[0].ValorCentavos);
        Assert.Equal(5, r.PorCategoria[0].Pecas);

        var m = r.PorTamanho.Single(t => t.Tamanho == "M");
        Assert.Equal(9, m.Pecas); // 3 + 1 + 5
        Assert.Equal("", r.PorTamanho.Single(t => t.Pecas == 2).Tamanho); // "Sem tamanho" sai vazio
        var camM = r.PorProdutoETamanho.Single(t => t.ProdutoId == 1 && t.Tamanho == "M");
        Assert.Equal((4, 20000L), (camM.Pecas, camM.ValorCentavos));
    }

    [Fact]
    public void Curva_abc_classifica_pelo_acumulado()
    {
        // 70% / 20% / 6% / 4%: A, B (acumulado antes = 70 < 80 -> A tambem para o 2o? ver regra: antes < 80 = A)
        var itens = new[] { I(1, "P1", "U", 1, 7000), I(2, "P2", "U", 1, 2000), I(3, "P3", "U", 1, 600), I(4, "P4", "U", 1, 400) };
        var cad = new[] { P(1, "P1", null), P(2, "P2", null), P(3, "P3", null), P(4, "P4", null) };
        var r = RelatorioProdutos.Gerar(new[] { V(1, false, 0, itens) }, cad, Nov);
        Assert.Equal("AABC", new string(r.MaisVendidos.OrderByDescending(p => p.ValorCentavos).Select(p => p.ClasseAbc).ToArray()));
    }

    [Fact]
    public void Periodo_vazio_nao_quebra_e_ignora_vendas_fora()
    {
        var r = RelatorioProdutos.Gerar(new[] { new Venda { DataHora = new DateTime(2026, 10, 31), Itens = { I(1, "Camiseta", "M", 1, 5000) } } }, Cadastro, Nov);
        Assert.Equal(0, r.FaturamentoCentavos);
        Assert.Empty(r.MaisVendidos);
        Assert.Empty(r.PorCategoria);
    }

    [Fact]
    public void Parados_lista_quem_tem_estoque_e_esta_sem_venda_ha_dias_ou_nunca_vendeu()
    {
        var hoje = new DateTime(2026, 11, 19);
        var produtos = new[]
        {
            P(1, "Recente", "A", 1000, estoque: 5),                     // vendeu há 10 dias: fora
            P(2, "Parado 70 dias", "A", 2000, estoque: 3),               // custo parado 6.000
            P(3, "Nunca vendeu", "B", 500, estoque: 10),                 // custo parado 5.000
            P(4, "Sem custo", "B", 0, estoque: 4),                       // custo desconhecido
            P(5, "Sem estoque", "A", 1000, estoque: 0),                  // fora
            P(6, "Desativado", "A", 1000, estoque: 9, ativo: false),     // fora
            new Produto { Id = 7, Nome = "Serviço", EstoqueAtual = 5, VenderSemBaixa = true, PrecoCustoCentavos = 100 }, // fora
            P(8, "Exato 60 dias", "A", 100, estoque: 1)                  // 60 dias: entra
        };
        var ultima = new Dictionary<int, DateTime>
        {
            [1] = hoje.AddDays(-10), [2] = hoje.AddDays(-70), [4] = hoje.AddDays(-200), [8] = hoje.AddDays(-60)
        };

        var lista = RelatorioProdutos.Parados(produtos, ultima, hoje, 60);

        Assert.Equal(new[] { "Parado 70 dias", "Nunca vendeu", "Exato 60 dias", "Sem custo" }, lista.Select(p => p.Nome).ToArray());
        Assert.Equal(6000, lista[0].CustoParadoCentavos);
        Assert.Equal(70, lista[0].DiasSemVenda);
        Assert.Null(lista[1].UltimaVenda);
        Assert.Null(lista[1].DiasSemVenda);
        Assert.Null(lista.Single(p => p.Nome == "Sem custo").CustoParadoCentavos);
        Assert.Equal(11100, RelatorioProdutos.CustoTotalParado(lista));
        Assert.Null(RelatorioProdutos.CustoTotalParado(lista.Where(p => p.Nome == "Sem custo")));
    }

    [Fact]
    public void Csvs_tem_cabecalho_e_numeros_no_formato_brasileiro()
    {
        var vendas = new[] { V(1, false, 0, I(1, "Camiseta; Azul", "M", 2, 5000, 2000)) };
        var r = RelatorioProdutos.Gerar(vendas, Cadastro, Nov);

        var mais = RelatorioProdutos.MaisVendidosCsv(r);
        Assert.StartsWith("Posição;Produto;Referência;Categoria;Quantidade;Faturamento;% do faturamento;Vendas;Lucro;Margem (%);Classe ABC\r\n", mais);
        Assert.Contains("1;\"Camiseta; Azul\";CAM1;Roupas;2;100,00;100,0;1;60,00;60,0;A", mais);
        Assert.Contains("Roupas;2;100,00;100,0;60,00;60,0", RelatorioProdutos.PorCategoriaCsv(r));
        Assert.Contains("\"Camiseta; Azul\";M;2;100,00", RelatorioProdutos.PorTamanhoCsv(r));

        var parados = RelatorioProdutos.ParadosCsv(RelatorioProdutos.Parados(
            new[] { P(3, "Meia", null, 0, estoque: 2) }, new Dictionary<int, DateTime>(), new DateTime(2026, 11, 19)));
        Assert.Contains("Meia;;Sem categoria;2;nunca vendeu;;sem custo", parados);
    }

    [Fact]
    public async Task Servico_le_do_banco_ignora_canceladas_e_acha_a_ultima_venda()
    {
        using var banco = new BancoDeTeste();
        await using (var db = banco.CriarContexto())
        {
            var camiseta = P(0, "Camiseta", "Roupas", 2000, estoque: 8); camiseta.Id = 0;
            var parado = P(0, "Calça parada", "Roupas", 3000, estoque: 2); parado.Id = 0;
            db.Produtos.AddRange(camiseta, parado);
            await db.SaveChangesAsync();
            var v1 = V(1, false, 0, I(camiseta.Id, "Camiseta", "M", 2, 5000)); v1.Id = 0;
            var v2 = V(5, true, 0, I(parado.Id, "Calça parada", "M", 1, 9000)); v2.Id = 0; // cancelada: não conta como venda
            db.Vendas.AddRange(v1, v2);
            await db.SaveChangesAsync();
        }

        var svc = new RelatorioProdutosService(banco.Fabrica);
        var r = await svc.GerarAsync(Nov);
        Assert.Single(r.MaisVendidos);
        Assert.Equal(10000, r.FaturamentoCentavos);

        var parados = await svc.ParadosAsync(new DateTime(2026, 11, 19), 10);
        Assert.Equal(new[] { "Calça parada", "Camiseta" }, parados.OrderByDescending(p => p.DiasSemVenda ?? int.MaxValue).Select(p => p.Nome).ToArray());
        Assert.Equal(18, parados.Single(p => p.Nome == "Camiseta").DiasSemVenda);
        Assert.Null(parados.Single(p => p.Nome == "Calça parada").UltimaVenda);
    }
}
