using NexusLS.Core.Consultor;

namespace NexusLS.Tests.Consultor;

/// <summary>As 4 regras acrescentadas depois: margem baixa, descontos altos, clientes sumidos e melhor horário.</summary>
public sealed class RegrasConsultorNovasTests
{
    private static readonly DateTime Hoje = new(2026, 11, 19, 15, 0, 0);
    private static DadosConsultor Dados() => new(Hoje);

    // ---------------------------------------------------------------- margem baixa
    [Fact]
    public void Margem_sem_dados_nao_gera_cartao() => Assert.Null(new RegraMargemBaixa().Avaliar(Dados()));

    [Fact]
    public void Margem_de_20_por_cento_gera_cartao()
    {
        var d = Dados() with { Margem = new MargemDoPeriodo(1_000_000, 800_000, 30, 0) };
        var s = new RegraMargemBaixa().Avaliar(d)!;
        Assert.Equal(GravidadeSugestao.Atencao, s.Gravidade);
        Assert.Contains("20%", s.Texto);
    }

    [Fact]
    public void Margem_abaixo_de_10_por_cento_e_urgente()
    {
        var d = Dados() with { Margem = new MargemDoPeriodo(1_000_000, 950_000, 30, 0) };
        Assert.Equal(GravidadeSugestao.Urgente, new RegraMargemBaixa().Avaliar(d)!.Gravidade);
    }

    [Fact]
    public void Margem_de_25_por_cento_nao_gera_cartao() =>
        Assert.Null(new RegraMargemBaixa().Avaliar(Dados() with { Margem = new MargemDoPeriodo(1_000_000, 750_000, 30, 0) }));

    [Fact]
    public void Margem_com_pouco_dado_ou_maioria_sem_custo_nao_gera_cartao()
    {
        Assert.Null(new RegraMargemBaixa().Avaliar(Dados() with { Margem = new MargemDoPeriodo(1_000_000, 900_000, 19, 0) }));
        Assert.Null(new RegraMargemBaixa().Avaliar(Dados() with { Margem = new MargemDoPeriodo(99_999, 90_000, 30, 0) }));
        Assert.Null(new RegraMargemBaixa().Avaliar(Dados() with { Margem = new MargemDoPeriodo(1_000_000, 900_000, 20, 21) }));
    }

    // ---------------------------------------------------------------- descontos altos
    [Fact]
    public void Descontos_sem_dados_nao_gera_cartao() => Assert.Null(new RegraDescontosAltos().Avaliar(Dados()));

    [Fact]
    public void Descontos_de_10_por_cento_geram_cartao_e_de_9_nao()
    {
        var s = new RegraDescontosAltos().Avaliar(Dados() with { Descontos = new DescontosDoPeriodo(25, 1_000_000, 100_000) })!;
        Assert.Contains("10%", s.Texto);
        Assert.Null(new RegraDescontosAltos().Avaliar(Dados() with { Descontos = new DescontosDoPeriodo(25, 1_000_000, 90_000) }));
    }

    [Fact]
    public void Descontos_com_poucas_vendas_nao_gera_cartao() =>
        Assert.Null(new RegraDescontosAltos().Avaliar(Dados() with { Descontos = new DescontosDoPeriodo(19, 1_000_000, 500_000) }));

    // ---------------------------------------------------------------- clientes sumidos
    [Fact]
    public void Clientes_sumidos_sem_dados_nao_gera_cartao() => Assert.Null(new RegraClientesSumidos().Avaliar(Dados()));

    [Fact]
    public void Clientes_sumidos_lista_os_de_maior_gasto_primeiro()
    {
        var d = Dados() with
        {
            ClientesSumidos =
            [
                new ClienteSumido(1, "Ana", Hoje.Date.AddDays(-100), 4, 50_000),
                new ClienteSumido(2, "Bia", Hoje.Date.AddDays(-120), 5, 90_000)
            ]
        };
        var s = new RegraClientesSumidos().Avaliar(d)!;
        Assert.Equal(GravidadeSugestao.Informacao, s.Gravidade);
        Assert.Contains("2 clientes", s.Texto);
        Assert.True(s.Texto.IndexOf("Bia", StringComparison.Ordinal) < s.Texto.IndexOf("Ana", StringComparison.Ordinal));
    }

    [Fact]
    public void Clientes_recentes_ou_com_poucas_compras_nao_contam()
    {
        var d = Dados() with
        {
            ClientesSumidos = [new ClienteSumido(1, "Ana", Hoje.Date.AddDays(-89), 4, 50_000), new ClienteSumido(2, "Bia", Hoje.Date.AddDays(-200), 2, 90_000)]
        };
        Assert.Null(new RegraClientesSumidos().Avaliar(d));
    }

    // ---------------------------------------------------------------- melhor horário
    [Fact]
    public void Melhor_horario_sem_dados_nao_gera_cartao() => Assert.Null(new RegraMelhorHorario().Avaliar(Dados()));

    [Fact]
    public void Melhor_horario_aponta_a_faixa_de_duas_horas_que_mais_vende()
    {
        var d = Dados() with
        {
            Horarios = [new VendasPorHora(10, 10, 100_000), new VendasPorHora(14, 25, 400_000), new VendasPorHora(15, 20, 300_000), new VendasPorHora(18, 10, 100_000)]
        };
        var s = new RegraMelhorHorario().Avaliar(d)!;
        Assert.Contains("14h às 16h", s.Texto);
        Assert.Contains("78%", s.Texto);
    }

    [Fact]
    public void Melhor_horario_com_vendas_espalhadas_ou_poucas_nao_gera_cartao()
    {
        var espalhado = Enumerable.Range(8, 12).Select(h => new VendasPorHora(h, 5, 100_000)).ToList();
        Assert.Null(new RegraMelhorHorario().Avaliar(Dados() with { Horarios = espalhado }));
        Assert.Null(new RegraMelhorHorario().Avaliar(Dados() with { Horarios = [new VendasPorHora(14, 39, 900_000)] }));
    }
}
