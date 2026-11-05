using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Models;

public partial class ItemFila : ObservableObject
{
    public Produto Produto { get; }

    [ObservableProperty]
    private int _quantidade = 1;

    [ObservableProperty]
    private string _tamanho = "";

    public string Nome => Produto.Nome;
    public string? Referencia => Produto.Referencia;
    public string? CodigoInterno => Produto.CodigoInterno;
    public string? CodigoBarras => Produto.CodigoBarras;
    public long PrecoVarejoCentavos => Produto.PrecoVendaCentavos;
    public long PrecoAtacadoCentavos => Produto.PrecoAtacadoCentavos;

    public string PrecoVarejoFormatado => Dinheiro.Formatar(PrecoVarejoCentavos);
    public string PrecoAtacadoFormatado => PrecoAtacadoCentavos > 0 ? Dinheiro.Formatar(PrecoAtacadoCentavos) : "-";

    public ItemFila(Produto produto, int quantidade = 1, string tamanho = "")
    {
        Produto = produto;
        _quantidade = Math.Max(1, quantidade);
        _tamanho = tamanho;
    }
}
