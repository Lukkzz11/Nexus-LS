using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record LinhaSaldoConta(int Id, string Nome, string Tipo, string SaldoInicial, string Entradas, string Saidas, string Saldo);

/// <summary>
/// Visão geral do dinheiro: saldo disponível (gaveta + bancos), a receber, a pagar, em compensação (cheques e cartões),
/// projeção e o resultado do período. Todos os números vêm do <see cref="PosicaoFinanceiraService"/> (motor financeiro).
/// </summary>
public partial class SaldosViewModel : ViewModelBase
{
    private readonly PosicaoFinanceiraService _posicao;
    private int _versao;
    private bool _definindoPeriodo;

    public SaldosViewModel(PosicaoFinanceiraService posicao, Func<DateTime>? agora = null)
    {
        _posicao = posicao;
        var hoje = (agora ?? (() => DateTime.Now))().Date;
        _de = new DateTime(hoje.Year, hoje.Month, 1);
        _ate = hoje;
        Carregamento = CarregarAsync();
    }

    /// <summary>A primeira carga (os testes esperam por ela).</summary>
    public Task Carregamento { get; }

    public ObservableCollection<LinhaSaldoConta> Contas { get; } = new();
    public ObservableCollection<string> Avisos { get; } = new();
    public ObservableCollection<string> DespesasPorCategoria { get; } = new();

    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;

    [ObservableProperty] private string _saldoDisponivelTexto = "";
    [ObservableProperty] private string _dinheiroFisicoTexto = "";
    [ObservableProperty] private string _bancosTexto = "";
    [ObservableProperty] private string _aReceberTexto = "";
    [ObservableProperty] private string _aReceberDetalhe = "";
    [ObservableProperty] private string _aPagarTexto = "";
    [ObservableProperty] private string _aPagarDetalhe = "";
    [ObservableProperty] private string _emCompensacaoTexto = "";
    [ObservableProperty] private string _emCompensacaoDetalhe = "";
    [ObservableProperty] private string _projecao7Texto = "";
    [ObservableProperty] private string _projecao30Texto = "";
    [ObservableProperty] private string _projecao60Texto = "";
    [ObservableProperty] private string _projecao90Texto = "";

    [ObservableProperty] private string _receitaBrutaTexto = "";
    [ObservableProperty] private string _custoTexto = "";
    [ObservableProperty] private string _lucroBrutoTexto = "";
    [ObservableProperty] private string _outrasReceitasTexto = "";
    [ObservableProperty] private string _despesasTexto = "";
    [ObservableProperty] private string _resultadoTexto = "";
    [ObservableProperty] private bool _resultadoNegativo;
    [ObservableProperty] private bool _temAvisos;

    partial void OnDeChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnAteChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }

    [RelayCommand]
    public Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        if (ate.Date < de.Date)
            throw new RegraDeNegocioException("A data final não pode ser antes da inicial.");

        var p = await _posicao.CalcularAsync(de, ate);
        if (versao != _versao) return;

        Contas.Clear();
        foreach (var c in p.Contas)
            Contas.Add(new LinhaSaldoConta(c.Id, c.Nome, c.Tipo == TipoContaFin.Caixa ? "Dinheiro" : "Banco",
                Dinheiro.Formatar(c.SaldoInicialCentavos), Dinheiro.Formatar(c.EntradasCentavos),
                Dinheiro.Formatar(c.SaidasCentavos), Dinheiro.Formatar(c.DisponivelCentavos)));

        SaldoDisponivelTexto = Dinheiro.Formatar(p.SaldoDisponivelCentavos);
        DinheiroFisicoTexto = Dinheiro.Formatar(p.DinheiroFisicoCentavos);
        BancosTexto = Dinheiro.Formatar(p.BancosCentavos);

        AReceberTexto = Dinheiro.Formatar(p.AReceber.TotalCentavos);
        AReceberDetalhe = Detalhe(p.AReceber);
        APagarTexto = Dinheiro.Formatar(p.APagar.TotalCentavos);
        APagarDetalhe = Detalhe(p.APagar);
        EmCompensacaoTexto = Dinheiro.Formatar(p.EmCompensacao.TotalCentavos);
        EmCompensacaoDetalhe = $"Cheques {Dinheiro.Formatar(p.EmCompensacao.ChequesCentavos)} · Cartões {Dinheiro.Formatar(p.EmCompensacao.CartoesCentavos)}" +
            (p.EmCompensacao.ChequesEmitidosCentavos > 0 ? $"\nCheques emitidos (a sair): {Dinheiro.Formatar(p.EmCompensacao.ChequesEmitidosCentavos)}" : "");

        Projecao7Texto = Dinheiro.Formatar(p.Projecao.PorDias[7]);
        Projecao30Texto = Dinheiro.Formatar(p.Projecao.PorDias[30]);
        Projecao60Texto = Dinheiro.Formatar(p.Projecao.PorDias[60]);
        Projecao90Texto = Dinheiro.Formatar(p.Projecao.PorDias[90]);

        var per = p.Periodo;
        ReceitaBrutaTexto = Dinheiro.Formatar(per.ReceitaBrutaCentavos);
        CustoTexto = Dinheiro.Formatar(per.CustoMercadoriasCentavos);
        LucroBrutoTexto = Dinheiro.Formatar(per.LucroBrutoCentavos);
        OutrasReceitasTexto = Dinheiro.Formatar(per.OutrasReceitasCentavos);
        DespesasTexto = Dinheiro.Formatar(per.DespesasOperacionaisCentavos);
        ResultadoTexto = Dinheiro.Formatar(per.ResultadoCentavos);
        ResultadoNegativo = per.ResultadoCentavos < 0;

        DespesasPorCategoria.Clear();
        foreach (var d in per.DespesasPorCategoria.OrderByDescending(d => d.Value))
            DespesasPorCategoria.Add($"{d.Key}: {Dinheiro.Formatar(d.Value)}");

        Avisos.Clear();
        foreach (var a in p.Avisos) Avisos.Add(a.Mensagem);
        TemAvisos = Avisos.Count > 0;
        Mensagem = "";
    });

    private static string Detalhe(PendenciasFin p) =>
        p.Quantidade == 0
            ? "Nada em aberto"
            : $"{p.Quantidade} item(ns) · Vencido {Dinheiro.Formatar(p.VencidoCentavos)} · Em compensação {Dinheiro.Formatar(p.EmCompensacaoCentavos)}";

    [RelayCommand] private void Hoje() => Definir(DateTime.Today, DateTime.Today);
    [RelayCommand] private void UltimosSeteDias() => Definir(DateTime.Today.AddDays(-6), DateTime.Today);
    [RelayCommand] private void MesAtual() => Definir(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);

    private void Definir(DateTime de, DateTime ate)
    {
        _definindoPeriodo = true;
        De = de;
        Ate = ate;
        _definindoPeriodo = false;
        _ = CarregarAsync();
    }
}
