using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha do carrinho na tela do PDV. A quantidade é editável, então ela avisa a tela quando muda.</summary>
public partial class ItemCarrinhoViewModel : ObservableObject
{
    public ItemCarrinhoViewModel(Produto produto, int quantidade)
    {
        ProdutoId = produto.Id;
        Nome = produto.Nome;
        PrecoCentavos = produto.PrecoVendaCentavos;
        EstoqueDisponivel = produto.EstoqueAtual;
        _quantidade = quantidade;
    }

    public int ProdutoId { get; }
    public string Nome { get; }
    public long PrecoCentavos { get; }

    /// <summary>Quanto havia em estoque quando o produto foi buscado; limita a quantidade no carrinho.</summary>
    public int EstoqueDisponivel { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalCentavos))]
    private int _quantidade;

    public long TotalCentavos => PrecoCentavos * Quantidade;
}
