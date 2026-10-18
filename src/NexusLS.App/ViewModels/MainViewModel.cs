using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexusLS.App.ViewModels;

/// <summary>Os itens do menu lateral. O nome do item é também o "Tag" do botão: assim o menu destaca o da tela aberta.</summary>
public enum ItemMenu { Pdv, Caixa, Fechamento, MaisVendidos, EstoqueDaLoja, Etiquetas, Clientes, Relatorios, Configuracoes }

/// <summary>
/// ViewModel = a "lógica da tela". Ele guarda os dados que a tela mostra e os comandos dos botões,
/// mas não sabe nada de botões ou janelas. O CommunityToolkit gera o código repetitivo:
/// [ObservableProperty] cria a propriedade pública que avisa a tela quando muda,
/// [RelayCommand] transforma um método em comando que um botão pode chamar.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // O PDV é criado uma vez só: se o operador for olhar o estoque no meio de uma venda, o carrinho continua lá.
    private readonly PdvViewModel _pdv =
        new(Servicos.Produtos, Servicos.Vendas, Servicos.Rascunho, Servicos.Clientes, Servicos.Cupons);

    [ObservableProperty]
    private object? _telaAtual;

    [ObservableProperty]
    private ItemMenu _itemAtivo;

    public MainViewModel()
    {
        IrParaPdv();
        _ = _pdv.RestaurarRascunhoAsync(); // se a venda anterior ficou no meio, ela volta
    }

    // Vendas
    [RelayCommand] private void IrParaPdv() => Abrir(_pdv, ItemMenu.Pdv);
    [RelayCommand] private void IrParaCaixa() => Abrir(new VendasViewModel(Servicos.Vendas, Servicos.Cupons, Servicos.Caixa), ItemMenu.Caixa);
    [RelayCommand] private void IrParaFechamento() => Abrir(new FechamentoViewModel(Servicos.Caixa), ItemMenu.Fechamento);
    [RelayCommand] private void IrParaMaisVendidos() => Abrir(new MaisVendidosViewModel(Servicos.Relatorios), ItemMenu.MaisVendidos);

    // Estoque
    [RelayCommand] private void IrParaEstoque() => Abrir(new EstoqueLojaViewModel(Servicos.Produtos, Servicos.Estoque), ItemMenu.EstoqueDaLoja);
    [RelayCommand] private void IrParaEtiquetas() => Abrir(new EtiquetasViewModel(Servicos.Produtos), ItemMenu.Etiquetas);

    // Gestão
    [RelayCommand] private void IrParaClientes() => Abrir(new ClientesViewModel(Servicos.Clientes), ItemMenu.Clientes);
    [RelayCommand] private void IrParaRelatorios() => Abrir(new RelatoriosViewModel(Servicos.Relatorios), ItemMenu.Relatorios);
    [RelayCommand] private void IrParaConfiguracoes() => Abrir(new ConfiguracoesViewModel(Servicos.Configuracao, Servicos.Cupons), ItemMenu.Configuracoes);

    private void Abrir(object tela, ItemMenu item)
    {
        TelaAtual = tela;
        ItemAtivo = item;
    }
}
