using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexusLS.App.ViewModels;

/// <summary>Os itens do menu lateral que já têm tela. O menu destaca o item da tela aberta.</summary>
public enum ItemMenu { Pdv, Caixa, MaisVendidos, EstoqueDaLoja, Configuracoes }

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
    [NotifyPropertyChangedFor(nameof(PdvAtivo))]
    [NotifyPropertyChangedFor(nameof(CaixaAtivo))]
    [NotifyPropertyChangedFor(nameof(MaisVendidosAtivo))]
    [NotifyPropertyChangedFor(nameof(EstoqueAtivo))]
    [NotifyPropertyChangedFor(nameof(ConfiguracoesAtivo))]
    private ItemMenu _itemAtivo;

    public bool PdvAtivo => ItemAtivo == ItemMenu.Pdv;
    public bool CaixaAtivo => ItemAtivo == ItemMenu.Caixa;
    public bool MaisVendidosAtivo => ItemAtivo == ItemMenu.MaisVendidos;
    public bool EstoqueAtivo => ItemAtivo == ItemMenu.EstoqueDaLoja;
    public bool ConfiguracoesAtivo => ItemAtivo == ItemMenu.Configuracoes;

    public MainViewModel()
    {
        IrParaPdv();
        _ = _pdv.RestaurarRascunhoAsync(); // se a venda anterior ficou no meio, ela volta
    }

    [RelayCommand]
    private void IrParaPdv() => Abrir(_pdv, ItemMenu.Pdv);

    [RelayCommand]
    private void IrParaCaixa() => Abrir(new VendasViewModel(Servicos.Vendas, Servicos.Cupons), ItemMenu.Caixa);

    [RelayCommand]
    private void IrParaMaisVendidos() => Abrir(new MaisVendidosViewModel(Servicos.Relatorios), ItemMenu.MaisVendidos);

    [RelayCommand]
    private void IrParaEstoque() => Abrir(new EstoqueLojaViewModel(Servicos.Produtos, Servicos.Estoque), ItemMenu.EstoqueDaLoja);

    [RelayCommand]
    private void IrParaConfiguracoes() =>
        Abrir(new ConfiguracoesViewModel(Servicos.Configuracao, Servicos.Cupons), ItemMenu.Configuracoes);

    private void Abrir(object tela, ItemMenu item)
    {
        TelaAtual = tela;
        ItemAtivo = item;
    }
}
