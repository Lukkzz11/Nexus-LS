using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class ProdutoItemViewModel : ObservableObject
{
    /// <summary>Opção da lista de tamanhos que manda imprimir uma etiqueta para cada tamanho da grade.</summary>
    public const string TodosOsTamanhos = "Todos os tamanhos";

    public Produto Produto { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _codigoBarras;

    [ObservableProperty]
    private string _tamanhoSelecionado = Tamanhos.SemTamanho;

    [ObservableProperty]
    private int _quantidadeAdicionar = 1;

    [ObservableProperty]
    private bool _temEtiquetaPersonalizada;

    [ObservableProperty]
    private string _nomeEtiquetaPersonalizada = "Padrão do Sistema";

    [ObservableProperty]
    private string _dimensoesEtiquetaPersonalizada = "50 × 30 mm";

    /// <summary>Tamanhos que podem ir para a fila. Produto com grade ganha a opção "Todos os tamanhos".</summary>
    public IReadOnlyList<string> OpcoesTamanho { get; }

    /// <summary>Verdadeiro quando o produto tem pelo menos um tamanho de verdade (P, M, G, 38...).</summary>
    public bool TemGrade { get; }

    /// <summary>Código da etiqueta no tamanho escolhido (EAN-13, ou referência + número do tamanho).</summary>
    public string CodigoBarrasExibicao =>
        CodigoBarrasTamanho.Resolver(Produto, TamanhoParaCodigo) ?? "—";

    private string? TamanhoParaCodigo =>
        string.Equals(TamanhoSelecionado, TodosOsTamanhos, StringComparison.OrdinalIgnoreCase) ? "" : TamanhoSelecionado;

    [RelayCommand]
    public void IncrementarQtd() => QuantidadeAdicionar++;

    [RelayCommand]
    public void DecrementarQtd()
    {
        if (QuantidadeAdicionar > 1) QuantidadeAdicionar--;
    }

    public int Id => Produto.Id;
    public string Nome => Produto.Nome;
    public string? Referencia => Produto.Referencia;
    public string? Categoria => Produto.Categoria;
    public string? CodigoInterno => Produto.CodigoInterno;
    public long PrecoVendaCentavos => Produto.PrecoVendaCentavos;
    public long PrecoAtacadoCentavos => Produto.PrecoAtacadoCentavos;
    public int EstoqueAtual => Produto.EstoqueAtual;

    public string PrecoVarejoFormatado => Dinheiro.Formatar(PrecoVendaCentavos);
    public string PrecoAtacadoFormatado => PrecoAtacadoCentavos > 0 ? Dinheiro.Formatar(PrecoAtacadoCentavos) : "-";

    public bool TemCodigoBarras => !string.IsNullOrWhiteSpace(CodigoBarras);
    public string StatusCodigoTexto => TemCodigoBarras ? "Com Código" : "Sem Código";

    public ProdutoItemViewModel(Produto produto)
    {
        Produto = produto;
        _codigoBarras = produto.CodigoBarras;

        var tamanhos = (produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Where(t => !string.IsNullOrWhiteSpace(t.Tamanho))
            .OrderBy(t => t.Ordem > 0 ? t.Ordem : int.MaxValue)
            .ThenBy(t => t.Tamanho)
            .Select(t => t.Tamanho.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var reais = tamanhos
            .Where(t => !string.Equals(t, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            .ToList();

        TemGrade = reais.Count > 0;
        if (TemGrade)
        {
            var opcoes = new List<string> { TodosOsTamanhos };
            opcoes.AddRange(reais);

            // "Sem tamanho" só aparece quando há estoque nele: senão seria só ruído na lista.
            var semTamanho = produto.Tamanhos?.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(t.Tamanho) &&
                string.Equals(t.Tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase));
            if (semTamanho is { Estoque: > 0 }) opcoes.Add(Tamanhos.SemTamanho);

            OpcoesTamanho = opcoes;
            _tamanhoSelecionado = TodosOsTamanhos;
        }
        else
        {
            OpcoesTamanho = new[] { Tamanhos.SemTamanho };
            _tamanhoSelecionado = Tamanhos.SemTamanho;
        }

        AtualizarInfoEtiqueta();
    }

    partial void OnTamanhoSelecionadoChanged(string value) =>
        OnPropertyChanged(nameof(CodigoBarrasExibicao));

    public void AtualizarInfoEtiqueta()
    {
        var info = GerenciadorEtiquetasProdutoService.ObterInfoEtiqueta(Id);
        TemEtiquetaPersonalizada = info.TemPersonalizada;
        NomeEtiquetaPersonalizada = info.NomeModelo;
        DimensoesEtiquetaPersonalizada = info.Dimensoes;
    }

    public void AtualizarCodigo(string? novoCodigo)
    {
        CodigoBarras = novoCodigo;
        Produto.CodigoBarras = novoCodigo;
        OnPropertyChanged(nameof(TemCodigoBarras));
        OnPropertyChanged(nameof(StatusCodigoTexto));
        OnPropertyChanged(nameof(CodigoBarrasExibicao));
    }
}
