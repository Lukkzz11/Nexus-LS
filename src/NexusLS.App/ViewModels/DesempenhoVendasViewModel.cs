using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using static NexusLS.Core.Services.DesempenhoVendas;

namespace NexusLS.App.ViewModels;

/// <summary>Uma barra de gráfico: <c>Fracao</c> vai de 0 a 1 (o maior valor da série = 1).</summary>
public sealed record BarraGrafico(string Rotulo, string Texto, double Fracao);

/// <summary>Um indicador do topo da tela, com a variação contra o período anterior.</summary>
public sealed record Indicador(string Titulo, string Valor, string Variacao, bool Subiu, bool Caiu);

/// <summary>Uma linha dos rankings (mais vendidos, menos vendidos, sem venda).</summary>
public sealed record LinhaRanking(string Nome, string Detalhe);

/// <summary>Uma venda onde apareceu a referência pesquisada.</summary>
public sealed record LinhaVendaBusca(string Data, string Numero, string Quantidade, string Valor);

/// <summary>
/// Desempenho de vendas: indicadores do período contra o anterior, vendas no tempo, rankings e quebras por categoria,
/// forma de pagamento e quem vendeu. Os gráficos são barras desenhadas pela própria tela (sem biblioteca).
/// </summary>
public partial class DesempenhoVendasViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly DesempenhoService _servico;
    private readonly ResumoVendasService? _resumo;
    private ResumoVendas.Completo? _ultimoResumo;
    private ResumoVendas.ResultadoBusca? _ultimaBusca;
    private int _versaoResumo;
    private readonly Func<DateTime> _hoje;
    private int _versao;
    private bool _definindo; // true enquanto um atalho troca período e datas de uma vez (carrega só no fim)

    public DesempenhoVendasViewModel(DesempenhoService servico, Func<DateTime>? hoje = null, ResumoVendasService? resumo = null)
    {
        _servico = servico;
        _resumo = resumo;
        _hoje = hoje ?? (() => DateTime.Today);
        _ate = _hoje().Date;
        _de = _ate.Value.AddDays(-29);
        _tipo = TipoPeriodo.Ultimos30Dias;
        _ = AtualizarAsync();
    }

    public ObservableCollection<Indicador> Indicadores { get; } = new();
    public ObservableCollection<BarraGrafico> VendasNoTempo { get; } = new();
    public ObservableCollection<BarraGrafico> PorCategoriaBarras { get; } = new();
    public ObservableCollection<BarraGrafico> PorFormaBarras { get; } = new();
    public ObservableCollection<BarraGrafico> PorOperadorBarras { get; } = new();
    public ObservableCollection<LinhaRanking> MaisVendidosLista { get; } = new();
    public ObservableCollection<LinhaRanking> MenosVendidosLista { get; } = new();
    public ObservableCollection<LinhaRanking> SemVendasLista { get; } = new();

    // Resumo completo de vendas e pesquisa por referência/produto
    public ObservableCollection<Indicador> ResumoIndicadores { get; } = new();
    public ObservableCollection<LinhaRanking> ResumoFormas { get; } = new();
    public ObservableCollection<LinhaRanking> ResumoDias { get; } = new();
    public ObservableCollection<LinhaRanking> ResumoHorarios { get; } = new();
    public ObservableCollection<LinhaRanking> ResumoPorQuantidade { get; } = new();
    public ObservableCollection<LinhaRanking> ResumoPorValor { get; } = new();
    public ObservableCollection<LinhaRanking> ResumoVendedores { get; } = new();
    public ObservableCollection<LinhaRanking> BuscaVariacoes { get; } = new();
    public ObservableCollection<LinhaVendaBusca> BuscaVendas { get; } = new();

    [ObservableProperty] private string _pesquisaReferencia = "";
    [ObservableProperty] private string _buscaTotais = "";
    [ObservableProperty] private string _buscaAviso = "";
    [ObservableProperty] private string _resumoCancelamentos = "";
    [ObservableProperty] private string _avisoVendedores = "";
    [ObservableProperty] private bool _temBusca;
    [ObservableProperty] private bool _temResumo;

    partial void OnPesquisaReferenciaChanged(string value) => _ = ExecutarAsync(CarregarResumoAsync);

    private const string Todas = "Todas";
    private bool _preenchendoFiltros;

    public ObservableCollection<string> OpcoesCategoria { get; } = new() { Todas };
    public ObservableCollection<string> OpcoesOperador { get; } = new() { Todas };
    public IReadOnlyList<string> OpcoesForma { get; } =
        new[] { Todas }.Concat(SelecaoPagamento.Formas.Select(SelecaoPagamento.Nome)).ToList();

    [ObservableProperty] private string _filtroCategoria = Todas;
    [ObservableProperty] private string _filtroOperador = Todas;
    [ObservableProperty] private string _filtroForma = Todas;
    [ObservableProperty] private string _filtroProduto = "";

    /// <summary>Pergunta onde salvar o CSV (a tela define; nulo = cancelou).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    partial void OnFiltroCategoriaChanged(string value) => FiltroMudou();
    partial void OnFiltroOperadorChanged(string value) => FiltroMudou();
    partial void OnFiltroFormaChanged(string value) => FiltroMudou();
    partial void OnFiltroProdutoChanged(string value) => FiltroMudou();

    private void FiltroMudou()
    {
        if (!_preenchendoFiltros) _ = AtualizarAsync();
    }

    private FiltroDesempenho MontarFiltro() => new(
        FiltroCategoria is null or Todas ? null : FiltroCategoria,
        string.IsNullOrWhiteSpace(FiltroProduto) ? null : FiltroProduto,
        FiltroForma is null or Todas ? null : SelecaoPagamento.Formas.FirstOrDefault(f => SelecaoPagamento.Nome(f) == FiltroForma),
        FiltroOperador is null or Todas ? null : FiltroOperador);

    public bool TemFiltro => !MontarFiltro().Vazio;

    [RelayCommand]
    private void LimparFiltros()
    {
        _preenchendoFiltros = true;
        FiltroCategoria = FiltroOperador = FiltroForma = Todas;
        FiltroProduto = "";
        _preenchendoFiltros = false;
        _ = AtualizarAsync();
    }

    /// <summary>Atualiza as listas de categoria/operador sem perder o que está escolhido.</summary>
    private void AtualizarOpcoes(ObservableCollection<string> alvo, IEnumerable<string> novas, string atual, Action<string> restaurar)
    {
        var lista = new[] { Todas }.Concat(novas).ToList();
        if (atual != Todas && !lista.Contains(atual)) lista.Add(atual);
        if (alvo.SequenceEqual(lista)) return;
        _preenchendoFiltros = true;
        alvo.Clear();
        foreach (var i in lista) alvo.Add(i);
        restaurar(atual);
        _preenchendoFiltros = false;
    }

    /// <summary>Salva em CSV as linhas vendidas do período, respeitando os filtros da tela.</summary>
    [RelayCommand]
    private Task ExportarAsync() => ExecutarAsync(async () =>
    {
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        var periodo = Resolver(TipoPeriodo.Personalizado, _hoje(), de, ate);
        var linhas = await _servico.LinhasAsync(periodo, MontarFiltro());
        if (linhas.Count == 0)
            throw new RegraDeNegocioException("Não há vendas neste período e com estes filtros para exportar.");

        var caminho = EscolherArquivoParaSalvar($"desempenho-{de:yyyy-MM-dd}_a_{ate:yyyy-MM-dd}.csv");
        if (caminho is null) return;
        ExportadorCsv.Salvar(caminho, ExportadorCsv.Desempenho(linhas));
        MostrarSucesso($"Arquivo salvo: {caminho} ({linhas.Count} linha(s)).");
    });

    [ObservableProperty] private TipoPeriodo _tipo;
    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;
    [ObservableProperty] private Granularidade _granularidade = Granularidade.Dia;
    [ObservableProperty] private string _textoPeriodo = "";
    [ObservableProperty] private string _avisoLucro = "";
    [ObservableProperty] private string _avisoOperador = "";
    [ObservableProperty] private bool _temVendas;

    public bool PorDia { get => Granularidade == Granularidade.Dia; set { if (value) Granularidade = Granularidade.Dia; } }
    public bool PorSemana { get => Granularidade == Granularidade.Semana; set { if (value) Granularidade = Granularidade.Semana; } }
    public bool PorMes { get => Granularidade == Granularidade.Mes; set { if (value) Granularidade = Granularidade.Mes; } }

    partial void OnGranularidadeChanged(Granularidade value)
    {
        OnPropertyChanged(nameof(PorDia)); OnPropertyChanged(nameof(PorSemana)); OnPropertyChanged(nameof(PorMes));
        _ = AtualizarAsync();
    }

    partial void OnDeChanged(DateTime? value) { if (!_definindo) { Tipo = TipoPeriodo.Personalizado; _ = AtualizarAsync(); } }
    partial void OnAteChanged(DateTime? value) { if (!_definindo) { Tipo = TipoPeriodo.Personalizado; _ = AtualizarAsync(); } }

    private void Escolher(TipoPeriodo tipo)
    {
        var p = Resolver(tipo, _hoje());
        _definindo = true;
        Tipo = tipo; De = p.Inicio; Ate = p.Fim;
        _definindo = false;
        _ = AtualizarAsync();
    }

    [RelayCommand] private void Hoje() => Escolher(TipoPeriodo.Hoje);
    [RelayCommand] private void UltimosSeteDias() => Escolher(TipoPeriodo.Ultimos7Dias);
    [RelayCommand] private void UltimosTrintaDias() => Escolher(TipoPeriodo.Ultimos30Dias);
    [RelayCommand] private void MesAtual() => Escolher(TipoPeriodo.MesAtual);

    [RelayCommand]
    private Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        var periodo = Resolver(TipoPeriodo.Personalizado, _hoje(), de, ate);

        var r = await _servico.GerarAsync(periodo, Granularidade, MontarFiltro());
        if (versao != _versao) return; // chegou uma resposta mais nova

        Mensagem = "";
        AtualizarOpcoes(OpcoesCategoria, r.Categorias, FiltroCategoria, v => FiltroCategoria = v);
        AtualizarOpcoes(OpcoesOperador, r.Operadores, FiltroOperador, v => FiltroOperador = v);
        OnPropertyChanged(nameof(TemFiltro));
        TextoPeriodo = $"{r.Periodo.Inicio:dd/MM/yyyy} a {r.Periodo.Fim:dd/MM/yyyy} ({r.Periodo.Dias} dia(s)), comparado com {r.Anterior.Inicio:dd/MM} a {r.Anterior.Fim:dd/MM}";
        TemVendas = r.Atual.Vendas > 0;

        Indicadores.Clear();
        Indicadores.Add(Montar("Faturamento", Dinheiro.Formatar(r.Atual.FaturamentoCentavos), Variacao(r.Atual.FaturamentoCentavos, r.Antes.FaturamentoCentavos)));
        Indicadores.Add(Montar("Vendas", r.Atual.Vendas.ToString(PtBr), Variacao(r.Atual.Vendas, r.Antes.Vendas)));
        Indicadores.Add(Montar("Peças vendidas", r.Atual.Pecas.ToString(PtBr), Variacao(r.Atual.Pecas, r.Antes.Pecas)));
        Indicadores.Add(Montar("Ticket médio", Dinheiro.Formatar(r.Atual.TicketMedioCentavos), Variacao(r.Atual.TicketMedioCentavos, r.Antes.TicketMedioCentavos)));
        Indicadores.Add(r.Atual.LucroCentavos is long lucro
            ? Montar("Lucro estimado", $"{Dinheiro.Formatar(lucro)} ({r.Atual.MargemPct?.ToString("0.#", PtBr)}%)",
                r.Antes.LucroCentavos is long antes ? Variacao(lucro, antes) : null)
            : new Indicador("Lucro estimado", "—", "", false, false));
        AvisoLucro = r.Atual.LucroCentavos is null && r.Atual.Vendas > 0
            ? $"O lucro não é mostrado porque o custo é conhecido em só {(r.Atual.CoberturaCusto * 100).ToString("0", PtBr)}% do valor vendido. Cadastre o custo dos produtos."
            : "";

        Preencher(VendasNoTempo, r.Serie.Select(p => (p.Rotulo, (decimal)p.FaturamentoCentavos, $"{Dinheiro.Formatar(p.FaturamentoCentavos)} · {p.Vendas} venda(s)")));
        Preencher(PorCategoriaBarras, r.PorCategoria.Select(g => (g.Categoria, (decimal)g.ValorCentavos, $"{Dinheiro.Formatar(g.ValorCentavos)} · {g.Pecas} peça(s)")));
        Preencher(PorFormaBarras, r.PorForma.Select(g => (SelecaoPagamento.Nome(g.Forma), (decimal)g.ValorCentavos, $"{Dinheiro.Formatar(g.ValorCentavos)} · {g.Vendas} venda(s)")));
        if (r.PorOperador is { } ops)
        {
            Preencher(PorOperadorBarras, ops.Select(g => (g.Operador, (decimal)g.ValorCentavos, $"{Dinheiro.Formatar(g.ValorCentavos)} · {g.Vendas} venda(s)")));
            AvisoOperador = "";
        }
        else
        {
            PorOperadorBarras.Clear();
            AvisoOperador = r.Atual.Vendas > 0 ? "Ninguém foi identificado nas vendas deste período, então não dá para separar por quem vendeu." : "";
        }

        Preencher(MaisVendidosLista, r.MaisVendidos.Select(p => new LinhaRanking(p.Nome, $"{p.Pecas} peça(s) · {Dinheiro.Formatar(p.ValorCentavos)}")));
        Preencher(MenosVendidosLista, r.MenosVendidos.Select(p => new LinhaRanking(p.Nome, $"{p.Pecas} peça(s) · {Dinheiro.Formatar(p.ValorCentavos)}")));
        Preencher(SemVendasLista, r.SemVendas.Select(p => new LinhaRanking(p.Nome, $"{p.Categoria} · estoque {p.Estoque}")));
        await CarregarResumoAsync();
    });

    /// <summary>Resumo completo (sem os filtros acima) e pesquisa por referência, do período escolhido.</summary>
    private async Task CarregarResumoAsync()
    {
        if (_resumo is null || De is not DateTime de || Ate is not DateTime ate) return;
        var versao = ++_versaoResumo;
        var periodo = Resolver(TipoPeriodo.Personalizado, _hoje(), de, ate);
        var (r, busca) = await _resumo.GerarAsync(periodo, PesquisaReferencia);
        if (versao != _versaoResumo) return;
        _ultimoResumo = r;
        _ultimaBusca = busca;
        TemResumo = r.Vendas.Atual > 0 || r.Canceladas > 0;

        ResumoIndicadores.Clear();
        ResumoIndicadores.Add(Montar("Total vendido", Dinheiro.Formatar(r.Total.Atual), r.Total.VariacaoPct));
        ResumoIndicadores.Add(Montar("Número de vendas", r.Vendas.Atual.ToString(PtBr), r.Vendas.VariacaoPct));
        ResumoIndicadores.Add(Montar("Ticket médio", Dinheiro.Formatar(r.TicketMedio.Atual), r.TicketMedio.VariacaoPct));
        ResumoIndicadores.Add(Montar("Peças vendidas", r.Pecas.Atual.ToString(PtBr), r.Pecas.VariacaoPct));
        ResumoIndicadores.Add(new Indicador("Descontos concedidos", Dinheiro.Formatar(r.DescontosCentavos), "", false, false));
        ResumoIndicadores.Add(new Indicador("Período anterior", Dinheiro.Formatar(r.Total.Anterior),
            $"{r.Anterior.Inicio:dd/MM} a {r.Anterior.Fim:dd/MM}", false, false));
        ResumoCancelamentos = r.Canceladas > 0
            ? $"{r.Canceladas} venda(s) cancelada(s), {Dinheiro.Formatar(r.CanceladasCentavos)}, fora de todos os totais acima."
            : "Nenhuma venda cancelada no período.";

        var pt = PtBr.DateTimeFormat;
        Preencher(ResumoFormas, r.PorForma.Select(f => new LinhaRanking(SelecaoPagamento.Nome(f.Forma), $"{Dinheiro.Formatar(f.ValorCentavos)} · {f.Vendas} venda(s)")));
        Preencher(ResumoDias, r.MelhoresDias.Select(d => new LinhaRanking(Maiuscula(pt.GetDayName(d.Dia)), $"{Dinheiro.Formatar(d.ValorCentavos)} · {d.Vendas} venda(s)")));
        Preencher(ResumoHorarios, r.MelhoresHorarios.Select(h => new LinhaRanking($"{h.Hora:00}h às {h.Hora:00}h59", $"{Dinheiro.Formatar(h.ValorCentavos)} · {h.Vendas} venda(s)")));
        Preencher(ResumoPorQuantidade, r.MaisVendidosPorQuantidade.Select(p => new LinhaRanking(p.Nome, $"{p.Pecas} peça(s) · {Dinheiro.Formatar(p.ValorCentavos)}")));
        Preencher(ResumoPorValor, r.MaisVendidosPorValor.Select(p => new LinhaRanking(p.Nome, $"{Dinheiro.Formatar(p.ValorCentavos)} · {p.Pecas} peça(s)")));
        Preencher(ResumoVendedores, (r.PorVendedor ?? Array.Empty<ResumoVendas.VendedorTotal>()).Select(v => new LinhaRanking(v.Vendedor, $"{Dinheiro.Formatar(v.ValorCentavos)} · {v.Vendas} venda(s)")));
        AvisoVendedores = r.PorVendedor is null && r.Vendas.Atual > 0 ? "Ninguém foi identificado nas vendas deste período." : "";

        BuscaVariacoes.Clear();
        BuscaVendas.Clear();
        TemBusca = busca.Achou;
        if (string.IsNullOrWhiteSpace(PesquisaReferencia)) { BuscaTotais = ""; BuscaAviso = ""; }
        else if (!busca.Achou) { BuscaTotais = ""; BuscaAviso = $"Nenhuma venda de \"{PesquisaReferencia.Trim()}\" neste período."; }
        else
        {
            BuscaAviso = "";
            BuscaTotais = $"{busca.Unidades} unidade(s) vendida(s) · {Dinheiro.Formatar(busca.ValorCentavos)} · {busca.Vendas.Select(v => v.VendaId).Distinct().Count()} venda(s)";
            foreach (var x in busca.Variacoes)
                BuscaVariacoes.Add(new LinhaRanking(x.Tamanho.Length == 0 ? "Sem tamanho" : $"Tamanho {x.Tamanho}", $"{x.Unidades} un. · {Dinheiro.Formatar(x.ValorCentavos)}"));
            foreach (var x in busca.Vendas)
                BuscaVendas.Add(new LinhaVendaBusca($"{x.DataHora:dd/MM/yyyy HH:mm}", $"Venda {x.VendaId}", $"{x.Quantidade} un.", Dinheiro.Formatar(x.ValorCentavos)));
        }
    }

    private static string Maiuscula(string t) => t.Length == 0 ? t : char.ToUpper(t[0], PtBr) + t[1..];

    /// <summary>Salva em CSV o resumo completo do período (e a pesquisa por referência, se houver).</summary>
    [RelayCommand]
    private Task ExportarResumoAsync() => ExecutarAsync(async () =>
    {
        if (_resumo is null) throw new RegraDeNegocioException("O resumo de vendas não está disponível.");
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        if (_ultimoResumo is null) await CarregarResumoAsync();
        var r = _ultimoResumo;
        if (r is null || (r.Vendas.Atual == 0 && r.Canceladas == 0))
            throw new RegraDeNegocioException("Não há vendas neste período para exportar o resumo.");

        var caminho = EscolherArquivoParaSalvar($"resumo-vendas-{de:yyyy-MM-dd}_a_{ate:yyyy-MM-dd}.csv");
        if (caminho is null) return;
        ExportadorCsv.Salvar(caminho, ExportadorCsv.ResumoDeVendas(r, _ultimaBusca));
        MostrarSucesso($"Resumo salvo: {caminho}");
    });

    private static Indicador Montar(string titulo, string valor, decimal? variacao) => variacao is decimal v
        ? new Indicador(titulo, valor, $"{(v > 0 ? "▲" : v < 0 ? "▼" : "=")} {Math.Abs(v).ToString("0.#", PtBr)}% sobre o período anterior", v > 0, v < 0)
        : new Indicador(titulo, valor, "sem base de comparação", false, false);

    private static decimal? Variacao(long atual, long anterior) => DesempenhoVendas.Variacao(atual, anterior);

    private static void Preencher<T>(ObservableCollection<T> alvo, IEnumerable<T> itens)
    {
        alvo.Clear();
        foreach (var i in itens) alvo.Add(i);
    }

    private static void Preencher(ObservableCollection<BarraGrafico> alvo, IEnumerable<(string Rotulo, decimal Valor, string Texto)> pontos)
    {
        var lista = pontos.ToList();
        var maior = lista.Count == 0 ? 0 : lista.Max(p => p.Valor);
        alvo.Clear();
        foreach (var p in lista)
            alvo.Add(new BarraGrafico(p.Rotulo, p.Texto, maior > 0 ? (double)(p.Valor / maior) : 0));
    }
}
