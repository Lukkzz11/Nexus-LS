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
    [ObservableProperty]
    private object? _telaAtual;

    public MainViewModel() => IrParaProdutos();

    [RelayCommand]
    private void IrParaProdutos() => TelaAtual = new ProdutosViewModel(Servicos.Produtos);
}
