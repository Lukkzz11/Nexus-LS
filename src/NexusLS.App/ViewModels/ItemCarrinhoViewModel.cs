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
    /// <summary>Produto com tamanho: a linha mostra só "tam. P" e o nome fica no cabeçalho do grupo.</summary>
    public bool ModoGrade => Linha.Tamanho != Tamanhos.SemTamanho;
    public string RotuloLinha => ModoGrade ? $"      tam. {Linha.Tamanho}" : Linha.Nome;

    private bool _primeiroDoGrupo;
    private string _resumoGrupo = "";
    private long _totalGrupo;

    /// <summary>Cabeçalho "Camiseta · ref. 1435 — P×1, M×1" aparece só na primeira linha de cada produto.</summary>
    public bool MostrarCabecalho => ModoGrade && _primeiroDoGrupo;
    public string CabecalhoGrupo => string.IsNullOrEmpty(Linha.Referencia)
        ? $"{Linha.Nome}  —  {_resumoGrupo}"
        : $"{Linha.Nome}  ·  ref. {Linha.Referencia}  —  {_resumoGrupo}";
    public long TotalGrupoCentavos => _totalGrupo;

    public void DefinirGrupo(bool primeiro, string resumo, long total)
    {
        _primeiroDoGrupo = primeiro;
        _resumoGrupo = resumo;
        _totalGrupo = total;
    }

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
