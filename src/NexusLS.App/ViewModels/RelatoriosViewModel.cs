using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma barra do gráfico "vendas por dia": o dia, quanto foi vendido e o tamanho da barra (0 a 100).</summary>
public record DiaDoRelatorio(string Dia, int Vendas, string Total, double Percentual);

/// <summary>Uma linha de "por categoria" com os textos prontos.</summary>
public record LinhaCategoria(string Categoria, int Quantidade, string Faturamento, string Lucro);

/// <summary>Relatório de vendas por período: totais, ticket médio, por forma de pagamento, por dia e a lista das vendas.</summary>
public partial class RelatoriosViewModel : ViewModelBase
{
    private readonly RelatorioService _relatorio;
    private int _versao;
    private RelatorioVendas? _atual;

    public RelatoriosViewModel(RelatorioService relatorio, DateTime? hoje = null)
    {
        _relatorio = relatorio;
        var dia = (hoje ?? DateTime.Today).Date;
        _de = new DateTime(dia.Year, dia.Month, 1); // campos diretos: não disparam o carregamento duas vezes
        _ate = dia;
        _ = CarregarAsync();
    }

    /// <summary>A tela define a caixa "Salvar como" (ver VendasViewModel).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    public ObservableCollection<DiaDoRelatorio> PorDia { get; } = new();
    public ObservableCollection<ResumoForma> PorForma { get; } = new();
    public ObservableCollection<LinhaCategoria> PorCategoria { get; } = new();
    public ObservableCollection<Venda> Vendas { get; } = new();

    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;

    [ObservableProperty] private string _totalTexto = "";
    [ObservableProperty] private string _quantidadeTexto = "";
    [ObservableProperty] private string _ticketMedioTexto = "";
    [ObservableProperty] private string _descontosTexto = "";
    [ObservableProperty] private string _canceladasTexto = "";
    [ObservableProperty] private string _lucroTexto = "";
    [ObservableProperty] private string _custoTexto = "";
    [ObservableProperty] private string _avisoLucro = "";

    private bool _definindoPeriodo; // true enquanto um atalho troca as duas datas de uma vez (carrega só no fim)

    partial void OnDeChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnAteChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");

        RelatorioVendas relatorio;
        try
        {
            relatorio = await _relatorio.VendasPorPeriodoAsync(de, ate);
        }
        catch (RegraDeNegocioException)
        {
            if (versao == _versao) Limpar();
            throw; // a mensagem (ex.: data final antes da inicial) aparece na tela
        }
        if (versao != _versao) return;

        _atual = relatorio;
        Mensagem = "";

        TotalTexto = Dinheiro.Formatar(relatorio.TotalCentavos);
        QuantidadeTexto = relatorio.QuantidadeVendas.ToString();
        TicketMedioTexto = Dinheiro.Formatar(relatorio.TicketMedioCentavos);
        DescontosTexto = Dinheiro.Formatar(relatorio.DescontosCentavos);
        CanceladasTexto = relatorio.QuantidadeCanceladas.ToString();

        LucroTexto = Dinheiro.Formatar(relatorio.LucroBrutoCentavos);
        CustoTexto = Dinheiro.Formatar(relatorio.CustoTotalCentavos);
        AvisoLucro = relatorio.CustoEstimado
            ? "Lucro estimado: parte das vendas é de antes do registro do custo e usou o custo atual do produto."
            : "";

        PorCategoria.Clear();
        foreach (var c in relatorio.PorCategoria ?? Array.Empty<ResumoCategoria>())
            PorCategoria.Add(new LinhaCategoria(c.Categoria, c.Quantidade, Dinheiro.Formatar(c.FaturamentoCentavos),
                Dinheiro.Formatar(c.LucroCentavos)));

        PorForma.Clear();
        foreach (var f in relatorio.PorForma) PorForma.Add(f);

        var maior = relatorio.PorDia.Count == 0 ? 0 : relatorio.PorDia.Max(d => d.TotalCentavos);
        PorDia.Clear();
        foreach (var d in relatorio.PorDia)
            PorDia.Add(new DiaDoRelatorio($"{d.Dia:dd/MM}", d.Vendas, Dinheiro.Formatar(d.TotalCentavos),
                maior == 0 ? 0 : 100.0 * d.TotalCentavos / maior));

        Vendas.Clear();
        foreach (var v in relatorio.Vendas.Reverse()) Vendas.Add(v); // mais recentes primeiro
    });

    private void Limpar()
    {
        _atual = null;
        TotalTexto = QuantidadeTexto = TicketMedioTexto = DescontosTexto = CanceladasTexto = LucroTexto = CustoTexto = AvisoLucro = "";
        PorCategoria.Clear();
        PorDia.Clear();
        PorForma.Clear();
        Vendas.Clear();
    }

    // Atalhos de período
    [RelayCommand] private void Hoje() => Definir(DateTime.Today, DateTime.Today);
    [RelayCommand] private void UltimosSeteDias() => Definir(DateTime.Today.AddDays(-6), DateTime.Today);
    [RelayCommand] private void MesAtual() => Definir(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);

    [RelayCommand]
    private void MesAnterior()
    {
        var primeiro = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(-1);
        Definir(primeiro, primeiro.AddMonths(1).AddDays(-1));
    }

    private void Definir(DateTime de, DateTime ate)
    {
        _definindoPeriodo = true;
        De = de;
        Ate = ate;
        _definindoPeriodo = false;
        _ = CarregarAsync();
    }

    /// <summary>Salva as vendas do período num CSV (uma linha por venda).</summary>
    [RelayCommand]
    private Task ExportarVendasAsync() => ExportarAsync("vendas", ExportadorCsv.Vendas);

    /// <summary>Salva os itens vendidos no período num CSV (uma linha por item).</summary>
    [RelayCommand]
    private Task ExportarItensAsync() => ExportarAsync("itens", ExportadorCsv.Itens);

    private Task ExportarAsync(string tipo, Func<IEnumerable<Venda>, string> gerar) => ExecutarAsync(() =>
    {
        if (_atual is null || _atual.Vendas.Count == 0)
            throw new RegraDeNegocioException("Não há vendas neste período para exportar.");

        var caminho = EscolherArquivoParaSalvar($"{tipo}-{_atual.De:yyyy-MM-dd}_a_{_atual.Ate:yyyy-MM-dd}.csv");
        if (caminho is null) return Task.CompletedTask; // a pessoa desistiu na caixa "Salvar como"

        ExportadorCsv.Salvar(caminho, gerar(_atual.Vendas));
        MostrarSucesso($"Arquivo salvo: {caminho}");
        return Task.CompletedTask;
    });
}
