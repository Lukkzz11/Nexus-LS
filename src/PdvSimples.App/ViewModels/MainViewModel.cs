using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PdvSimples.App.ViewModels;

/// <summary>
/// ViewModel = a "lógica da tela". Ele guarda os dados que a tela mostra e os comandos dos botões,
/// mas não sabe nada de botões ou janelas. O CommunityToolkit gera o código repetitivo:
/// [ObservableProperty] cria a propriedade pública que avisa a tela quando muda,
/// [RelayCommand] transforma um método em comando que um botão pode chamar.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // O PDV é criado uma vez só: se o operador for olhar o estoque no meio de uma venda, o carrinho continua lá.
    private readonly PdvViewModel _pdv = new(Servicos.Produtos, Servicos.Vendas);

    [ObservableProperty]
    private object? _telaAtual;

    public MainViewModel() => IrParaPdv();

    [RelayCommand]
    private void IrParaPdv() => TelaAtual = _pdv;

    [RelayCommand]
    private void IrParaProdutos() => TelaAtual = new ProdutosViewModel(Servicos.Produtos);

    [RelayCommand]
    private void IrParaVendas() => TelaAtual = new VendasViewModel(Servicos.Vendas);

    [RelayCommand]
    private void IrParaEstoque() => TelaAtual = new EstoqueViewModel(Servicos.Produtos, Servicos.Estoque);
}
