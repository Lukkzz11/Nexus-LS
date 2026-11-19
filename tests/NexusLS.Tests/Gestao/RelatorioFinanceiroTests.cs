using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests.Gestao;

public class RelatorioFinanceiroTests
{
    private static readonly DesempenhoVendas.Intervalo Nov = new(new DateTime(2026, 11, 1), new DateTime(2026, 11, 3));

    private static Venda V(int dia, long total, FormaPagamento forma = FormaPagamento.Dinheiro, bool cancelada = false, long vale = 0, int hora = 10) => new()
    {
        Id = dia * 100 + (int)(total % 97), DataHora = new DateTime(2026, 11, dia, hora, 0, 0), TotalCentavos = total, SubtotalCentavos = total,
        FormaPagamento = forma, Cancelada = cancelada, ValeTrocaCentavos = vale
    };

    private static LancamentoEmpresa L(int dia, long valor, TipoMovimentoEmpresa tipo, string cat = "Outros", FormaPagamento? forma = null) => new()
    {
        Data = new DateTime(2026, 11, dia), ValorCentavos = valor, Tipo = tipo, Categoria = cat, Descricao = "x", Forma = forma
    };

    [Fact]
    public void Soma_vendas_entradas_saidas_e_saldo_do_periodo()
    {
        var r = RelatorioFinanceiro.Gerar(
            new[] { V(1, 10000), V(2, 5000, FormaPagamento.Pix), V(3, 2000, cancelada: true), V(4, 99999) }, // dia 4 fora; cancelada fora
            new[] { L(1, 3000, TipoMovimentoEmpresa.Entrada), L(2, 4000, TipoMovimentoEmpresa.Saida, "Aluguel"), L(5, 777, TipoMovimentoEmpresa.Saida) },
            null, Nov);

        Assert.Equal(2, r.NumeroDeVendas);
        Assert.Equal(15000, r.VendasCentavos);
        Assert.Equal(3000, r.EntradasLancadasCentavos);
        Assert.Equal(4000, r.SaidasLancadasCentavos);
        Assert.Equal(18000, r.TotalEntradasCentavos);
        Assert.Equal(4000, r.TotalSaidasCentavos);
        Assert.Equal(14000, r.SaldoCentavos);
    }

    [Fact]
    public void Periodo_inclui_o_dia_final_inteiro()
    {
        var r = RelatorioFinanceiro.Gerar(new[] { V(3, 100, hora: 23) }, Array.Empty<LancamentoEmpresa>(), null, Nov);
        Assert.Equal(100, r.VendasCentavos);
    }

    [Fact]
    public void Por_forma_de_pagamento_fecha_com_o_faturamento_inclusive_vale_troca()
    {
        var r = RelatorioFinanceiro.Gerar(
            new[] { V(1, 10000, FormaPagamento.Dinheiro), V(1, 6000, FormaPagamento.Pix, vale: 1500), V(2, 3000, FormaPagamento.ValeTroca, vale: 3000) },
            new[] { L(1, 500, TipoMovimentoEmpresa.Saida, forma: FormaPagamento.Dinheiro), L(2, 800, TipoMovimentoEmpresa.Entrada) },
            null, Nov);

        long Vendas(FormaPagamento? f) => r.PorFormaPagamento.Single(x => x.Forma == f).VendasCentavos;
        Assert.Equal(10000, Vendas(FormaPagamento.Dinheiro));
        Assert.Equal(4500, Vendas(FormaPagamento.Pix));
        Assert.Equal(4500, Vendas(FormaPagamento.ValeTroca)); // 1.500 de parte da venda Pix + 3.000 da venda paga só com vale
        Assert.Equal(r.VendasCentavos, r.PorFormaPagamento.Sum(x => x.VendasCentavos));
        Assert.Equal(500, r.PorFormaPagamento.Single(x => x.Forma == FormaPagamento.Dinheiro).SaidasCentavos);
        var semForma = r.PorFormaPagamento.Single(x => x.Forma is null);
        Assert.Equal(800, semForma.EntradasCentavos);
        Assert.Equal("Sem forma informada", semForma.Nome);
    }

    [Fact]
    public void Por_dia_traz_todos_os_dias_com_saldo_acumulado()
    {
        var r = RelatorioFinanceiro.Gerar(
            new[] { V(1, 1000), V(3, 500) },
            new[] { L(1, 200, TipoMovimentoEmpresa.Saida), L(3, 100, TipoMovimentoEmpresa.Entrada) }, null, Nov);

        Assert.Equal(3, r.PorDiaDoPeriodo.Count);
        Assert.Equal(new long[] { 800, 0, 600 }, r.PorDiaDoPeriodo.Select(d => d.SaldoDoDiaCentavos).ToArray());
        Assert.Equal(new long[] { 800, 800, 1400 }, r.PorDiaDoPeriodo.Select(d => d.SaldoAcumuladoCentavos).ToArray());
        Assert.Equal(r.SaldoCentavos, r.PorDiaDoPeriodo[^1].SaldoAcumuladoCentavos);
    }

    [Fact]
    public void Categorias_agrupam_sem_diferenciar_maiusculas_e_acentos()
    {
        var r = RelatorioFinanceiro.Gerar(Array.Empty<Venda>(),
            new[] { L(1, 100, TipoMovimentoEmpresa.Saida, "Água"), L(2, 250, TipoMovimentoEmpresa.Saida, "agua"), L(2, 900, TipoMovimentoEmpresa.Saida, "Aluguel"),
                    L(1, 50, TipoMovimentoEmpresa.Entrada, "Aporte") }, null, Nov);
        Assert.Equal(new[] { "Aluguel", "Água" }, r.SaidasPorCategoria.Select(c => c.Categoria).ToArray());
        Assert.Equal(350, r.SaidasPorCategoria[1].ValorCentavos);
        Assert.Equal(2, r.SaidasPorCategoria[1].Lancamentos);
        Assert.Single(r.EntradasPorCategoria);
    }

    [Fact]
    public void Devolucoes_dao_faturamento_liquido_e_ignoram_as_canceladas()
    {
        var devs = new[]
        {
            new Devolucao { DataHora = new DateTime(2026, 11, 2, 9, 0, 0), ValorCentavos = 1200 },
            new Devolucao { DataHora = new DateTime(2026, 11, 2, 9, 0, 0), ValorCentavos = 700, Cancelada = true },
            new Devolucao { DataHora = new DateTime(2026, 12, 2, 9, 0, 0), ValorCentavos = 900 }
        };
        var r = RelatorioFinanceiro.Gerar(new[] { V(1, 5000) }, Array.Empty<LancamentoEmpresa>(), devs, Nov);
        Assert.Equal(1200, r.DevolucoesCentavos);
        Assert.Equal(3800, r.FaturamentoLiquidoCentavos);
        Assert.Equal(5000, r.SaldoCentavos); // devolução não mexe no saldo (saiu um vale, não dinheiro)
    }

    [Fact]
    public void Periodo_vazio_nao_quebra()
    {
        var r = RelatorioFinanceiro.Gerar(Array.Empty<Venda>(), Array.Empty<LancamentoEmpresa>(), null, Nov);
        Assert.Equal(0, r.SaldoCentavos);
        Assert.Empty(r.PorFormaPagamento);
        Assert.Equal(3, r.PorDiaDoPeriodo.Count);
    }

    [Fact]
    public void Csv_tem_cabecalho_valores_com_virgula_e_protege_formula()
    {
        var r = RelatorioFinanceiro.Gerar(new[] { V(1, 12345, FormaPagamento.Pix) },
            new[] { L(1, 1000, TipoMovimentoEmpresa.Saida, "=SOMA(A1)") }, null, Nov);
        var csv = RelatorioFinanceiro.ParaCsv(r);
        Assert.StartsWith("Seção;Item;Vendas;Entradas;Saídas;Saldo\r\n", csv);
        Assert.Contains("Resumo;Vendas (faturamento);123,45;;;", csv);
        Assert.Contains("Forma de pagamento;Pix;123,45", csv);
        Assert.Contains("Por dia;01/11/2026;123,45;0,00;10,00;113,45", csv);
        Assert.Contains("'=SOMA(A1)", csv);
    }

    // ---------------------------------------------------------------- com o banco

    [Fact]
    public async Task Venda_enviada_a_pedidos_continua_no_faturamento_e_cancelada_nao_entra()
    {
        using var banco = new BancoDeTeste();
        await using (var db = banco.CriarContexto())
        {
            var enviada = V(2, 7000, FormaPagamento.APrazo); enviada.Id = 0;
            var comum = V(2, 3000); comum.Id = 0;
            var cancelada = V(2, 9000, cancelada: true); cancelada.Id = 0;
            db.Vendas.AddRange(enviada, comum, cancelada);
            await db.SaveChangesAsync();
            db.Pedidos.Add(new Pedido { Numero = "PED-2611-0001", VendaId = enviada.Id, Origem = OrigemPedido.Balcao, TotalCentavos = 7000, CriadoEm = DateTime.Now, AtualizadoEm = DateTime.Now });
            db.LancamentosEmpresa.Add(new LancamentoEmpresa { Data = new DateTime(2026, 11, 2), Tipo = TipoMovimentoEmpresa.Saida, Categoria = "Frete", Descricao = "Frete", ValorCentavos = 1000 });
            await db.SaveChangesAsync();
        }

        var r = await new RelatorioFinanceiroService(banco.Fabrica).GerarAsync(Nov);

        Assert.Equal(2, r.NumeroDeVendas);
        Assert.Equal(10000, r.VendasCentavos);
        Assert.Equal(9000, r.SaldoCentavos);
    }
}
