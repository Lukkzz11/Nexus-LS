using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public sealed record LinhaFormaFinanceiro(string Forma, string Vendas, string Entradas, string Saidas);
public sealed record LinhaDiaFinanceiro(string Dia, string Vendas, string Entradas, string Saidas, string SaldoDoDia, string Acumulado);
public sealed record LinhaCategoriaFinanceira2(string Categoria, string Valor, string Quantidade);

/// <summary>
/// Relatório financeiro: entradas, saídas e saldo de um período, por forma de pagamento e por dia, com CSV.
/// Só quem tem a permissão Financeiro vê valores; sem ela a tela explica e não carrega nada.
/// </summary>
public partial class RelatorioFinanceiroViewModel : PeriodoViewModelBase
{
    private readonly RelatorioFinanceiroService _servico;
    private readonly Func<bool> _podeVer;
    private RelatorioFinanceiro.Resultado? _ultimo;
    private int _versao;

    public RelatorioFinanceiroViewModel(RelatorioFinanceiroService servico, Func<DateTime>? hoje = null, Func<bool>? podeVerValores = null)
        : base(hoje)
    {
        _servico = servico;
        _podeVer = podeVerValores ?? (() => Sessao.Pode(Permissao.Financeiro));
        Carregamento = AtualizarAsync();
    }

    public ObservableCollection<CartaoNumero> Resumo { get; } = new();
    public ObservableCollection<LinhaFormaFinanceiro> PorForma { get; } = new();
    public ObservableCollection<LinhaDiaFinanceiro> PorDia { get; } = new();
    public ObservableCollection<LinhaCategoriaFinanceira2> EntradasPorCategoria { get; } = new();
    public ObservableCollection<LinhaCategoriaFinanceira2> SaidasPorCategoria { get; } = new();

    [ObservableProperty] private string _textoPeriodo = "";
    [ObservableProperty] private bool _semPermissao;
    [ObservableProperty] private bool _temMovimento;
    [ObservableProperty] private string _avisoDevolucao = "";

    /// <summary>Pergunta onde salvar o CSV (a tela define; nulo = cancelou).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    protected override void PeriodoMudou() => Carregamento = AtualizarAsync();

    [RelayCommand]
    private Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (!_podeVer())
        {
            Limpar();
            SemPermissao = true;
            MostrarErro("Você não tem permissão para ver os valores financeiros. Peça a um gerente ou administrador.");
            return;
        }
        SemPermissao = false;
        var periodo = PeriodoEscolhido();
        var r = await _servico.GerarAsync(periodo);
        if (versao != _versao) return; // chegou uma resposta mais nova
        _ultimo = r;
        MostrarSucesso("");
        TextoPeriodo = TextoDoPeriodo(r.Periodo);

        Resumo.Clear();
        Resumo.Add(new CartaoNumero("Entradas", Reais(r.TotalEntradasCentavos), $"{r.NumeroDeVendas} venda(s) + entradas lançadas"));
        Resumo.Add(new CartaoNumero("Saídas", Reais(r.TotalSaidasCentavos), "saídas lançadas"));
        Resumo.Add(new CartaoNumero("Saldo do período", Reais(r.SaldoCentavos), "entradas menos saídas", r.SaldoCentavos < 0));
        Resumo.Add(new CartaoNumero("Vendas (faturamento)", Reais(r.VendasCentavos), $"líquido de devoluções: {Reais(r.FaturamentoLiquidoCentavos)}"));
        AvisoDevolucao = r.DevolucoesCentavos > 0
            ? $"Devoluções por vale-troca no período: {Reais(r.DevolucoesCentavos)}. Não mexem no saldo (saiu um vale, não dinheiro)."
            : "";

        Trocar(PorForma, r.PorFormaPagamento.Select(f => new LinhaFormaFinanceiro(f.Nome, Reais(f.VendasCentavos), Reais(f.EntradasCentavos), Reais(f.SaidasCentavos))));
        Trocar(PorDia, r.PorDiaDoPeriodo.Select(d => new LinhaDiaFinanceiro(d.Dia.ToString("dd/MM/yyyy", PtBr), Reais(d.VendasCentavos),
            Reais(d.EntradasLancadasCentavos), Reais(d.SaidasCentavos), Reais(d.SaldoDoDiaCentavos), Reais(d.SaldoAcumuladoCentavos))));
        Trocar(EntradasPorCategoria, r.EntradasPorCategoria.Select(c => new LinhaCategoriaFinanceira2(c.Categoria, Reais(c.ValorCentavos), $"{c.Lancamentos} lançamento(s)")));
        Trocar(SaidasPorCategoria, r.SaidasPorCategoria.Select(c => new LinhaCategoriaFinanceira2(c.Categoria, Reais(c.ValorCentavos), $"{c.Lancamentos} lançamento(s)")));
        TemMovimento = r.NumeroDeVendas > 0 || r.EntradasLancadasCentavos > 0 || r.SaidasLancadasCentavos > 0;
    });

    private void Limpar()
    {
        _ultimo = null;
        Resumo.Clear(); PorForma.Clear(); PorDia.Clear(); EntradasPorCategoria.Clear(); SaidasPorCategoria.Clear();
        TemMovimento = false; AvisoDevolucao = ""; TextoPeriodo = "";
    }

    private static void Trocar<T>(ObservableCollection<T> alvo, IEnumerable<T> novos)
    {
        alvo.Clear();
        foreach (var n in novos) alvo.Add(n);
    }

    /// <summary>Salva o relatório do período mostrado em CSV (abre no Excel).</summary>
    [RelayCommand]
    private Task ExportarAsync() => ExecutarAsync(() =>
    {
        if (!_podeVer()) throw new RegraDeNegocioException("Você não tem permissão para ver os valores financeiros.");
        if (_ultimo is null || !TemMovimento) throw new RegraDeNegocioException("Não há movimento neste período para exportar.");
        var p = _ultimo.Periodo;
        var caminho = EscolherArquivoParaSalvar($"relatorio-financeiro-{p.Inicio:yyyy-MM-dd}_a_{p.Fim:yyyy-MM-dd}.csv");
        if (caminho is null) return Task.CompletedTask;
        ExportadorCsv.Salvar(caminho, RelatorioFinanceiro.ParaCsv(_ultimo));
        MostrarSucesso($"Arquivo salvo: {caminho}");
        return Task.CompletedTask;
    });
}
