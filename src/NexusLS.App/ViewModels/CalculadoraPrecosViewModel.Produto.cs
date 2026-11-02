using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Liga a calculadora aos produtos: puxar o custo de um produto e aplicar o preço calculado nele.</summary>
public partial class CalculadoraPrecosViewModel
{
    private readonly ProdutoService? _produtos;
    private long? _precoCalculado;

    private readonly ParametrosCalculadoraStore? _parametros;

    public CalculadoraPrecosViewModel(ProdutoService? produtos = null, ParametrosCalculadoraStore? parametros = null)
    {
        _produtos = produtos;
        _parametros = parametros;
        CarregarParametros();
    }

    /// <summary>Os parâmetros que valem para a loja toda (o custo e o frete mudam a cada peça e ficam de fora).</summary>
    private Dictionary<string, string> LerParametros() => new()
    {
        [nameof(EmbalagemReais)] = EmbalagemReais, [nameof(OutrosCustosPct)] = OutrosCustosPct, [nameof(ImpostosPct)] = ImpostosPct,
        [nameof(ComissaoPct)] = ComissaoPct, [nameof(OutrasVariaveisPct)] = OutrasVariaveisPct, [nameof(OutrasVariaveisReais)] = OutrasVariaveisReais,
        [nameof(TaxaPagamentoPct)] = TaxaPagamentoPct, [nameof(FixasPct)] = FixasPct, [nameof(FixasMensais)] = FixasMensais,
        [nameof(PecasPorMes)] = PecasPorMes, [nameof(MargemPct)] = MargemPct, [nameof(TerminarEm90)] = TerminarEm90.ToString(),
        [nameof(FormasPagamento)] = FormasPagamento, [nameof(Quantidades)] = Quantidades
    };

    private void CarregarParametros()
    {
        if (_parametros is null) return;
        var v = _parametros.Carregar();
        string Pega(string chave, string padrao) => v.TryGetValue(chave, out var x) ? x : padrao;
        EmbalagemReais = Pega(nameof(EmbalagemReais), EmbalagemReais); OutrosCustosPct = Pega(nameof(OutrosCustosPct), OutrosCustosPct);
        ImpostosPct = Pega(nameof(ImpostosPct), ImpostosPct); ComissaoPct = Pega(nameof(ComissaoPct), ComissaoPct);
        OutrasVariaveisPct = Pega(nameof(OutrasVariaveisPct), OutrasVariaveisPct); OutrasVariaveisReais = Pega(nameof(OutrasVariaveisReais), OutrasVariaveisReais);
        TaxaPagamentoPct = Pega(nameof(TaxaPagamentoPct), TaxaPagamentoPct); FixasPct = Pega(nameof(FixasPct), FixasPct);
        FixasMensais = Pega(nameof(FixasMensais), FixasMensais); PecasPorMes = Pega(nameof(PecasPorMes), PecasPorMes);
        MargemPct = Pega(nameof(MargemPct), MargemPct); TerminarEm90 = bool.TryParse(Pega(nameof(TerminarEm90), ""), out var b) && b;
        FormasPagamento = Pega(nameof(FormasPagamento), FormasPagamento); Quantidades = Pega(nameof(Quantidades), Quantidades);
    }

    /// <summary>Guarda os parâmetros da loja para as próximas vezes.</summary>
    [RelayCommand]
    private Task SalvarParametrosAsync() => ExecutarAsync(() =>
    {
        if (_parametros is null) throw new RegraDeNegocioException("Não há onde guardar os parâmetros neste modo.");
        _parametros.Salvar(LerParametros());
        MostrarSucesso("Parâmetros guardados. Na próxima vez a calculadora abre com eles (o custo e o frete não são guardados).");
        return Task.CompletedTask;
    });

    /// <summary>A tela define como perguntar "tem certeza?" (assim o ViewModel não depende de MessageBox).</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    public bool TemProdutos => _produtos is not null;
    public ObservableCollection<Produto> ProdutosAchados { get; } = new();

    [ObservableProperty] private string _produtoBusca = "";
    [ObservableProperty] private Produto? _produtoEscolhido;
    [ObservableProperty] private string _produtoInfo = "";

    [RelayCommand]
    private Task BuscarProdutoAsync() => ExecutarAsync(async () =>
    {
        if (_produtos is null) return;
        var achados = await _produtos.BuscarAsync(ProdutoBusca);
        ProdutosAchados.Clear();
        foreach (var p in achados.Take(8)) ProdutosAchados.Add(p);
        ProdutoEscolhido = ProdutosAchados.FirstOrDefault();
        if (ProdutosAchados.Count == 0) MostrarErro("Nenhum produto encontrado.");
    });

    /// <summary>Preenche o custo e os preços da calculadora com os do produto escolhido.</summary>
    [RelayCommand]
    private Task UsarProdutoAsync() => ExecutarAsync(() =>
    {
        if (ProdutoEscolhido is not { } p)
        {
            MostrarErro("Busque e escolha um produto.");
            return Task.CompletedTask;
        }

        CustoCompra = p.PrecoCustoCentavos > 0 ? Dinheiro.FormatarParaEdicao(p.PrecoCustoCentavos) : "";
        var venda = p.PrecoVendaCentavos > 0 ? Dinheiro.FormatarParaEdicao(p.PrecoVendaCentavos) : "";
        PrecoOriginal = PrecoPagamento = PrecoQuantidades = venda;
        _precoCalculado = null;
        ProdutoInfo = $"{p.Nome}: custo {Dinheiro.Formatar(p.PrecoCustoCentavos)}, venda atual {Dinheiro.Formatar(p.PrecoVendaCentavos)}.";
        MostrarSucesso("Custo e preço do produto carregados. Informe as despesas e calcule.");
        return Task.CompletedTask;
    });

    /// <summary>Grava o preço calculado (aba Preço) como preço de venda do produto. Só o preço muda.</summary>
    [RelayCommand]
    private Task AplicarPrecoAsync() => ExecutarAsync(async () =>
    {
        if (_produtos is null || ProdutoEscolhido is not { } p)
        {
            MostrarErro("Escolha o produto em \"Usar um produto\" antes de aplicar o preço.");
            return;
        }
        if (_precoCalculado is not long preco)
        {
            MostrarErro("Calcule o preço na aba Preço primeiro.");
            return;
        }
        if (!Confirmar($"Mudar o preço de venda de \"{p.Nome}\" de {Dinheiro.Formatar(p.PrecoVendaCentavos)} para {Dinheiro.Formatar(preco)}?"))
            return;

        await _produtos.AplicarPrecoVendaAsync(p.Id, preco);
        p.PrecoVendaCentavos = preco;
        ProdutoInfo = $"{p.Nome}: custo {Dinheiro.Formatar(p.PrecoCustoCentavos)}, venda atual {Dinheiro.Formatar(preco)}.";
        MostrarSucesso($"Preço de \"{p.Nome}\" atualizado para {Dinheiro.Formatar(preco)}.");
    });
}
