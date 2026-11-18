using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;
using static NexusLS.Core.Services.ResumoVendas;

namespace NexusLS.Tests;

public sealed class ResumoVendasTests : IDisposable
{
    // 2026-10-28 é quarta-feira
    private static readonly DateTime Hoje = new(2026, 10, 28);
    private static readonly DesempenhoVendas.Intervalo Periodo = new(new DateTime(2026, 10, 22), Hoje); // 7 dias; anterior: 15 a 21/10
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private static Venda V(int id, DateTime quando, long total, FormaPagamento forma = FormaPagamento.Pix, string? operador = "Ana",
        long desconto = 0, bool cancelada = false, params (int produto, string nome, string tam, int qtd, long totalItem)[] itens) => new()
    {
        Id = id, DataHora = quando, TotalCentavos = total, SubtotalCentavos = total + desconto, DescontoCentavos = desconto,
        FormaPagamento = forma, Operador = operador, Cancelada = cancelada,
        Itens = itens.Select(i => new ItemVenda { ProdutoId = i.produto, NomeProduto = i.nome, Tamanho = i.tam, Quantidade = i.qtd, TotalCentavos = i.totalItem }).ToList()
    };

    private static List<Venda> Cenario() => new()
    {
        V(1, Hoje.AddHours(10), 10000, FormaPagamento.Pix, "Ana", 0, false, (1, "Camiseta Azul", "M", 2, 10000)),
        V(2, Hoje.AddHours(10).AddMinutes(30), 9000, FormaPagamento.Dinheiro, "Bia", 1000, false, (1, "Camiseta Azul", "G", 1, 5000), (2, "Boné", "U", 1, 5000)),
        V(3, Hoje.AddDays(-1).AddHours(15), 3000, FormaPagamento.CartaoDebito, "Ana", 0, false, (2, "Boné", "U", 1, 3000)),
        V(4, Hoje.AddHours(11), 99999, FormaPagamento.Pix, "Ana", 0, true, (1, "Camiseta Azul", "M", 9, 99999)),   // cancelada
        V(5, Hoje.AddDays(-10).AddHours(10), 11000, FormaPagamento.Pix, "Ana", 0, false, (1, "Camiseta Azul", "M", 1, 11000)), // período anterior
        V(6, Hoje.AddDays(-30), 50000, FormaPagamento.Pix, "Ana", 0, false, (1, "Camiseta Azul", "M", 5, 50000))      // fora de tudo
    };

    [Fact]
    public void Totais_comparacao_descontos_e_canceladas_fora_do_total()
    {
        var r = Gerar(Cenario(), Periodo);

        Assert.Equal(22000, r.Total.Atual);              // 100 + 90 + 30; a cancelada de 999,99 fica fora
        Assert.Equal(11000, r.Total.Anterior);
        Assert.Equal(100m, r.Total.VariacaoPct);
        Assert.Equal(3, r.Vendas.Atual);
        Assert.Equal(1, r.Vendas.Anterior);
        Assert.Equal(7333, r.TicketMedio.Atual);         // 220,00 / 3 arredondado
        Assert.Equal(5, r.Pecas.Atual);                  // 2 + (1 + 1) + 1
        Assert.Equal(1000, r.DescontosCentavos);
        Assert.Equal(1, r.Canceladas);
        Assert.Equal(99999, r.CanceladasCentavos);
    }

    [Fact]
    public void Sem_base_anterior_a_variacao_e_nula()
    {
        var r = Gerar(new[] { Cenario()[0] }, Periodo);

        Assert.Null(r.Total.VariacaoPct);
        Assert.Equal(0, r.Total.Anterior);
    }

    [Fact]
    public void Forma_de_pagamento_dia_da_semana_e_horario()
    {
        var r = Gerar(Cenario(), Periodo);

        Assert.Equal(FormaPagamento.Pix, r.PorForma[0].Forma);
        Assert.Equal(10000, r.PorForma[0].ValorCentavos);
        Assert.Equal(3, r.PorForma.Count);
        Assert.Equal(DayOfWeek.Wednesday, r.MelhoresDias[0].Dia);   // 190,00 na quarta
        Assert.Equal(19000, r.MelhoresDias[0].ValorCentavos);
        Assert.Equal(DayOfWeek.Tuesday, r.MelhoresDias[1].Dia);
        Assert.Equal(7, r.PorDiaSemana.Count);
        Assert.Equal(10, r.MelhoresHorarios[0].Hora);
        Assert.Equal(2, r.MelhoresHorarios[0].Vendas);
        Assert.Equal(15, r.MelhoresHorarios[1].Hora);
    }

    [Fact]
    public void Mais_vendidos_por_quantidade_e_por_valor_e_por_vendedor()
    {
        var r = Gerar(Cenario(), Periodo);

        Assert.Equal("Camiseta Azul", r.MaisVendidosPorQuantidade[0].Nome);
        Assert.Equal(3, r.MaisVendidosPorQuantidade[0].Pecas);
        Assert.Equal("Boné", r.MaisVendidosPorQuantidade[1].Nome);
        Assert.Equal("Camiseta Azul", r.MaisVendidosPorValor[0].Nome);
        Assert.Equal(2, r.MaisVendidosPorQuantidade.Count);

        Assert.NotNull(r.PorVendedor);
        Assert.Equal("Ana", r.PorVendedor![0].Vendedor);
        Assert.Equal(13000, r.PorVendedor[0].ValorCentavos);
        Assert.Equal("Bia", r.PorVendedor[1].Vendedor);
    }

    [Fact]
    public void Sem_vendedor_identificado_nao_ha_quebra_por_vendedor()
    {
        var r = Gerar(new[] { V(1, Hoje.AddHours(9), 5000, operador: "Caixa Geral") }, Periodo);

        Assert.Null(r.PorVendedor);
    }

    [Fact]
    public void Pesquisa_por_nome_sem_acento_e_sem_diferenciar_maiusculas_agrupa_por_tamanho()
    {
        var cad = new[] { new ProdutoCadastro(1, "Camiseta Azul", "REF-100", "00100", "7890000000017"), new ProdutoCadastro(2, "Boné", "REF-200", null, null) };

        var r = Pesquisar(Cenario(), cad, Periodo, "  BONE ");

        Assert.Equal(2, r.Unidades);
        Assert.Equal(1, r.ProdutosEncontrados);
        Assert.Equal(2, r.Vendas.Count);
        Assert.Equal(5000 - 500 + 3000, r.ValorCentavos);   // o desconto de 10,00 da venda 2 é rateado: boné fica com 45,00
        Assert.Equal(new DateTime(2026, 10, 27, 15, 0, 0), r.Vendas[0].DataHora);
        Assert.Equal(3, r.Vendas[0].VendaId);
    }

    [Fact]
    public void Pesquisa_por_referencia_codigo_interno_e_codigo_de_barras()
    {
        var cad = new[] { new ProdutoCadastro(1, "Camiseta Azul", "REF-100", "00100", "7890000000017"), new ProdutoCadastro(2, "Boné", "REF-200", null, null) };

        foreach (var busca in new[] { "ref-100", "00100", "7890000000017" })
        {
            var r = Pesquisar(Cenario(), cad, Periodo, busca);
            Assert.Equal(3, r.Unidades);               // cancelada e período anterior ficam de fora
            Assert.Equal(2, r.Variacoes.Count);        // M e G
            Assert.Equal("M", r.Variacoes[0].Tamanho);
            Assert.Equal(2, r.Variacoes[0].Unidades);
            Assert.Equal("G", r.Variacoes[1].Tamanho);
        }
    }

    [Fact]
    public void Pesquisa_vazia_ou_sem_resultado_nao_acha_nada()
    {
        var cad = Array.Empty<ProdutoCadastro>();

        Assert.False(Pesquisar(Cenario(), cad, Periodo, "").Achou);
        Assert.False(Pesquisar(Cenario(), cad, Periodo, "inexistente").Achou);
    }

    [Fact]
    public void Normalizar_tira_acentos_e_caixa()
    {
        Assert.Equal("calcao ninja", Normalizar("  CALÇÃO Ninja "));
        Assert.Equal("", Normalizar(null));
    }

    [Fact]
    public async Task Servico_le_o_banco_e_busca_pela_referencia_cadastrada()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Jaqueta Jeans", "7891234567895", 20000, 8000, 1, "JQ-77"), 10);
        await using (var db = _banco.CriarContexto())
        {
            db.Vendas.Add(new Venda
            {
                DataHora = Hoje.AddHours(14), TotalCentavos = 20000, SubtotalCentavos = 20000, FormaPagamento = FormaPagamento.Pix, Operador = "Ana",
                Itens = { new ItemVenda { ProdutoId = p.Id, NomeProduto = "Jaqueta Jeans", Quantidade = 1, PrecoUnitarioCentavos = 20000, TotalCentavos = 20000 } }
            });
            await db.SaveChangesAsync();
        }

        var (resumo, busca) = await new ResumoVendasService(_banco.Fabrica).GerarAsync(Periodo, "jq-77");

        Assert.Equal(20000, resumo.Total.Atual);
        Assert.Equal(1, busca.Unidades);
        Assert.Equal(20000, busca.ValorCentavos);
    }

    [Fact]
    public void Csv_do_resumo_tem_BOM_ponto_e_virgula_acentos_e_a_pesquisa()
    {
        var cad = new[] { new ProdutoCadastro(1, "Camiseta Azul", "REF-100", null, null) };
        var r = Gerar(Cenario(), Periodo);
        var busca = Pesquisar(Cenario(), cad, Periodo, "ref-100");

        var csv = ExportadorCsv.ResumoDeVendas(r, busca);

        Assert.StartsWith("Seção;Item;Vendas;Peças;Valor;Variação (%)", csv);
        Assert.Contains("Resumo;Total vendido;;;220,00;100,0", csv);
        Assert.Contains("Canceladas (fora do total);Vendas canceladas;1;;999,99;", csv);
        Assert.Contains("Pesquisa por variação;M;;2;", csv);

        var arquivo = Path.Combine(Path.GetTempPath(), "resumo_" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            ExportadorCsv.Salvar(arquivo, csv);
            var bytes = File.ReadAllBytes(arquivo);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
            Assert.Contains("Seção", Encoding.UTF8.GetString(bytes));
        }
        finally { File.Delete(arquivo); }
    }
}
