using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>O que uma linha do carrinho precisa pedir ao PDV quando o operador edita um campo.</summary>
public interface IEditorCarrinho
{
    void EditarQuantidade(ItemCarrinhoViewModel item, int quantidade);
    void EditarPrecoUnitario(ItemCarrinhoViewModel item, string texto);
    void EditarTotal(ItemCarrinhoViewModel item, string texto);
}

/// <summary>
/// Uma linha do carrinho na tela. Os dados e as regras ficam na LinhaCarrinho/Carrinho (Core);
/// esta classe só mostra e repassa as edições do operador (quantidade, preço unitário, total da linha).
/// </summary>
public class ItemCarrinhoViewModel : ObservableObject
{
    private readonly IEditorCarrinho _editor;

    public ItemCarrinhoViewModel(LinhaCarrinho linha, IEditorCarrinho editor)
    {
        Linha = linha;
        _editor = editor;
    }

    public LinhaCarrinho Linha { get; }

    public string Nome => Linha.Nome;
    public string TamanhoTexto => Linha.Tamanho == Tamanhos.SemTamanho ? "" : $"  ·  tam. {Linha.Tamanho}";
    public bool PrecoEditado => Linha.PrecoEditado;
    public long TotalCentavos => Linha.TotalCentavos;

    public int Quantidade
    {
        get => Linha.Quantidade;
        set
        {
            if (value != Linha.Quantidade) _editor.EditarQuantidade(this, value);
        }
    }

    public string PrecoUnitarioTexto
    {
        get => Dinheiro.FormatarParaEdicao(Linha.PrecoUnitarioCentavos);
        set => _editor.EditarPrecoUnitario(this, value);
    }

    public string TotalTexto
    {
        get => Dinheiro.FormatarParaEdicao(Linha.TotalCentavos);
        set => _editor.EditarTotal(this, value);
    }

    /// <summary>Manda a tela reler tudo (depois de uma edição que o Carrinho ajustou ou recusou).</summary>
    public void Atualizar() => OnPropertyChanged(string.Empty);
}
