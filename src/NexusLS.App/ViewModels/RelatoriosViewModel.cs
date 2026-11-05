using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma barra do gráfico "vendas por dia": o dia, quanto foi vendido e o tamanho da barra (0 a 100).</summary>
public record DiaDoRelatorio(string Dia, int Vendas, string Total, double Percentual);

/// <summary>Uma linha de "por categoria" com os textos prontos.</summary>
public record LinhaCategoria(string Categoria, int Quantidade, string Faturamento, string Lucro);

/// <summary>Uma linha do demonstrativo (DRE) com os textos prontos. Recuo = linha de detalhe; Destaque = total.</summary>
public record LinhaDre(string Rotulo, string Valor, bool Destaque = false, bool Recuo = false, bool Negativo = false, bool Titulo = false);

/// <summary>Relatório de vendas por período: totais, ticket médio, por forma de pagamento, por dia e a lista das vendas.</summary>
public partial class RelatoriosViewModel : ViewModelBase
{
    private readonly RelatorioService _relatorio;
    private readonly DemonstrativoService? _demonstrativo;
    private int _versao;
    private RelatorioVendas? _atual;

    public RelatoriosViewModel(RelatorioService relatorio, DateTime? hoje = null, DemonstrativoService? demonstrativo = null)
    {
        _relatorio = relatorio;
        _demonstrativo = demonstrativo;
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
    public ObservableCollection<LinhaDre> Dre { get; } = new();
    [ObservableProperty] private string _dreAvisos = "";
    public bool TemDre => _demonstrativo is not null;

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

        if (_demonstrativo is not null)
        {
            var dre = await _demonstrativo.MontarAsync(de, ate);
            if (versao != _versao) return;
            MontarDre(dre);
        }
    });

    private void MontarDre(DemonstrativoFinanceiro d)
    {
        static string R(long c) => Dinheiro.Formatar(c);

        // O cartão do topo usa a MESMA fonte do DRE (o motor, já com abatimentos/acréscimos em título), para a tela nunca mostrar dois lucros.
        LucroTexto = R(d.LucroBrutoCentavos);
        if (d.AjustesTitulosCentavos != 0)
            AvisoLucro = (AvisoLucro.Length == 0 ? "" : AvisoLucro + " ") +
                         $"O lucro bruto inclui {R(d.AjustesTitulosCentavos)} de abatimentos/acréscimos em títulos a prazo (a aba Vendas mostra o lucro só das vendas).";
        Dre.Clear();
        void L(string rotulo, long valor, bool destaque = false, bool recuo = false) =>
            Dre.Add(new LinhaDre(rotulo, R(valor), destaque, recuo, destaque && valor < 0));

        L("Receita bruta (vendas do PDV)", d.ReceitaBrutaCentavos);
        L("(−) Devoluções", d.DevolucoesCentavos, recuo: true);
        L("(+/−) Abatimentos/acréscimos em títulos a prazo", d.AjustesTitulosCentavos, recuo: true);
        L("= Receita líquida", d.ReceitaLiquidaCentavos, destaque: true);
        L("(−) Custo das mercadorias vendidas", d.CustoMercadoriasCentavos, recuo: true);
        Dre.Add(new LinhaDre($"= Lucro bruto (margem {d.MargemBrutaPercentual:0.#}%)", R(d.LucroBrutoCentavos), true, false, d.LucroBrutoCentavos < 0));
        L("(+) Outras receitas", d.OutrasReceitasCentavos, recuo: true);
        L("(−) Despesas operacionais", d.DespesasOperacionaisCentavos, recuo: true);
        foreach (var c in d.DespesasPorCategoria) L(c.Categoria, c.TotalCentavos, recuo: true);
        L("= Resultado do período", d.ResultadoCentavos, destaque: true);

        Dre.Add(new LinhaDre("Entrou ou saiu, mas NÃO é receita nem despesa", "", Titulo: true));
        L("Recebimentos de dívidas de clientes (a venda já contou antes)", d.RecebimentosDeDividasCentavos, recuo: true);
        L("Compras de mercadoria (o custo entra quando a peça é vendida)", d.ComprasMercadoriaCentavos, recuo: true);
        L("Aportes do proprietário", d.AportesCentavos, recuo: true);
        L("Outras entradas", d.OutrasEntradasCentavos, recuo: true);
        L("Retiradas do proprietário", d.RetiradasProprietarioCentavos, recuo: true);
        L("Outras saídas", d.OutrasSaidasCentavos, recuo: true);
        L("Sangrias do caixa", d.RetiradasCaixaCentavos, recuo: true);
        L("Reforços do caixa", d.ReforcosCaixaCentavos, recuo: true);

        Dre.Add(new LinhaDre("Fluxo de caixa (dinheiro que de fato entrou e saiu)", "", Titulo: true));
        L("Entradas realizadas", d.EntradasRealizadasCentavos, recuo: true);
        L("Saídas realizadas", d.SaidasRealizadasCentavos, recuo: true);
        L("= Fluxo líquido", d.FluxoCaixaCentavos, destaque: true);

        DreAvisos = string.Join(Environment.NewLine, d.Avisos ?? Array.Empty<string>());
    }

    private void Limpar()
    {
        _atual = null;
        TotalTexto = QuantidadeTexto = TicketMedioTexto = DescontosTexto = CanceladasTexto = LucroTexto = CustoTexto = AvisoLucro = "";
        PorCategoria.Clear();
        PorDia.Clear();
        PorForma.Clear();
        Vendas.Clear();
        Dre.Clear();
        DreAvisos = "";
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
