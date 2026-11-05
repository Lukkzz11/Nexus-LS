using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Relatório do Caixa por período: turnos, vendas por forma, sangrias, reforços, despesas, diferenças e o PDF.</summary>
public class RelatorioCaixaPeriodoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private DateTime _agora = new(2026, 10, 10, 9, 0, 0);
    private readonly VendaService _vendas;
    private readonly CaixaService _caixa;
    private readonly RelatorioCaixaPeriodoService _servico;
    private int _produtoId;

    public RelatorioCaixaPeriodoTests()
    {
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
        _caixa = new CaixaService(_banco.Fabrica, () => _agora);
        _servico = new RelatorioCaixaPeriodoService(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private async Task PrepararAsync() =>
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", "111", 1000, 600, 0), 200)).Id;

    private Task<Venda> VenderAsync(FormaPagamento forma, int qtd = 1)
    {
        _agora = _agora.AddMinutes(1);
        return _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, qtd) }, 0, forma,
            forma == FormaPagamento.Dinheiro ? qtd * 1000L : null));
    }

    /// <summary>Dia 10: abre com R$ 50, vende, mexe no caixa, fecha com R$ 5 a menos em dinheiro. Dia 12: outro turno, sem diferença.</summary>
    private async Task<(TurnoCaixa t1, TurnoCaixa t2)> CenarioDoisTurnosAsync()
    {
        await PrepararAsync();
        var t1 = await _caixa.AbrirAsync(5000);
        await VenderAsync(FormaPagamento.Dinheiro, 2);       // 20,00
        await VenderAsync(FormaPagamento.Pix, 1);            // 10,00
        var cancelada = await VenderAsync(FormaPagamento.Pix, 9); // 90,00, cancelada logo depois
        await _vendas.CancelarAsync(cancelada.Id, "cliente desistiu");
        await _caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 3000, "Levou ao banco");
        await _caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Reforco, 1000, "Troco");
        await _caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Despesa, 500, "Café");
        var r1 = await _caixa.ResumoAsync(t1.Id);
        _agora = _agora.AddMinutes(5);
        await _caixa.FecharAsync(t1.Id, new ConferenciaCaixa(r1.EsperadoDinheiroCentavos - 500, r1.EsperadoCartaoCentavos, r1.EsperadoPixCentavos, "Faltou troco"));

        _agora = new DateTime(2026, 10, 12, 8, 0, 0);
        var t2 = await _caixa.AbrirAsync(2000);
        await VenderAsync(FormaPagamento.CartaoCredito, 3);  // 30,00
        var r2 = await _caixa.ResumoAsync(t2.Id);
        _agora = _agora.AddMinutes(5);
        await _caixa.FecharAsync(t2.Id, new ConferenciaCaixa(r2.EsperadoDinheiroCentavos, r2.EsperadoCartaoCentavos, r2.EsperadoPixCentavos));
        return (t1, t2);
    }

    [Fact]
    public async Task Totais_do_periodo_somam_vendas_por_forma_movimentos_e_diferencas_sem_as_canceladas()
    {
        await CenarioDoisTurnosAsync();
        _agora = new DateTime(2026, 10, 20, 9, 0, 0);

        var r = await _servico.GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 31).AddDays(-11)); // até 20/10

        Assert.Equal(2, r.Turnos.Count);
        Assert.Equal(3, r.VendasQuantidade);                       // dinheiro + pix + crédito (a cancelada fora)
        Assert.Equal(6150, r.VendasCentavos);                      // o crédito em 3x leva acréscimo: R$ 31,50
        Assert.Equal(1, r.VendasCanceladasIgnoradas);
        Assert.Equal(new[] { (FormaPagamento.Dinheiro, 2000L), (FormaPagamento.Pix, 1000L), (FormaPagamento.CartaoCredito, 3150L) }.OrderBy(x => x.Item1),
            r.VendasPorForma.Select(f => (f.Forma, f.TotalCentavos)).OrderBy(x => x.Forma));
        Assert.Equal(3000, r.SangriasCentavos);
        Assert.Equal(1000, r.ReforcosCentavos);
        Assert.Equal(500, r.DespesasCentavos);
        Assert.Equal(3, r.Movimentos.Count);
        Assert.Equal(-500, r.DiferencaDinheiroCentavos);
        Assert.Equal(-500, r.DiferencaTotalCentavos);
        Assert.Contains(r.Avisos, a => a.Contains("cancelada"));
    }

    [Fact]
    public async Task Periodo_usa_dias_locais_inclusive_e_deixa_de_fora_o_que_e_de_outro_dia()
    {
        var (t1, _) = await CenarioDoisTurnosAsync();
        _agora = new DateTime(2026, 10, 20, 9, 0, 0);

        var soDia10 = await _servico.GerarAsync(new DateTime(2026, 10, 10), new DateTime(2026, 10, 10));
        Assert.Equal(new[] { t1.Id }, soDia10.Turnos.Select(t => t.TurnoId));
        Assert.Equal(3000, soDia10.VendasCentavos);               // dinheiro 20 + pix 10; o crédito é do dia 12
        Assert.Equal(3000, soDia10.SangriasCentavos);

        var soDia12 = await _servico.GerarAsync(new DateTime(2026, 10, 12), new DateTime(2026, 10, 12));
        Assert.Single(soDia12.Turnos);
        Assert.Equal(3150, soDia12.VendasCentavos);
        Assert.Equal(0, soDia12.SangriasCentavos);
        Assert.Equal(0, soDia12.DiferencaTotalCentavos);

        var vazio = await _servico.GerarAsync(new DateTime(2026, 10, 15), new DateTime(2026, 10, 16));
        Assert.Empty(vazio.Turnos);
        Assert.Equal(0, vazio.VendasCentavos);
        Assert.Empty(vazio.VendasPorForma);
    }

    [Fact]
    public async Task Cada_turno_traz_sua_ultima_conferencia_e_turno_aberto_gera_aviso()
    {
        var (t1, _) = await CenarioDoisTurnosAsync();
        _agora = new DateTime(2026, 10, 13, 8, 0, 0);
        var aberto = await _caixa.AbrirAsync(100);

        // refazer a conferência do primeiro vira a versão 2 (a mais recente é a que vale no relatório)
        var r1 = await _caixa.ResumoAsync(t1.Id);
        await _caixa.FecharAsync(t1.Id, new ConferenciaCaixa(r1.EsperadoDinheiroCentavos, r1.EsperadoCartaoCentavos, r1.EsperadoPixCentavos, "Achou a diferença"));

        var r = await _servico.GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 13));

        Assert.Equal(2, r.Turnos.First(t => t.TurnoId == t1.Id).Conferencia!.Versao);
        Assert.Equal(0, r.DiferencaDinheiroCentavos);
        Assert.True(r.Turnos.Single(t => t.TurnoId == aberto.Id).Aberto);
        Assert.Contains(r.Avisos, a => a.Contains("ainda não foi fechado"));
    }

    [Fact]
    public async Task Periodo_invalido_e_recusado()
    {
        _agora = new DateTime(2026, 10, 20, 9, 0, 0);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.GerarAsync(new DateTime(2026, 10, 5), new DateTime(2026, 10, 4)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.GerarAsync(new DateTime(2026, 10, 5), new DateTime(2026, 10, 21)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.GerarAsync(new DateTime(2025, 1, 1), new DateTime(2026, 10, 20)));
        Assert.Null(_servico.ValidarPeriodo(new DateTime(2026, 10, 20), new DateTime(2026, 10, 20)));
    }

    [Fact]
    public async Task Texto_e_pdf_trazem_os_numeros_e_nao_somam_a_venda_cancelada()
    {
        await CenarioDoisTurnosAsync();
        _agora = new DateTime(2026, 10, 20, 9, 0, 0);
        var r = await _servico.GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 20));

        var linhas = RelatorioCaixaPdf.Linhas(r, "Loja da Ana");
        var texto = string.Join("\n", linhas);

        Assert.Equal("Loja da Ana", linhas[0]);
        Assert.Contains("RELATÓRIO DO CAIXA - 01/10/2026 a 20/10/2026", texto);
        Assert.Contains("Total vendido", texto);
        Assert.Contains("R$ 61,50", texto);                       // total sem a cancelada de R$ 90,00
        Assert.DoesNotContain("R$ 151,50", texto);
        Assert.Contains("Caixa nº", texto);
        Assert.Contains("Faltou troco", texto);
        Assert.All(linhas, l => Assert.True(l.Length <= 90, "linha larga demais para a página: " + l));

        var pdf = RelatorioCaixaPdf.Gerar(r, "Loja da Ana");
        Assert.Equal("%PDF-1.4", Encoding.Latin1.GetString(pdf, 0, 8));
        Assert.Contains("%%EOF", Encoding.Latin1.GetString(pdf));
        Assert.Equal("caixa_2026-10-01_a_2026-10-20.pdf", RelatorioCaixaPdf.NomeDoArquivo(r));
    }

    [Fact]
    public async Task Relatorio_so_le_e_nao_grava_nada()
    {
        await CenarioDoisTurnosAsync();
        _agora = new DateTime(2026, 10, 20, 9, 0, 0);
        await using var antes = _banco.CriarContexto();
        var vendasAntes = antes.Vendas.Count();
        var movAntes = antes.MovimentosCaixa.Count();
        var fechAntes = antes.FechamentosCaixa.Count();

        await _servico.GerarAsync(new DateTime(2026, 10, 1), new DateTime(2026, 10, 20));

        await using var depois = _banco.CriarContexto();
        Assert.Equal((vendasAntes, movAntes, fechAntes), (depois.Vendas.Count(), depois.MovimentosCaixa.Count(), depois.FechamentosCaixa.Count()));
    }
}
