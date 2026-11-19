using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Um cartão com um número grande (faturamento, saldo...). <c>Negativo</c> pinta o valor de vermelho.</summary>
public sealed record CartaoNumero(string Titulo, string Valor, string Detalhe = "", bool Negativo = false);

/// <summary>
/// O que as telas da Gestão com "período" têm em comum: de/até, os atalhos (hoje, 7 dias, este mês, mês anterior) e as
/// mensagens de erro. A tela recarrega sozinha quando o período muda.
/// </summary>
public abstract partial class PeriodoViewModelBase : ViewModelBase
{
    protected static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly Func<DateTime> _relogio;
    private bool _definindoPeriodo;

    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;

    protected PeriodoViewModelBase(Func<DateTime>? hoje)
    {
        _relogio = hoje ?? (() => DateTime.Today);
        var h = _relogio().Date;
        _definindoPeriodo = true;
        De = new DateTime(h.Year, h.Month, 1);
        Ate = h;
        _definindoPeriodo = false;
    }

    /// <summary>A última carga de dados (para os testes esperarem a tela terminar de carregar).</summary>
    public Task Carregamento { get; protected set; } = Task.CompletedTask;

    protected DateTime AgoraData => _relogio().Date;

    /// <summary>Chamado quando o usuário muda o período (datas ou atalhos). A tela filha recarrega aqui.</summary>
    protected abstract void PeriodoMudou();

    partial void OnDeChanged(DateTime? value) { if (!_definindoPeriodo) PeriodoMudou(); }
    partial void OnAteChanged(DateTime? value) { if (!_definindoPeriodo) PeriodoMudou(); }

    protected void DefinirPeriodo(DateTime de, DateTime ate)
    {
        _definindoPeriodo = true;
        De = de.Date;
        Ate = ate.Date;
        _definindoPeriodo = false;
        PeriodoMudou();
    }

    /// <summary>O período escolhido (datas inclusas). Se as datas vieram trocadas, ajeita. Sem data, explica.</summary>
    protected DesempenhoVendas.Intervalo PeriodoEscolhido()
    {
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        return de.Date <= ate.Date ? new DesempenhoVendas.Intervalo(de.Date, ate.Date) : new DesempenhoVendas.Intervalo(ate.Date, de.Date);
    }

    [RelayCommand] private void Hoje() => DefinirPeriodo(AgoraData, AgoraData);
    [RelayCommand] private void UltimosSeteDias() => DefinirPeriodo(AgoraData.AddDays(-6), AgoraData);
    [RelayCommand] private void MesAtual() => DefinirPeriodo(new DateTime(AgoraData.Year, AgoraData.Month, 1), AgoraData);

    [RelayCommand]
    private void MesAnterior()
    {
        var primeiro = new DateTime(AgoraData.Year, AgoraData.Month, 1).AddMonths(-1);
        DefinirPeriodo(primeiro, primeiro.AddMonths(1).AddDays(-1));
    }

    protected static string Reais(long centavos) => Dinheiro.Formatar(centavos);

    protected static string Percentual(decimal? valor) => valor is decimal v ? v.ToString("0.0", PtBr) + "%" : "sem dados";

    protected string TextoDoPeriodo(DesempenhoVendas.Intervalo p) =>
        p.Inicio == p.Fim ? p.Inicio.ToString("dd/MM/yyyy", PtBr) : $"{p.Inicio:dd/MM/yyyy} a {p.Fim:dd/MM/yyyy} ({p.Dias} dias)";
}
