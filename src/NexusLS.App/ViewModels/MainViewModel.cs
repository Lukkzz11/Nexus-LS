using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>Os itens do menu lateral. O nome do item é também o "Tag" do botão: assim o menu destaca o da tela aberta.</summary>
public enum ItemMenu { Inicio, Pdv, Caixa, Fechamento, MaisVendidos, EstoqueDaLoja, Compras, Fornecedores, Etiquetas, Clientes, Financeiro, Relatorios, CatalogoOnline, Funcionarios, Configuracoes }

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
        if (abrirNoPdv && Sessao.Pode(Permissao.Vender)) IrParaPdv(); else IrParaInicio();
        Sessao.Mudou += AtualizarPermissoes;
        _ = _pdv.RestaurarRascunhoAsync(); // se a venda anterior ficou no meio, ela volta
    }

    /// <summary>Usado pelos atalhos da tela Início: abre a tela do item pedido.</summary>
    public void Ir(ItemMenu item)
    {
        if (!Permitido(item)) return;
        switch (item)
        {
            case ItemMenu.Inicio: IrParaInicio(); break;
            case ItemMenu.Pdv: IrParaPdv(); break;
            case ItemMenu.Caixa: IrParaCaixa(); break;
            case ItemMenu.Fechamento: IrParaFechamento(); break;
            case ItemMenu.MaisVendidos: IrParaMaisVendidos(); break;
            case ItemMenu.EstoqueDaLoja: IrParaEstoque(); break;
            case ItemMenu.Compras: IrParaCompras(); break;
            case ItemMenu.Fornecedores: IrParaFornecedores(); break;
            case ItemMenu.Etiquetas: IrParaEtiquetas(); break;
            case ItemMenu.Clientes: IrParaClientes(); break;
            case ItemMenu.Financeiro: IrParaFinanceiro(); break;
            case ItemMenu.Relatorios: IrParaRelatorios(); break;
            case ItemMenu.CatalogoOnline: IrParaCatalogoOnline(); break;
            case ItemMenu.Funcionarios: IrParaFuncionarios(); break;
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
    [RelayCommand] private void IrParaCompras() => Abrir(new ComprasViewModel(Servicos.Compras, Servicos.Produtos, Servicos.Fornecedores), ItemMenu.Compras);
    [RelayCommand] private void IrParaFornecedores() => Abrir(new FornecedoresViewModel(Servicos.Fornecedores), ItemMenu.Fornecedores);
    [RelayCommand] private void IrParaEtiquetas() => Abrir(new EtiquetasViewModel(Servicos.Produtos, Servicos.ModelosEtiqueta), ItemMenu.Etiquetas);

    // Gestão
    [RelayCommand] private void IrParaClientes() => Abrir(new ClientesViewModel(Servicos.Clientes), ItemMenu.Clientes);
    [RelayCommand] private void IrParaFinanceiro() => Abrir(new FinanceiroViewModel(Servicos.Financeiro), ItemMenu.Financeiro);
    [RelayCommand] private void IrParaRelatorios() => Abrir(new RelatoriosViewModel(Servicos.Relatorios), ItemMenu.Relatorios);
    [RelayCommand] private void IrParaCatalogoOnline() => Abrir(new CatalogoOnlineViewModel(Servicos.Firebase, Servicos.Sincronizador, Servicos.SincronizacaoRepo, Servicos.DestinoCatalogo, Servicos.Configuracao), ItemMenu.CatalogoOnline);
    [RelayCommand] private void IrParaFuncionarios() => Abrir(new FuncionariosViewModel(Servicos.Funcionarios), ItemMenu.Funcionarios);
    [RelayCommand] private void IrParaConfiguracoes() => Abrir(new ConfiguracoesViewModel(Servicos.Configuracao, Servicos.Cupons, Servicos.Backup, new NexusLS.App.Backup.DialogosBackupWpf()), ItemMenu.Configuracoes);

    private void Abrir(object tela, ItemMenu item)
    {
        if (!Permitido(item)) return; // defesa extra: o menu já esconde o que não é permitido
        TelaAtual = tela;
        ItemAtivo = item;
    }

    // ================= permissões =================

    /// <summary>Qual permissão cada item do menu exige. O Início é de todos.</summary>
    private static bool Permitido(ItemMenu item) => item switch
    {
        ItemMenu.Inicio => true,
        ItemMenu.Pdv => Sessao.Pode(Permissao.Vender),
        ItemMenu.Caixa or ItemMenu.Fechamento => Sessao.Pode(Permissao.Caixa),
        ItemMenu.MaisVendidos or ItemMenu.Relatorios => Sessao.Pode(Permissao.Relatorios),
        ItemMenu.EstoqueDaLoja or ItemMenu.Etiquetas => Sessao.Pode(Permissao.Estoque),
        ItemMenu.Compras or ItemMenu.Fornecedores => Sessao.Pode(Permissao.Compras),
        ItemMenu.Clientes => Sessao.Pode(Permissao.Clientes),
        ItemMenu.Financeiro => Sessao.Pode(Permissao.Financeiro),
        ItemMenu.Funcionarios => Sessao.Pode(Permissao.Funcionarios),
        ItemMenu.Configuracoes or ItemMenu.CatalogoOnline => Sessao.Pode(Permissao.Configuracoes),
        _ => false
    };

    public bool VerPdv => Sessao.Pode(Permissao.Vender);
    public bool VerCaixa => Sessao.Pode(Permissao.Caixa);
    public bool VerEstoque => Sessao.Pode(Permissao.Estoque);
    public bool VerCompras => Sessao.Pode(Permissao.Compras);
    public bool VerClientes => Sessao.Pode(Permissao.Clientes);
    public bool VerFinanceiro => Sessao.Pode(Permissao.Financeiro);
    public bool VerRelatorios => Sessao.Pode(Permissao.Relatorios);
    public bool VerConfiguracoes => Sessao.Pode(Permissao.Configuracoes);
    public bool VerFuncionarios => Sessao.Pode(Permissao.Funcionarios);

    // Os títulos de grupo do menu só aparecem se sobrou algum item dentro.
    public bool VerSecaoVendas => VerPdv || VerCaixa || VerRelatorios;
    public bool VerSecaoEstoque => VerEstoque || VerCompras;
    public bool VerSecaoGestao => VerClientes || VerFinanceiro || VerRelatorios || VerFuncionarios || VerConfiguracoes;

    public bool TemUsuario => Sessao.LoginAtivo && Sessao.Atual is not null;
    public string UsuarioTexto => Sessao.Atual is { } f ? $"{f.Nome} · {Permissoes.Nome(f.Perfil)}" : "";

    /// <summary>A janela de entrada (a MainWindow preenche). Devolve quem entrou ou nulo se desistiu.</summary>
    public Func<Funcionario?> PedirLogin { get; set; } = () => null;

    [RelayCommand]
    private void TrocarUsuario()
    {
        var f = PedirLogin();
        if (f is null) return;
        Sessao.Entrar(f);
        IrParaInicio(); // cada pessoa recomeça pelo Início
    }

    private void AtualizarPermissoes()
    {
        foreach (var nome in new[]
        {
            nameof(VerPdv), nameof(VerCaixa), nameof(VerEstoque), nameof(VerCompras), nameof(VerClientes), nameof(VerFinanceiro),
            nameof(VerRelatorios), nameof(VerConfiguracoes), nameof(VerFuncionarios), nameof(VerSecaoVendas), nameof(VerSecaoEstoque),
            nameof(VerSecaoGestao), nameof(TemUsuario), nameof(UsuarioTexto)
        }) OnPropertyChanged(nome);
    }
}
