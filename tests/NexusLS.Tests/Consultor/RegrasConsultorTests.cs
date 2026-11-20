using NexusLS.Core.Consultor;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests.Consultor;

/// <summary>Cada regra do Consultor Nexus, sozinha: dispara, não dispara, nos limites e sem dados (sem dado = sem cartão).</summary>
public sealed class RegrasConsultorTests
{
    // Quinta-feira, 19/11/2026.
    private static readonly DateTime Hoje = new(2026, 11, 19, 15, 0, 0);
    private static DateTime Dia(int diasAtras) => Hoje.Date.AddDays(-diasAtras);

    /// <summary>56 dias fechados (hoje-56 a ontem). <paramref name="f"/> recebe "quantos dias atrás" (1 = ontem).</summary>
    private static List<VendasDoDia> Serie(Func<int, (int Vendas, long Total)> f) =>
        Enumerable.Range(1, 56).Reverse().Select(atras => { var (v, t) = f(atras); return new VendasDoDia(Dia(atras), v, t); }).ToList();

    private static DadosConsultor Dados() => new(Hoje);

    private static RelatorioProdutos.ProdutoParado Parado(string nome, int dias, long? custo, int estoque = 3) =>
        new(1, nome, null, "Geral", estoque, Dia(dias), dias, custo);

    // ---------------------------------------------------------------- estoque parado

    [Fact]
    public void Estoque_parado_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraEstoqueParado().Avaliar(Dados()));

    [Fact]
    public void Estoque_parado_com_pouco_custo_e_poucos_itens_nao_gera_cartao()
    {
        var d = Dados() with { Parados = [Parado("Camiseta", 90, 49_999)] };
        Assert.Null(new RegraEstoqueParado().Avaliar(d));
    }

    [Fact]
    public void Estoque_parado_com_custo_de_500_reais_gera_cartao_com_os_numeros()
    {
        var d = Dados() with { Parados = [Parado("Casaco de lã", 75, 50_000, 4), Parado("Meia", 61, 1_000, 2)] };
        var s = new RegraEstoqueParado().Avaliar(d)!;
        Assert.Equal("estoque-parado", s.Id);
        Assert.Equal(AreaConsultor.Estoque, s.Area);
        Assert.Equal(DestinoConsultor.EstoqueParado, s.Destino);
        Assert.Contains("2 produtos", s.Texto);
        Assert.Contains("6 peças", s.Texto);
        Assert.Contains(Dinheiro.Formatar(51_000), s.Texto);
        Assert.Contains("Casaco de lã", s.Texto);
    }

    [Fact]
    public void Estoque_parado_dez_itens_sem_custo_geram_cartao_sem_inventar_valor()
    {
        var d = Dados() with { Parados = Enumerable.Range(1, 10).Select(i => Parado($"Item {i}", 80, null)).ToList() };
        var s = new RegraEstoqueParado().Avaliar(d)!;
        Assert.Contains("10 produtos", s.Texto);
        Assert.Contains("sem custo cadastrado", s.Texto);
        Assert.DoesNotContain("R$", s.Texto);
    }

    [Fact]
    public void Estoque_parado_ignora_quem_vendeu_ha_menos_de_60_dias_e_quem_nunca_vendeu()
    {
        var nuncaVendeu = new RelatorioProdutos.ProdutoParado(9, "Novo", null, "Geral", 5, null, null, 900_000);
        var d = Dados() with { Parados = [Parado("Recente", 59, 900_000), nuncaVendeu] };
        Assert.Null(new RegraEstoqueParado().Avaliar(d));

        var limite = Dados() with { Parados = [Parado("No limite", 60, 50_000)] };
        Assert.NotNull(new RegraEstoqueParado().Avaliar(limite));
    }

    [Fact]
    public void Estoque_parado_so_soma_o_custo_conhecido_e_avisa_dos_que_ficaram_fora()
    {
        var d = Dados() with { Parados = [Parado("A", 70, 60_000), Parado("B", 70, null)] };
        var s = new RegraEstoqueParado().Avaliar(d)!;
        Assert.Contains(Dinheiro.Formatar(60_000), s.Texto);
        Assert.Contains("1 produto sem custo", s.Texto);
    }

    // ---------------------------------------------------------------- risco de ruptura

    private static ProdutoComGiro Giro(string nome, int estoque, int vendidas) => new(1, nome, estoque, 2, vendidas);

    [Fact]
    public void Ruptura_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraRiscoDeRuptura().Avaliar(Dados()));

    [Fact]
    public void Ruptura_com_poucas_vendas_nao_gera_cartao_nem_com_estoque_zerado()
    {
        var d = Dados() with { ProdutosComVenda = [Giro("Raro", 0, 4)] };
        Assert.Null(new RegraRiscoDeRuptura().Avaliar(d));
    }

    [Fact]
    public void Ruptura_quinze_dias_de_cobertura_ainda_nao_e_risco_mas_catorze_e()
    {
        // 30 peças em 30 dias = 1 por dia.
        Assert.Null(new RegraRiscoDeRuptura().Avaliar(Dados() with { ProdutosComVenda = [Giro("Camiseta", 15, 30)] }));
        var s = new RegraRiscoDeRuptura().Avaliar(Dados() with { ProdutosComVenda = [Giro("Camiseta", 14, 30)] })!;
        Assert.Equal(GravidadeSugestao.Atencao, s.Gravidade);
        Assert.Contains("Camiseta", s.Texto);
        Assert.Contains("14 peças em estoque", s.Texto);
        Assert.Contains("30 peças vendidas em 30 dias", s.Texto);
        Assert.Equal(DestinoConsultor.Estoque, s.Destino);
    }

    [Fact]
    public void Ruptura_produto_zerado_que_vende_e_urgente_e_vem_primeiro()
    {
        var d = Dados() with { ProdutosComVenda = [Giro("Boné", 3, 30), Giro("Calça", 0, 6)] };
        var s = new RegraRiscoDeRuptura().Avaliar(d)!;
        Assert.Equal(GravidadeSugestao.Urgente, s.Gravidade);
        Assert.StartsWith("2 produtos", s.Texto);
        Assert.True(s.Texto.IndexOf("Calça", StringComparison.Ordinal) < s.Texto.IndexOf("Boné", StringComparison.Ordinal));
        Assert.Contains("sem estoque", s.Texto);
    }

    [Fact]
    public void Ruptura_lista_so_tres_produtos_e_conta_os_outros()
    {
        var d = Dados() with { ProdutosComVenda = Enumerable.Range(1, 5).Select(i => Giro($"P{i}", i, 30)).ToList() };
        Assert.Contains("e mais 2", new RegraRiscoDeRuptura().Avaliar(d)!.Texto);
    }

    // ---------------------------------------------------------------- faturamento da semana

    private static DadosConsultor ComSemanas(long ultima, int vendasUltima, long anterior, int vendasAnterior, bool historico = true) =>
        Dados() with
        {
            PrimeiraVenda = historico ? Dia(120) : Dia(10),
            VendasPorDia = Serie(atras => atras <= 7 ? (vendasUltima / 7, ultima / 7) : atras <= 14 ? (vendasAnterior / 7, anterior / 7) : (0, 0))
        };

    [Fact]
    public void Faturamento_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraFaturamentoDaSemana().Avaliar(Dados()));

    [Fact]
    public void Faturamento_sem_historico_de_duas_semanas_nao_gera_cartao() =>
        Assert.Null(new RegraFaturamentoDaSemana().Avaliar(ComSemanas(10_000, 7, 100_000, 14, historico: false)));

    [Fact]
    public void Faturamento_com_pouca_venda_na_semana_anterior_nao_gera_cartao() =>
        Assert.Null(new RegraFaturamentoDaSemana().Avaliar(ComSemanas(1_000, 1, 100_100, 4)));

    [Fact]
    public void Faturamento_queda_de_14_por_cento_nao_gera_e_de_15_gera()
    {
        // Totais múltiplos de 7 para a soma diária fechar certinho.
        Assert.Null(new RegraFaturamentoDaSemana().Avaliar(ComSemanas(86_100, 14, 100_100, 14)));
        var s = new RegraFaturamentoDaSemana().Avaliar(ComSemanas(85_085, 14, 100_100, 14))!;
        Assert.Equal(GravidadeSugestao.Atencao, s.Gravidade);
        Assert.Equal("faturamento-da-semana", s.Id);
        Assert.Contains("15% a menos", s.Texto);
        Assert.Contains(Dinheiro.Formatar(85_085), s.Texto);
        Assert.Contains(Dinheiro.Formatar(100_100), s.Texto);
        Assert.Contains("12/11 a 18/11", s.Texto);
    }

    [Fact]
    public void Faturamento_alta_gera_cartao_informativo()
    {
        var s = new RegraFaturamentoDaSemana().Avaliar(ComSemanas(130_130, 20, 100_100, 14))!;
        Assert.Equal(GravidadeSugestao.Informacao, s.Gravidade);
        Assert.Contains("30% a mais", s.Texto);
    }

    [Fact]
    public void Faturamento_semana_sem_nenhuma_venda_depois_de_uma_boa_semana_e_queda_de_100_por_cento()
    {
        var s = new RegraFaturamentoDaSemana().Avaliar(ComSemanas(0, 0, 100_100, 14))!;
        Assert.Contains("100% a menos", s.Texto);
    }

    // ---------------------------------------------------------------- ticket médio

    private static DadosConsultor ComTicket(long ticketAtual, int vendasPorDiaAtual, long ticketAntes, int vendasPorDiaAntes, bool historico = true) =>
        Dados() with
        {
            PrimeiraVenda = historico ? Dia(120) : Dia(20),
            VendasPorDia = Serie(atras => atras <= 7 ? (vendasPorDiaAtual, vendasPorDiaAtual * ticketAtual)
                : atras <= 35 ? (vendasPorDiaAntes, vendasPorDiaAntes * ticketAntes) : (0, 0))
        };

    [Fact]
    public void Ticket_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraTicketMedioCaiu().Avaliar(Dados()));

    [Fact]
    public void Ticket_sem_cinco_semanas_de_historico_nao_gera_cartao() =>
        Assert.Null(new RegraTicketMedioCaiu().Avaliar(ComTicket(5_000, 2, 10_000, 2, historico: false)));

    [Fact]
    public void Ticket_com_poucas_vendas_nao_gera_cartao()
    {
        Assert.Null(new RegraTicketMedioCaiu().Avaliar(ComTicket(5_000, 1, 10_000, 2)));   // 7 vendas na semana
        Assert.Null(new RegraTicketMedioCaiu().Avaliar(ComTicket(5_000, 2, 10_000, 1)));   // 28 vendas antes
    }

    [Fact]
    public void Ticket_queda_de_9_por_cento_nao_gera_e_de_10_gera()
    {
        Assert.Null(new RegraTicketMedioCaiu().Avaliar(ComTicket(9_100, 2, 10_000, 2)));
        var s = new RegraTicketMedioCaiu().Avaliar(ComTicket(9_000, 2, 10_000, 2))!;
        Assert.Equal("ticket-medio-caiu", s.Id);
        Assert.Contains(Dinheiro.Formatar(9_000), s.Texto);
        Assert.Contains(Dinheiro.Formatar(10_000), s.Texto);
        Assert.Contains("10% a menos", s.Texto);
        Assert.Contains("14 vendas", s.Texto);
        Assert.Contains("56 vendas", s.Texto);
    }

    [Fact]
    public void Ticket_que_subiu_nao_gera_cartao() =>
        Assert.Null(new RegraTicketMedioCaiu().Avaliar(ComTicket(12_000, 2, 10_000, 2)));

    // ---------------------------------------------------------------- dia mais fraco

    private static DadosConsultor ComSemana(Func<DayOfWeek, long> totalDoDia, int vendasPorDiaAberto = 2) =>
        Dados() with
        {
            PrimeiraVenda = Dia(200),
            VendasPorDia = Serie(atras => { var t = totalDoDia(Dia(atras).DayOfWeek); return (t > 0 ? vendasPorDiaAberto : 0, t); })
        };

    [Fact]
    public void Dia_fraco_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraDiaMaisFraco().Avaliar(Dados()));

    [Fact]
    public void Dia_fraco_com_poucas_semanas_de_historico_nao_gera_cartao()
    {
        var d = ComSemana(dia => dia == DayOfWeek.Tuesday ? 1_000 : 10_000) with { PrimeiraVenda = Dia(30) };
        Assert.Null(new RegraDiaMaisFraco().Avaliar(d));
    }

    [Fact]
    public void Dia_fraco_gera_cartao_quando_um_dia_fica_abaixo_de_metade_da_media()
    {
        // Terça 1.000; os outros seis dias 10.000. Média dos 7 = 8.714,29; metade = 4.357,14.
        var s = new RegraDiaMaisFraco().Avaliar(ComSemana(dia => dia == DayOfWeek.Tuesday ? 1_000 : 10_000))!;
        Assert.Equal("dia-mais-fraco", s.Id);
        Assert.Contains("terça-feira", s.Texto);
        Assert.Contains(Dinheiro.Formatar(1_000), s.Texto);
        Assert.Equal(GravidadeSugestao.Informacao, s.Gravidade);
    }

    [Fact]
    public void Dia_fraco_nao_gera_cartao_quando_os_dias_sao_parecidos() =>
        Assert.Null(new RegraDiaMaisFraco().Avaliar(ComSemana(dia => dia == DayOfWeek.Tuesday ? 6_000 : 10_000)));

    [Fact]
    public void Dia_fraco_nao_conta_o_dia_em_que_a_loja_fecha()
    {
        // Domingo sem nenhuma venda em 8 semanas = loja fechada: não é "dia fraco". Entre os que abrem, nenhum fica abaixo de 50%.
        Assert.Null(new RegraDiaMaisFraco().Avaliar(ComSemana(dia => dia == DayOfWeek.Sunday ? 0 : dia == DayOfWeek.Monday ? 6_000 : 10_000)));
    }

    [Fact]
    public void Dia_fraco_com_poucas_vendas_no_total_nao_gera_cartao()
    {
        var d = ComSemana(dia => dia == DayOfWeek.Tuesday ? 100 : 10_000, vendasPorDiaAberto: 0);
        Assert.Null(new RegraDiaMaisFraco().Avaliar(d));
    }

    // ---------------------------------------------------------------- aniversariantes

    [Fact]
    public void Aniversariantes_sem_ninguem_nao_gera_cartao() =>
        Assert.Null(new RegraAniversariantes().Avaliar(Dados()));

    [Fact]
    public void Aniversariantes_listam_tres_nomes_e_contam_o_resto()
    {
        var lista = new List<AvisosAoAbrir.Aniversariante>
        {
            new("Maria", Hoje.Date, 0, 30),
            new("João", Hoje.Date.AddDays(1), 1, 41),
            new("Ana", Hoje.Date.AddDays(3), 3, 25),
            new("Rui", Hoje.Date.AddDays(7), 7, 50)
        };
        var s = new RegraAniversariantes().Avaliar(Dados() with { Aniversariantes = lista })!;
        Assert.Equal(DestinoConsultor.Clientes, s.Destino);
        Assert.Contains("4 clientes fazem", s.Texto);
        Assert.Contains("Maria (hoje)", s.Texto);
        Assert.Contains("João (amanhã)", s.Texto);
        Assert.Contains("Ana (em 3 dias, dia 22/11)", s.Texto);
        Assert.Contains("e mais 1", s.Texto);
        Assert.DoesNotContain("Rui", s.Texto);
    }

    [Fact]
    public void Aniversariantes_um_so_fica_no_singular()
    {
        var s = new RegraAniversariantes().Avaliar(Dados() with { Aniversariantes = [new("Maria", Hoje.Date, 0, 30)] })!;
        Assert.StartsWith("1 cliente faz", s.Texto);
    }

    // ---------------------------------------------------------------- forma de pagamento

    [Fact]
    public void Forma_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraFormaDePagamentoDominante().Avaliar(Dados()));

    [Fact]
    public void Forma_com_menos_de_20_vendas_nao_gera_cartao() =>
        Assert.Null(new RegraFormaDePagamentoDominante().Avaliar(Dados() with { Formas = [new(FormaPagamento.Pix, 19, 100_000)] }));

    [Fact]
    public void Forma_com_59_por_cento_nao_gera_e_com_60_gera()
    {
        Assert.Null(new RegraFormaDePagamentoDominante().Avaliar(Dados() with
        {
            Formas = [new(FormaPagamento.Pix, 15, 59_000), new(FormaPagamento.Dinheiro, 15, 41_000)]
        }));
        var s = new RegraFormaDePagamentoDominante().Avaliar(Dados() with
        {
            Formas = [new(FormaPagamento.Pix, 15, 60_000), new(FormaPagamento.Dinheiro, 15, 40_000)]
        })!;
        Assert.Equal("forma-de-pagamento-dominante", s.Id);
        Assert.Contains("Pix", s.Titulo);
        Assert.Contains("60%", s.Texto);
        Assert.Contains("15 de 30 vendas", s.Texto);
        Assert.Contains(Dinheiro.Formatar(60_000), s.Texto);
        Assert.Equal(GravidadeSugestao.Informacao, s.Gravidade);
    }

    [Fact]
    public void Forma_a_prazo_dominante_pede_atencao()
    {
        var s = new RegraFormaDePagamentoDominante().Avaliar(Dados() with
        {
            Formas = [new(FormaPagamento.APrazo, 20, 90_000), new(FormaPagamento.Pix, 5, 10_000)]
        })!;
        Assert.Equal(GravidadeSugestao.Atencao, s.Gravidade);
        Assert.Contains("dinheiro a receber", s.Texto);
    }

    // ---------------------------------------------------------------- contas

    private static ContaAVencer Conta(int id, string desc, int venceEmDias, long valor) =>
        new(id, desc, null, Hoje.Date.AddDays(venceEmDias), valor, venceEmDias < 0);

    [Fact]
    public void Contas_sem_dados_nao_geram_cartao()
    {
        Assert.Null(new RegraContasVencidas().Avaliar(Dados()));
        Assert.Null(new RegraContasAVencer().Avaliar(Dados()));
    }

    [Fact]
    public void Contas_vencidas_somam_e_citam_a_mais_antiga()
    {
        var d = Dados() with { Contas = [Conta(1, "Aluguel", -10, 200_000), Conta(2, "Luz", -1, 30_000), Conta(3, "Água", 2, 9_000)] };
        var s = new RegraContasVencidas().Avaliar(d)!;
        Assert.Equal(GravidadeSugestao.Urgente, s.Gravidade);
        Assert.Equal(AreaConsultor.Financeiro, s.Area);
        Assert.Contains("2 contas a pagar estão vencidas", s.Texto);
        Assert.Contains(Dinheiro.Formatar(230_000), s.Texto);
        Assert.Contains("Aluguel", s.Texto);
        Assert.Contains("10 dias", s.Texto);
        Assert.DoesNotContain("Água", s.Texto);
    }

    [Fact]
    public void Conta_que_vence_hoje_nao_esta_vencida_mas_esta_a_vencer()
    {
        var d = Dados() with { Contas = [Conta(1, "Internet", 0, 12_000)] };
        Assert.Null(new RegraContasVencidas().Avaliar(d));
        var s = new RegraContasAVencer().Avaliar(d)!;
        Assert.Contains("hoje", s.Texto);
        Assert.Contains("1 conta vence", s.Texto);
    }

    [Fact]
    public void Contas_a_vencer_so_olham_ate_sete_dias_e_ignoram_as_vencidas()
    {
        var fora = Dados() with { Contas = [Conta(1, "Atrasada", -2, 1_000), Conta(2, "Longe", 8, 5_000)] };
        Assert.Null(new RegraContasAVencer().Avaliar(fora));

        var dentro = Dados() with { Contas = [Conta(1, "Fornecedor", 7, 5_000), Conta(2, "Luz", 3, 7_000)] };
        var s = new RegraContasAVencer().Avaliar(dentro)!;
        Assert.Equal(GravidadeSugestao.Atencao, s.Gravidade);
        Assert.Contains("2 contas vencem", s.Texto);
        Assert.Contains(Dinheiro.Formatar(12_000), s.Texto);
        Assert.Contains("Luz", s.Texto);          // a primeira a vencer
        Assert.Contains("22/11", s.Texto);
    }

    // ---------------------------------------------------------------- saldo do mês

    private static SaldoDoMes Saldo(long entradas, long saidas) =>
        new(new DateTime(2026, 11, 1), Hoje.Date, entradas, saidas, entradas - saidas);

    [Fact]
    public void Saldo_sem_dados_nao_gera_cartao() =>
        Assert.Null(new RegraSaldoDoMesNegativo().Avaliar(Dados()));

    [Fact]
    public void Saldo_positivo_ou_zerado_nao_gera_cartao()
    {
        Assert.Null(new RegraSaldoDoMesNegativo().Avaliar(Dados() with { Saldo = Saldo(100_000, 99_999) }));
        Assert.Null(new RegraSaldoDoMesNegativo().Avaliar(Dados() with { Saldo = Saldo(100_000, 100_000) }));
    }

    [Fact]
    public void Saldo_sem_nenhum_movimento_nao_gera_cartao() =>
        Assert.Null(new RegraSaldoDoMesNegativo().Avaliar(Dados() with { Saldo = Saldo(0, 0) }));

    [Fact]
    public void Saldo_negativo_gera_cartao_com_entradas_saidas_e_saldo()
    {
        var s = new RegraSaldoDoMesNegativo().Avaliar(Dados() with { Saldo = Saldo(100_000, 130_000) })!;
        Assert.Equal(DestinoConsultor.RelatorioFinanceiro, s.Destino);
        Assert.Contains(Dinheiro.Formatar(100_000), s.Texto);
        Assert.Contains(Dinheiro.Formatar(130_000), s.Texto);
        Assert.Contains(Dinheiro.Formatar(-30_000), s.Texto);
        Assert.Contains("01/11 a 19/11", s.Texto);
    }

    [Fact]
    public void Saldo_negativo_nos_primeiros_quatro_dias_do_mes_ainda_nao_gera_cartao()
    {
        var quatro = new DadosConsultor(new DateTime(2026, 11, 4)) { Saldo = Saldo(0, 50_000) };
        Assert.Null(new RegraSaldoDoMesNegativo().Avaliar(quatro));
        var cinco = new DadosConsultor(new DateTime(2026, 11, 5)) { Saldo = Saldo(0, 50_000) };
        Assert.NotNull(new RegraSaldoDoMesNegativo().Avaliar(cinco));
    }

    // ---------------------------------------------------------------- conjunto

    [Fact]
    public void Todas_as_regras_tem_id_unico_e_nenhuma_gera_cartao_sem_dados()
    {
        var regras = RegrasDoConsultor.Padrao();
        Assert.Equal(14, regras.Count);
        Assert.Equal(regras.Count, regras.Select(r => r.Id).Distinct().Count());
        Assert.All(regras, r => Assert.Null(r.Avaliar(Dados())));
    }

    [Fact]
    public void Texto_dos_cartoes_nao_promete_resultado()
    {
        var d = Dados() with
        {
            Saldo = Saldo(100_000, 130_000),
            Contas = [Conta(1, "Aluguel", -3, 200_000)],
            Aniversariantes = [new("Maria", Hoje.Date, 0, 30)],
            ProdutosComVenda = [Giro("Boné", 0, 30)]
        };
        foreach (var r in RegrasDoConsultor.Padrao())
        {
            var s = r.Avaliar(d);
            if (s is null) continue;
            Assert.DoesNotContain("garant", s.Texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("vai aumentar", s.Texto, StringComparison.OrdinalIgnoreCase);
        }
    }
}
