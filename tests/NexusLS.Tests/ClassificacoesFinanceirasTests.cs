using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Fase 2.5: categorias configuráveis e classificação dos lançamentos (o que conta no resultado e o que só move o saldo).</summary>
[Collection("OperadorAtual")] // o FinanceiroService lê o OperadorAtual (estático)
public sealed class ClassificacoesFinanceirasTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 10, 0, 0);
    private readonly BancoDeTeste _banco = new();
    private readonly FinanceiroService _fin;
    private readonly CategoriaFinanceiraService _cats;
    private readonly PosicaoFinanceiraService _posicao;

    public ClassificacoesFinanceirasTests()
    {
        _fin = new FinanceiroService(_banco.Fabrica, () => Agora);
        _cats = new CategoriaFinanceiraService(_banco.Fabrica, () => Agora);
        _posicao = new PosicaoFinanceiraService(_banco.Fabrica, () => Agora);
    }

    public void Dispose() => _banco.Dispose();

    private static DateTime Dia(int d) => new(2026, 10, d);

    private Task<IReadOnlyList<Lancamento>> LancarPagoAsync(TipoLancamento tipo, long valor, ClassificacaoLancamento c,
        string? categoria = null, FormaPagamento forma = FormaPagamento.Dinheiro) =>
        _fin.CriarAsync(new DadosLancamento(tipo, "Teste", valor, Dia(10), categoria, JaPago: true, Forma: forma, Classificacao: c));

    // ---------- categorias ----------

    [Fact]
    public async Task Categorias_padrao_sao_criadas_na_primeira_consulta_e_so_uma_vez()
    {
        var despesas = await _cats.NomesAtivosAsync(TipoLancamento.Despesa);
        var receitas = await _cats.NomesAtivosAsync(TipoLancamento.Receita);
        await _cats.GarantirPadraoAsync();

        Assert.Equal(RegrasFinanceiras.CategoriasDespesa.OrderBy(n => n), despesas.OrderBy(n => n));
        Assert.Equal(RegrasFinanceiras.CategoriasReceita.OrderBy(n => n), receitas.OrderBy(n => n));
        Assert.Equal(despesas.Count + receitas.Count, (await _cats.ListarAsync(incluirInativas: true)).Count);
    }

    [Fact]
    public async Task Categoria_nova_aparece_na_lista_do_tipo_certo()
    {
        await _cats.CriarAsync(TipoLancamento.Despesa, "  Contador ");

        Assert.Contains("Contador", await _cats.NomesAtivosAsync(TipoLancamento.Despesa));
        Assert.DoesNotContain("Contador", await _cats.NomesAtivosAsync(TipoLancamento.Receita));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Categoria_sem_nome_e_recusada(string nome)
    {
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cats.CriarAsync(TipoLancamento.Despesa, nome));
        Assert.Contains("nome", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Categoria_repetida_e_recusada_mesmo_com_outra_caixa_de_letra()
    {
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _cats.CriarAsync(TipoLancamento.Despesa, "aluguel"));
        Assert.Contains("já existe", erro.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mesmo_nome_pode_existir_em_tipos_diferentes()
    {
        await _cats.CriarAsync(TipoLancamento.Receita, "Aluguel");

        Assert.Contains("Aluguel", await _cats.NomesAtivosAsync(TipoLancamento.Receita));
    }

    [Fact]
    public async Task Categoria_desativada_some_da_lista_mas_nao_e_apagada_e_pode_voltar()
    {
        var energia = (await _cats.ListarAsync(TipoLancamento.Despesa)).Single(c => c.Nome == "Energia");

        await _cats.DefinirAtivaAsync(energia.Id, false);
        Assert.DoesNotContain("Energia", await _cats.NomesAtivosAsync(TipoLancamento.Despesa));
        Assert.Contains(await _cats.ListarAsync(TipoLancamento.Despesa, incluirInativas: true), c => c.Nome == "Energia" && !c.Ativa);

        var de_novo = await _cats.CriarAsync(TipoLancamento.Despesa, "energia"); // criar o nome de novo reativa, não duplica
        Assert.Equal(energia.Id, de_novo.Id);
        Assert.Contains("Energia", await _cats.NomesAtivosAsync(TipoLancamento.Despesa));
    }

    [Fact]
    public async Task Categoria_desativada_nao_mexe_nos_lancamentos_antigos()
    {
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Conta de luz", 20000, Dia(15), "Energia"));
        var energia = (await _cats.ListarAsync(TipoLancamento.Despesa)).Single(c => c.Nome == "Energia");
        await _cats.DefinirAtivaAsync(energia.Id, false);

        var lanc = (await _fin.ListarAsync(new FiltroLancamentos(Dia(1), Dia(31)))).Single();
        Assert.Equal("Energia", lanc.Categoria);
    }

    // ---------- classificação ----------

    [Fact]
    public async Task Lancamento_comum_continua_com_classificacao_padrao()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Aluguel", 100000, Dia(5)))).Single();

        Assert.Equal(ClassificacaoLancamento.Padrao, l.Classificacao);
    }

    [Theory]
    [InlineData(TipoLancamento.Receita, ClassificacaoLancamento.RetiradaProprietario)]
    [InlineData(TipoLancamento.Receita, ClassificacaoLancamento.OutraSaida)]
    [InlineData(TipoLancamento.Despesa, ClassificacaoLancamento.Aporte)]
    [InlineData(TipoLancamento.Despesa, ClassificacaoLancamento.OutraEntrada)]
    public async Task Classificacao_de_outro_tipo_e_recusada(TipoLancamento tipo, ClassificacaoLancamento classificacao)
    {
        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _fin.CriarAsync(new DadosLancamento(tipo, "X", 1000, Dia(5), Classificacao: classificacao)));

        Assert.Contains("classificação", erro.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _fin.ListarAsync(new FiltroLancamentos(Dia(1), Dia(31))));
    }

    [Fact]
    public async Task Editar_pode_trocar_a_classificacao_e_recusa_a_invalida()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Dinheiro do dono", 50000, Dia(5)))).Single();

        await _fin.EditarAsync(l.Id, "Dinheiro do dono", 50000, Dia(5), null, null, null, ClassificacaoLancamento.Aporte);
        Assert.Equal(ClassificacaoLancamento.Aporte,
            (await _fin.ListarAsync(new FiltroLancamentos(Dia(1), Dia(31)))).Single().Classificacao);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _fin.EditarAsync(l.Id, "Dinheiro do dono", 50000, Dia(5), null, null, null, ClassificacaoLancamento.RetiradaProprietario));
    }

    [Fact]
    public async Task Editar_sem_informar_a_classificacao_mantem_a_que_ja_tinha()
    {
        var l = (await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Despesa, "Retirada", 30000, Dia(5),
            Classificacao: ClassificacaoLancamento.RetiradaProprietario))).Single();

        await _fin.EditarAsync(l.Id, "Retirada do mês", 35000, Dia(6), null, null, null);

        var depois = (await _fin.ListarAsync(new FiltroLancamentos(Dia(1), Dia(31)))).Single();
        Assert.Equal(ClassificacaoLancamento.RetiradaProprietario, depois.Classificacao);
        Assert.Equal(35000, depois.ValorCentavos);
    }

    [Fact]
    public async Task Conta_de_compra_de_mercadoria_nao_aceita_classificacao_especial()
    {
        var dados = new DadosLancamento(TipoLancamento.Despesa, "Compra", 10000, Dia(5), Classificacao: ClassificacaoLancamento.OutraSaida);

        await Assert.ThrowsAsync<RegraDeNegocioException>(async () =>
            await Task.FromResult(FinanceiroService.Montar(dados, Agora, compraId: 7)));
    }

    // ---------- resumo do Financeiro ----------

    [Fact]
    public async Task Resumo_separa_receita_e_despesa_do_resultado_do_que_so_move_o_saldo()
    {
        await LancarPagoAsync(TipoLancamento.Receita, 100000, ClassificacaoLancamento.Padrao);          // serviço: conta
        await LancarPagoAsync(TipoLancamento.Receita, 500000, ClassificacaoLancamento.Aporte);          // dono colocou: não conta
        await LancarPagoAsync(TipoLancamento.Receita, 20000, ClassificacaoLancamento.OutraEntrada);     // reembolso: não conta
        await LancarPagoAsync(TipoLancamento.Despesa, 30000, ClassificacaoLancamento.Padrao, "Aluguel"); // despesa: conta
        await LancarPagoAsync(TipoLancamento.Despesa, 200000, ClassificacaoLancamento.RetiradaProprietario); // dono tirou: não conta
        await LancarPagoAsync(TipoLancamento.Despesa, 10000, ClassificacaoLancamento.OutraSaida);       // empréstimo pago: não conta

        var r = await _fin.ResumoAsync(Dia(1), Dia(31));

        Assert.Equal(100000, r.ReceitasRecebidasCentavos);
        Assert.Equal(30000, r.DespesasPagasCentavos);
        Assert.Equal(70000, r.ResultadoCentavos);
        Assert.Equal(20000, r.OutrasEntradasCentavos);
        Assert.Equal(500000, r.AportesCentavos);
        Assert.Equal(200000, r.RetiradasProprietarioCentavos);
        Assert.Equal(10000, r.OutrasSaidasCentavos);
        Assert.Equal(520000, r.EntradasForaDoResultadoCentavos);
        Assert.Equal(210000, r.SaidasForaDoResultadoCentavos);
        Assert.Equal(new[] { "Aluguel" }, r.DespesasPorCategoria.Select(c => c.Categoria));
    }

    [Fact]
    public async Task Resumo_so_conta_o_que_foi_pago_dentro_do_periodo()
    {
        await _fin.CriarAsync(new DadosLancamento(TipoLancamento.Receita, "Aporte futuro", 90000, Dia(25),
            Classificacao: ClassificacaoLancamento.Aporte)); // em aberto: ainda não entrou

        var r = await _fin.ResumoAsync(Dia(1), Dia(31));

        Assert.Equal(0, r.AportesCentavos);
        Assert.Equal(90000, r.AReceberCentavos);
    }

    // ---------- motor (saldos) ----------

    [Fact]
    public async Task Aporte_aumenta_o_saldo_mas_nao_vira_receita_no_motor()
    {
        await LancarPagoAsync(TipoLancamento.Receita, 80000, ClassificacaoLancamento.Aporte);

        var p = await _posicao.CalcularAsync(Dia(1), Dia(31));

        Assert.Equal(80000, p.SaldoDisponivelCentavos);
        Assert.Equal(80000, p.Periodo.EntradasRealizadasCentavos);
        Assert.Equal(0, p.Periodo.OutrasReceitasCentavos);
        Assert.Equal(80000, p.Periodo.AportesCentavos);
        Assert.Equal(0, p.Periodo.ResultadoCentavos);
    }

    [Fact]
    public async Task Retirada_do_proprietario_reduz_o_saldo_mas_nao_vira_despesa_no_motor()
    {
        await LancarPagoAsync(TipoLancamento.Receita, 100000, ClassificacaoLancamento.Aporte);
        await LancarPagoAsync(TipoLancamento.Despesa, 40000, ClassificacaoLancamento.RetiradaProprietario);

        var p = await _posicao.CalcularAsync(Dia(1), Dia(31));

        Assert.Equal(60000, p.SaldoDisponivelCentavos);
        Assert.Equal(0, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(40000, p.Periodo.RetiradasProprietarioCentavos);
        Assert.Equal(0, p.Periodo.ResultadoCentavos);
    }

    [Fact]
    public async Task Receita_adicional_e_despesa_comuns_continuam_entrando_no_resultado_do_motor()
    {
        await LancarPagoAsync(TipoLancamento.Receita, 100000, ClassificacaoLancamento.Padrao);
        await LancarPagoAsync(TipoLancamento.Despesa, 30000, ClassificacaoLancamento.Padrao, "Energia");

        var p = await _posicao.CalcularAsync(Dia(1), Dia(31));

        Assert.Equal(100000, p.Periodo.OutrasReceitasCentavos);
        Assert.Equal(30000, p.Periodo.DespesasOperacionaisCentavos);
        Assert.Equal(70000, p.Periodo.ResultadoCentavos);
    }

    [Fact]
    public async Task Resumo_do_financeiro_e_o_motor_chegam_ao_mesmo_resultado()
    {
        await LancarPagoAsync(TipoLancamento.Receita, 100000, ClassificacaoLancamento.Padrao);
        await LancarPagoAsync(TipoLancamento.Receita, 500000, ClassificacaoLancamento.Aporte);
        await LancarPagoAsync(TipoLancamento.Despesa, 30000, ClassificacaoLancamento.Padrao, "Aluguel");
        await LancarPagoAsync(TipoLancamento.Despesa, 200000, ClassificacaoLancamento.RetiradaProprietario);

        var resumo = await _fin.ResumoAsync(Dia(1), Dia(31));
        var motor = await _posicao.CalcularAsync(Dia(1), Dia(31));

        Assert.Equal(motor.Periodo.ResultadoCentavos, resumo.ResultadoCentavos);
        Assert.Equal(motor.Periodo.AportesCentavos, resumo.AportesCentavos);
        Assert.Equal(motor.Periodo.RetiradasProprietarioCentavos, resumo.RetiradasProprietarioCentavos);
    }

    [Fact]
    public void Cada_tipo_oferece_a_classificacao_comum_primeiro()
    {
        Assert.Equal(ClassificacaoLancamento.Padrao, RegrasFinanceiras.ClassificacoesDe(TipoLancamento.Despesa)[0]);
        Assert.Equal(ClassificacaoLancamento.Padrao, RegrasFinanceiras.ClassificacoesDe(TipoLancamento.Receita)[0]);
        Assert.Equal(3, RegrasFinanceiras.ClassificacoesDe(TipoLancamento.Despesa).Count);
        Assert.Equal(3, RegrasFinanceiras.ClassificacoesDe(TipoLancamento.Receita).Count);
    }
}
