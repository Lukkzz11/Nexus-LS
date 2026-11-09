using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class ProdutoItemViewModel : ObservableObject
{
    public Produto Produto { get; }

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private string? _codigoBarras;

    [ObservableProperty]
    private int _quantidadeAdicionar = 1;

    [ObservableProperty]
    private bool _temEtiquetaPersonalizada;

    [ObservableProperty]
    private string _nomeEtiquetaPersonalizada = "Padrão do Sistema";

    [ObservableProperty]
    private string _dimensoesEtiquetaPersonalizada = "50 × 30 mm";

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
        AtualizarInfoEtiqueta();
    }

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
    }
}
