using NexusLS.Core.Financeiro;

namespace NexusLS.Tests;

public class MotorFinanceiroTests
{
    private static readonly DateTime Hoje = new(2026, 10, 20);
    private const int Caixa = 1, Banco = 2, Cofre = 3;

    private static readonly ContaFin[] Contas =
    {
        new(Caixa, "Gaveta", TipoContaFin.Caixa, 10000),
        new(Banco, "Banco", TipoContaFin.Banco, 50000, Padrao: true),
        new(Cofre, "Cofre", TipoContaFin.Caixa, 0)
    };

    private static LancFin Entrada(string id, long valor, int? conta, ClasseFin classe = ClasseFin.Venda,
        SituacaoFin sit = SituacaoFin.Realizado, DateTime? data = null, OrigemFin origem = OrigemFin.Venda, DateTime? prevista = null) =>
        new(id, origem, classe, SentidoFin.Entrada, sit, valor, data ?? Hoje, ContaId: conta, DataPrevista: prevista);

    private static LancFin Saida(string id, long valor, int? conta, ClasseFin classe = ClasseFin.Despesa,
        SituacaoFin sit = SituacaoFin.Realizado, DateTime? data = null, string? categoria = null, OrigemFin origem = OrigemFin.Lancamento) =>
        new(id, origem, classe, SentidoFin.Saida, sit, valor, data ?? Hoje, ContaId: conta, Categoria: categoria);

    private static PosicaoFinanceira Calcular(IEnumerable<LancFin> lancs, IEnumerable<VendaFin>? vendas = null, ContaFin[]? contas = null) =>
        MotorFinanceiro.Calcular(new EntradaMotor(Hoje, contas ?? Contas, lancs.ToList(), (vendas ?? Array.Empty<VendaFin>()).ToList(),
            new DateTime(2026, 10, 1), Hoje));

    private static long Disponivel(PosicaoFinanceira p, int conta) => p.Contas.Single(c => c.Id == conta).DisponivelCentavos;

    // ---------- saldos ----------

    [Fact]
    public void Saldo_da_conta_e_saldo_inicial_mais_entradas_menos_saidas()
    {
        var p = Calcular(new[]
        {
            Entrada("v1", 20000, Caixa),
            Saida("d1", 3000, Caixa, categoria: "Água"),
            Entrada("v2", 8000, Banco, origem: OrigemFin.Venda)
        });

        Assert.Equal(10000 + 20000 - 3000, Disponivel(p, Caixa));
        Assert.Equal(50000 + 8000, Disponivel(p, Banco));
        Assert.Equal(27000, p.DinheiroFisicoCentavos);
        Assert.Equal(58000, p.BancosCentavos);
        Assert.Equal(85000, p.SaldoDisponivelCentavos);
    }

    [Fact]
    public void Previsto_e_em_compensacao_nao_mexem_no_saldo_disponivel()
    {
        var p = Calcular(new[]
        {
            Entrada("cartao", 30000, null, sit: SituacaoFin.EmCompensacao, prevista: Hoje.AddDays(30)),
            Entrada("titulo", 15000, null, ClasseFin.RecebimentoCliente, SituacaoFin.Previsto, Hoje.AddDays(5), OrigemFin.Titulo),
            Saida("conta", 9000, null, sit: SituacaoFin.Previsto, data: Hoje.AddDays(3))
        });

        Assert.Equal(60000, p.SaldoDisponivelCentavos); // só os saldos iniciais
        Assert.Empty(p.Avisos); // previsto sem conta não é erro
    }

    [Fact]
    public void Transferencia_move_entre_contas_sem_ser_receita_nem_despesa()
    {
        var transf = new LancFin("t1", OrigemFin.Transferencia, ClasseFin.Transferencia, SentidoFin.Transferencia,
            SituacaoFin.Realizado, 4000, Hoje, ContaOrigemId: Caixa, ContaDestinoId: Banco);

        var p = Calcular(new[] { transf });

        Assert.Equal(6000, Disponivel(p, Caixa));
        Assert.Equal(54000, Disponivel(p, Banco));
        Assert.Equal(60000, p.SaldoDisponivelCentavos); // o total não muda
        Assert.Equal(0, p.Periodo.EntradasRealizadasCentavos);
        Assert.Equal(0, p.Periodo.SaidasRealizadasCentavos);
        Assert.Equal(0, p.Periodo.ResultadoCentavos);
    }

    [Fact]
    public void Transferencia_para_conta_inexistente_vira_aviso_e_nao_mexe_em_saldo()
    {
        var transf = new LancFin("t1", OrigemFin.Transferencia, ClasseFin.Transferencia, SentidoFin.Transferencia,
            SituacaoFin.Realizado, 4000, Hoje, ContaOrigemId: Caixa, ContaDestinoId: 99);

        var p = Calcular(new[] { transf });

        Assert.Equal(10000, Disponivel(p, Caixa));
        Assert.Contains(p.Avisos, a => a.Codigo == "sem_conta");
    }

    [Fact]
    public void Movimento_anterior_ao_saldo_inicial_da_conta_nao_conta_de_novo()
    {
        var contas = new[] { new ContaFin(Caixa, "Gaveta", TipoContaFin.Caixa, 10000, DataSaldoInicial: new DateTime(2026, 10, 10)) };

        var p = Calcular(new[]
        {
            Entrada("antiga", 5000, Caixa, data: new DateTime(2026, 10, 9)),  // já está dentro do saldo inicial
            Entrada("nova", 2000, Caixa, data: new DateTime(2026, 10, 10))
        }, contas: contas);

        Assert.Equal(12000, Disponivel(p, Caixa));
    }

    [Fact]
    public void Realizado_sem_conta_fica_fora_do_saldo_e_gera_aviso()
    {
        var p = Calcular(new[] { Entrada("pix", 7000, null) });

        Assert.Equal(60000, p.SaldoDisponivelCentavos);
        var aviso = Assert.Single(p.Avisos);
        Assert.Equal("sem_conta", aviso.Codigo);
        Assert.Contains("R$ 70,00", aviso.Mensagem);
    }

    [Fact]
    public void Mesmo_id_nunca_conta_duas_vezes()
    {
        var p = Calcular(new[] { Entrada("v1", 5000, Caixa), Entrada("v1", 5000, Caixa) });

        Assert.Equal(15000, Disponivel(p, Caixa));
        Assert.Contains(p.Avisos, a => a.Codigo == "duplicado");
    }

    // ---------- a receber, a pagar, em compensação ----------

    [Fact]
    public void A_receber_separa_em_dia_vencido_e_em_compensacao()
    {
        var p = Calcular(new[]
        {
            Entrada("t1", 10000, null, ClasseFin.RecebimentoCliente, SituacaoFin.Previsto, Hoje.AddDays(10), OrigemFin.Titulo),
            Entrada("t2", 4000, null, ClasseFin.RecebimentoCliente, SituacaoFin.Previsto, Hoje.AddDays(-3), OrigemFin.Titulo),
            Entrada("ch", 6000, null, sit: SituacaoFin.EmCompensacao, origem: OrigemFin.Cheque, prevista: Hoje.AddDays(15)),
            Entrada("cart", 9000, null, sit: SituacaoFin.EmCompensacao, origem: OrigemFin.Venda, prevista: Hoje.AddDays(30))
        });

        Assert.Equal(10000, p.AReceber.EmDiaCentavos);
        Assert.Equal(4000, p.AReceber.VencidoCentavos);
        Assert.Equal(15000, p.AReceber.EmCompensacaoCentavos);
        Assert.Equal(29000, p.AReceber.TotalCentavos);
        Assert.Equal(4, p.AReceber.Quantidade);
        Assert.Equal(6000, p.EmCompensacao.ChequesCentavos);
        Assert.Equal(9000, p.EmCompensacao.CartoesCentavos);
    }

    [Fact]
    public void A_pagar_separa_em_dia_vencido_e_cheque_emitido()
    {
        var p = Calcular(new[]
        {
            Saida("c1", 8000, null, sit: SituacaoFin.Previsto, data: Hoje.AddDays(2)),
            Saida("c2", 2000, null, sit: SituacaoFin.Previsto, data: Hoje.AddDays(-1)),
            Saida("ch", 5000, null, sit: SituacaoFin.EmCompensacao, origem: OrigemFin.Cheque)
        });

        Assert.Equal(8000, p.APagar.EmDiaCentavos);
        Assert.Equal(2000, p.APagar.VencidoCentavos);
        Assert.Equal(5000, p.APagar.EmCompensacaoCentavos);
        Assert.Equal(15000, p.APagar.TotalCentavos);
    }

    // ---------- projeção ----------

    [Fact]
    public void Projecao_soma_o_que_entra_e_subtrai_o_que_sai_ate_cada_data()
    {
        var p = Calcular(new[]
        {
            Entrada("t1", 10000, null, ClasseFin.RecebimentoCliente, SituacaoFin.Previsto, Hoje.AddDays(5), OrigemFin.Titulo),
            Saida("c1", 4000, null, sit: SituacaoFin.Previsto, data: Hoje.AddDays(20)),
            Entrada("cart", 9000, null, sit: SituacaoFin.EmCompensacao, prevista: Hoje.AddDays(45)),
            Saida("atrasada", 1000, null, sit: SituacaoFin.Previsto, data: Hoje.AddDays(-5))
        });

        // disponível 60.000; a conta atrasada conta já (como "hoje")
        Assert.Equal(60000 + 10000 - 1000, p.Projecao.PorDias[7]);
        Assert.Equal(60000 + 10000 - 1000 - 4000, p.Projecao.PorDias[30]);
        Assert.Equal(60000 + 10000 + 9000 - 1000 - 4000, p.Projecao.PorDias[60]);
        Assert.Equal(p.Projecao.PorDias[90], p.Projecao.TotalCentavos);
    }

    // ---------- resultado do período ----------

    [Fact]
    public void Resultado_e_lucro_bruto_mais_outras_receitas_menos_despesas()
    {
        var vendas = new[] { new VendaFin("v1", Hoje, 100000, 40000), new VendaFin("v2", Hoje.AddDays(-1), 50000, 20000) };
        var p = Calcular(new[]
        {
            Entrada("rec", 7000, Banco, ClasseFin.ReceitaOutra, origem: OrigemFin.Lancamento),
            Saida("luz", 12000, Banco, categoria: "Energia"),
            Saida("taxa", 3000, Banco, ClasseFin.TarifaCartao, origem: OrigemFin.Cartao)
        }, vendas);

        Assert.Equal(150000, p.Periodo.ReceitaBrutaCentavos);
        Assert.Equal(60000, p.Periodo.CustoMercadoriasCentavos);
        Assert.Equal(90000, p.Periodo.LucroBrutoCentavos);
        Assert.Equal(7000, p.Periodo.OutrasReceitasCentavos);
        Assert.Equal(15000, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(90000 + 7000 - 15000, p.Periodo.ResultadoCentavos);
        Assert.Equal(12000, p.Periodo.DespesasPorCategoria["Energia"]);
        Assert.Equal(3000, p.Periodo.DespesasPorCategoria["Taxas de cartão"]);
    }

    [Fact]
    public void Compra_de_mercadoria_sai_do_caixa_mas_nao_e_despesa_do_periodo()
    {
        // O custo já entra no lucro bruto quando a peça é vendida; contar a compra também seria descontar duas vezes.
        var p = Calcular(new[] { Saida("compra", 80000, Banco, ClasseFin.CompraEstoque, categoria: "Fornecedores") },
            new[] { new VendaFin("v1", Hoje, 100000, 40000) });

        Assert.Equal(50000 - 80000, Disponivel(p, Banco)); // o dinheiro saiu da conta
        Assert.Equal(0, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(60000, p.Periodo.ResultadoCentavos);
        Assert.Equal(80000, p.Periodo.SaidasRealizadasCentavos);
    }

    [Fact]
    public void Recebimento_de_cliente_aumenta_o_saldo_mas_nao_e_receita_de_novo()
    {
        var p = Calcular(new[] { Entrada("rec1", 5000, Caixa, ClasseFin.RecebimentoCliente, origem: OrigemFin.RecebimentoCliente) });

        Assert.Equal(15000, Disponivel(p, Caixa));
        Assert.Equal(0, p.Periodo.ResultadoCentavos);
        Assert.Equal(0, p.Periodo.OutrasReceitasCentavos);
        Assert.Equal(5000, p.Periodo.EntradasRealizadasCentavos);
    }

    [Fact]
    public void Retirada_e_reforco_do_caixa_mexem_no_saldo_mas_nao_no_resultado()
    {
        var p = Calcular(new[]
        {
            Saida("sangria", 20000, Caixa, ClasseFin.Retirada, origem: OrigemFin.Caixa),
            Entrada("reforco", 3000, Caixa, ClasseFin.Reforco, origem: OrigemFin.Caixa)
        });

        Assert.Equal(10000 - 20000 + 3000, Disponivel(p, Caixa));
        Assert.Equal(0, p.Periodo.ResultadoCentavos);
        Assert.Equal(0, p.Periodo.DespesasOperacionaisCentavos);
    }

    [Fact]
    public void Lancamento_fora_do_periodo_nao_entra_no_resultado_mas_entra_no_saldo()
    {
        var p = Calcular(new[]
        {
            Saida("velha", 9000, Banco, categoria: "Aluguel", data: new DateTime(2026, 9, 5))
        });

        Assert.Equal(50000 - 9000, Disponivel(p, Banco));
        Assert.Equal(0, p.Periodo.DespesasOperacionaisCentavos);
    }

    [Fact]
    public void Devolucao_reduz_a_receita_liquida()
    {
        var p = Calcular(new[] { Saida("dev", 5000, Caixa, ClasseFin.Devolucao, origem: OrigemFin.Caixa) },
            new[] { new VendaFin("v1", Hoje, 100000, 40000) });

        Assert.Equal(95000, p.Periodo.ReceitaLiquidaCentavos);
        Assert.Equal(55000, p.Periodo.LucroBrutoCentavos);
    }

    [Fact]
    public void Valores_zerados_ou_negativos_sao_ignorados()
    {
        var p = Calcular(new[] { Entrada("zero", 0, Caixa), Entrada("neg", -100, Caixa) });

        Assert.Equal(10000, Disponivel(p, Caixa));
        Assert.Empty(p.Avisos);
    }
}
