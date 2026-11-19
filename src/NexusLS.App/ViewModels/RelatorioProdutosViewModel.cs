using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public sealed record LinhaProdutoVendido(string Posicao, string Nome, string Referencia, string Categoria, string Pecas, string Faturamento,
    string Percentual, string Margem, string Classe);
public sealed record LinhaCategoriaVendida(string Categoria, string Pecas, string Faturamento, string Percentual, string Margem);
public sealed record LinhaTamanhoVendido(string Tamanho, string Pecas, string Faturamento);
public sealed record LinhaProdutoParado(string Nome, string Referencia, string Categoria, string Estoque, string UltimaVenda, string DiasSemVenda, string CustoParado);

/// <summary>
/// Relatório de produtos: mais vendidos (com a curva ABC), por categoria, por tamanho e estoque parado, com CSV de cada
/// parte. Mostra "sem dados" onde o custo não é conhecido (melhor do que um lucro errado).
/// </summary>
public partial class RelatorioProdutosViewModel : PeriodoViewModelBase
{
    private readonly RelatorioProdutosService _servico;
    private readonly Func<bool> _podeVer;
    private RelatorioProdutos.Resultado? _ultimo;
    private List<RelatorioProdutos.ProdutoParado> _parados = new();
    private int _versao;

    public RelatorioProdutosViewModel(RelatorioProdutosService servico, Func<DateTime>? hoje = null, Func<bool>? podeVerValores = null)
        : base(hoje)
    {
        _servico = servico;
        _podeVer = podeVerValores ?? (() => Sessao.Pode(Permissao.Relatorios));
        Carregamento = AtualizarAsync();
    }

    public ObservableCollection<CartaoNumero> Resumo { get; } = new();
    public ObservableCollection<LinhaProdutoVendido> MaisVendidos { get; } = new();
    public ObservableCollection<LinhaCategoriaVendida> PorCategoria { get; } = new();
    public ObservableCollection<LinhaTamanhoVendido> PorTamanho { get; } = new();
    public ObservableCollection<LinhaProdutoParado> Parados { get; } = new();

    [ObservableProperty] private string _textoPeriodo = "";
    [ObservableProperty] private string _diasParadoTexto = RelatorioProdutos.DiasParadoPadrao.ToString();
    [ObservableProperty] private bool _semPermissao;
    [ObservableProperty] private bool _temVendas;
    [ObservableProperty] private string _avisoAbc = "";
    [ObservableProperty] private string _resumoParados = "";

    /// <summary>Pergunta onde salvar o CSV (a tela define; nulo = cancelou).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    protected override void PeriodoMudou() => Carregamento = AtualizarAsync();

    partial void OnDiasParadoTextoChanged(string value) => Carregamento = AtualizarAsync();

    private int DiasParado()
    {
        if (!int.TryParse((DiasParadoTexto ?? "").Trim(), out var dias) || dias < 1 || dias > 3650)
            throw new RegraDeNegocioException("Em \"parados há quantos dias\", digite um número de 1 a 3650.");
        return dias;
    }

    [RelayCommand]
    private Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (!_podeVer())
        {
            _ultimo = null; _parados = new();
            Resumo.Clear(); MaisVendidos.Clear(); PorCategoria.Clear(); PorTamanho.Clear(); Parados.Clear();
            TemVendas = false; SemPermissao = true;
            MostrarErro("Você não tem permissão para ver este relatório (ele mostra custo e lucro). Peça a um gerente ou administrador.");
            return;
        }
        SemPermissao = false;
        var periodo = PeriodoEscolhido();
        var dias = DiasParado();
        var r = await _servico.GerarAsync(periodo);
        var parados = await _servico.ParadosAsync(AgoraData, dias);
        if (versao != _versao) return;
        _ultimo = r;
        _parados = parados;
        MostrarSucesso("");
        TextoPeriodo = TextoDoPeriodo(r.Periodo);
        TemVendas = r.Pecas > 0;

        var custoParado = RelatorioProdutos.CustoTotalParado(parados);
        Resumo.Clear();
        Resumo.Add(new CartaoNumero("Faturamento", Reais(r.FaturamentoCentavos), $"{r.Pecas} peça(s) vendida(s)"));
        Resumo.Add(new CartaoNumero("Lucro estimado", r.LucroCentavos is long l ? Reais(l) : "sem dados",
            r.MargemPct is decimal m ? $"margem {Percentual(m)}" : "falta o custo de alguns produtos", r.LucroCentavos is < 0));
        Resumo.Add(new CartaoNumero("Produtos diferentes", r.MaisVendidos.Count.ToString(PtBr), "vendidos no período"));
        Resumo.Add(new CartaoNumero("Estoque parado", custoParado is long c ? Reais(c) : "sem dados",
            $"{parados.Count} produto(s) sem venda há {dias} dia(s) ou mais"));

        Trocar(MaisVendidos, r.MaisVendidos.Select(p => new LinhaProdutoVendido(p.Posicao.ToString(PtBr), p.Nome, p.Referencia ?? "", p.Categoria,
            p.Pecas.ToString(PtBr), Reais(p.ValorCentavos), Percentual(p.PercentualDoFaturamento), Percentual(p.MargemPct), p.ClasseAbc.ToString())));
        Trocar(PorCategoria, r.PorCategoria.Select(c2 => new LinhaCategoriaVendida(c2.Categoria, c2.Pecas.ToString(PtBr), Reais(c2.ValorCentavos),
            Percentual(c2.PercentualDoFaturamento), Percentual(c2.MargemPct))));
        Trocar(PorTamanho, r.PorTamanho.Select(t => new LinhaTamanhoVendido(t.Tamanho, t.Pecas.ToString(PtBr), Reais(t.ValorCentavos))));
        Trocar(Parados, parados.Select(p => new LinhaProdutoParado(p.Nome, p.Referencia ?? "", p.Categoria, p.Estoque.ToString(PtBr),
            p.UltimaVenda is DateTime d ? d.ToString("dd/MM/yyyy", PtBr) : "nunca vendeu", p.DiasSemVenda is int n ? n.ToString(PtBr) : "—",
            p.CustoParadoCentavos is long v ? Reais(v) : "sem custo")));

        AvisoAbc = r.MaisVendidos.Count == 0 ? "" :
            "Curva ABC: A = produtos que somam até 80% do faturamento, B = os próximos 15%, C = o restante.";
        ResumoParados = parados.Count == 0 ? $"Nenhum produto com estoque está parado há {dias} dia(s) ou mais." :
            $"{parados.Count} produto(s) com estoque sem venda há {dias} dia(s) ou mais" + (custoParado is long cp ? $", {Reais(cp)} em custo parado." : ".");
    });

    private static void Trocar<T>(ObservableCollection<T> alvo, IEnumerable<T> novos)
    {
        alvo.Clear();
        foreach (var n in novos) alvo.Add(n);
    }

    private Task Exportar(string nome, Func<string> csv, bool tem, string semDados) => ExecutarAsync(() =>
    {
        if (!_podeVer()) throw new RegraDeNegocioException("Você não tem permissão para ver este relatório.");
        if (_ultimo is null || !tem) throw new RegraDeNegocioException(semDados);
        var caminho = EscolherArquivoParaSalvar($"{nome}-{_ultimo.Periodo.Inicio:yyyy-MM-dd}_a_{_ultimo.Periodo.Fim:yyyy-MM-dd}.csv");
        if (caminho is null) return Task.CompletedTask;
        ExportadorCsv.Salvar(caminho, csv());
        MostrarSucesso($"Arquivo salvo: {caminho}");
        return Task.CompletedTask;
    });

    [RelayCommand] private Task ExportarMaisVendidosAsync() =>
        Exportar("mais-vendidos", () => RelatorioProdutos.MaisVendidosCsv(_ultimo!), _ultimo?.MaisVendidos.Count > 0, "Não há vendas neste período para exportar.");
    [RelayCommand] private Task ExportarCategoriasAsync() =>
        Exportar("por-categoria", () => RelatorioProdutos.PorCategoriaCsv(_ultimo!), _ultimo?.PorCategoria.Count > 0, "Não há vendas neste período para exportar.");
    [RelayCommand] private Task ExportarTamanhosAsync() =>
        Exportar("por-tamanho", () => RelatorioProdutos.PorTamanhoCsv(_ultimo!), _ultimo?.PorProdutoETamanho.Count > 0, "Não há vendas neste período para exportar.");
    [RelayCommand] private Task ExportarParadosAsync() =>
        Exportar("produtos-parados", () => RelatorioProdutos.ParadosCsv(_parados), _parados.Count > 0, "Não há produtos parados para exportar.");
}
