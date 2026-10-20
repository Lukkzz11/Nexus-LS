using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace NexusLS.App.ViewModels;

/// <summary>Os itens do menu lateral. O nome do item é também o "Tag" do botão: assim o menu destaca o da tela aberta.</summary>
public enum ItemMenu { Inicio, Pdv, Caixa, Fechamento, MaisVendidos, EstoqueDaLoja, Etiquetas, Clientes, Financeiro, Relatorios, CatalogoOnline, Configuracoes }

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
        new(Servicos.Produtos, Servicos.Vendas, Servicos.Rascunho, Servicos.Clientes, Servicos.Cupons,
            configuracao: Servicos.Configuracao);

    [ObservableProperty]
    private object? _telaAtual;

    [ObservableProperty]
    private ItemMenu _itemAtivo;

    public MainViewModel()
    {
        // Abre na tela Início (indicadores e atalhos). Quem preferir o PDV direto liga isso em Configurações.
        // Task.Run: lê a configuração fora da thread da tela, sem risco de travar a abertura.
        var abrirNoPdv = Task.Run(() => Servicos.Configuracao.ObterAsync()).GetAwaiter().GetResult().AbrirNoPdv;
        if (abrirNoPdv) IrParaPdv(); else IrParaInicio();
        _ = _pdv.RestaurarRascunhoAsync(); // se a venda anterior ficou no meio, ela volta
    }

    /// <summary>Usado pelos atalhos da tela Início: abre a tela do item pedido.</summary>
    public void Ir(ItemMenu item)
    {
        switch (item)
        {
            case ItemMenu.Inicio: IrParaInicio(); break;
            case ItemMenu.Pdv: IrParaPdv(); break;
            case ItemMenu.Caixa: IrParaCaixa(); break;
            case ItemMenu.Fechamento: IrParaFechamento(); break;
            case ItemMenu.MaisVendidos: IrParaMaisVendidos(); break;
            case ItemMenu.EstoqueDaLoja: IrParaEstoque(); break;
            case ItemMenu.Etiquetas: IrParaEtiquetas(); break;
            case ItemMenu.Clientes: IrParaClientes(); break;
            case ItemMenu.Financeiro: IrParaFinanceiro(); break;
            case ItemMenu.Relatorios: IrParaRelatorios(); break;
            case ItemMenu.CatalogoOnline: IrParaCatalogoOnline(); break;
            case ItemMenu.Configuracoes: IrParaConfiguracoes(); break;
        }
    }

    [RelayCommand] private void IrParaInicio() => Abrir(new InicioViewModel(Servicos.Inicio, Servicos.Backup, Ir), ItemMenu.Inicio);

    // Vendas
    [RelayCommand] private void IrParaPdv() => Abrir(_pdv, ItemMenu.Pdv);
    [RelayCommand] private void IrParaCaixa() => Abrir(new VendasViewModel(Servicos.Vendas, Servicos.Cupons, Servicos.Caixa), ItemMenu.Caixa);
    [RelayCommand] private void IrParaFechamento() => Abrir(new FechamentoViewModel(Servicos.Caixa), ItemMenu.Fechamento);
    [RelayCommand] private void IrParaMaisVendidos() => Abrir(new MaisVendidosViewModel(Servicos.Relatorios), ItemMenu.MaisVendidos);

    // Estoque
    [RelayCommand] private void IrParaEstoque() => Abrir(new EstoqueLojaViewModel(Servicos.Produtos, Servicos.Estoque), ItemMenu.EstoqueDaLoja);
    [RelayCommand] private void IrParaEtiquetas() => Abrir(new EtiquetasViewModel(Servicos.Produtos, Servicos.ModelosEtiqueta), ItemMenu.Etiquetas);

    // Gestão
    [RelayCommand] private void IrParaClientes() => Abrir(new ClientesViewModel(Servicos.Clientes), ItemMenu.Clientes);
    [RelayCommand] private void IrParaFinanceiro() => Abrir(new FinanceiroViewModel(Servicos.Financeiro), ItemMenu.Financeiro);
    [RelayCommand] private void IrParaRelatorios() => Abrir(new RelatoriosViewModel(Servicos.Relatorios), ItemMenu.Relatorios);
    [RelayCommand] private void IrParaCatalogoOnline() => Abrir(new CatalogoOnlineViewModel(Servicos.Firebase, Servicos.Sincronizador, Servicos.SincronizacaoRepo, Servicos.DestinoCatalogo, Servicos.Configuracao), ItemMenu.CatalogoOnline);
    [RelayCommand] private void IrParaConfiguracoes() => Abrir(new ConfiguracoesViewModel(Servicos.Configuracao, Servicos.Cupons, Servicos.Backup, new NexusLS.App.Backup.DialogosBackupWpf()), ItemMenu.Configuracoes);

    private void Abrir(object tela, ItemMenu item)
    {
        TelaAtual = tela;
        ItemAtivo = item;
    }
}
