using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>Os itens do menu lateral. O nome do item é também o "Tag" do botão: assim o menu destaca o da tela aberta.</summary>
public enum ItemMenu { Inicio, Pdv, VendaCliente, Caixa, Fechamento, Pedidos, MaisVendidos, Desempenho, EstoqueDaLoja, Fabrica, CalculadoraPrecos, Compras, Fornecedores, Etiquetas, Clientes, Agenda, Financeiro, ContasESaldos, Relatorios, CatalogoOnline, Funcionarios, Atividades, Configuracoes }

/// <summary>
/// ViewModel = a "lógica da tela". Ele guarda os dados que a tela mostra e os comandos dos botões,
/// mas não sabe nada de botões ou janelas. O CommunityToolkit gera o código repetitivo:
/// [ObservableProperty] cria a propriedade pública que avisa a tela quando muda,
/// [RelayCommand] transforma um método em comando que um botão pode chamar.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    // O PDV é criado uma vez só: se o operador for olhar o estoque no meio de uma venda, o carrinho continua lá.
    private readonly ContainerDeServicos _servicos;
    private readonly PdvViewModel _pdv;

    /// <summary>Aviso de versão nova (faixa no alto da janela).</summary>
    public AtualizacaoViewModel Atualizacao => _servicos.Atualizacao;

    /// <summary>Indicador da fila de envio ao catálogo e conta do Firebase (menu lateral).</summary>
    public SincronizacaoViewModel Sincronizacao => _servicos.Sincronizacao;

    [ObservableProperty]
    private object? _telaAtual;

    [ObservableProperty]
    private ItemMenu _itemAtivo;

    public MainViewModel(ContainerDeServicos servicos)
    {
        _servicos = servicos;
        _pdv = new(_servicos.Produtos, _servicos.Vendas, _servicos.Rascunho, _servicos.Clientes, _servicos.Cupons,
            configuracao: _servicos.Configuracao);
        // Abre na tela Início (indicadores e atalhos). Quem preferir o PDV direto liga isso em Configurações.
        // Task.Run: lê a configuração fora da thread da tela, sem risco de travar a abertura.
        var abrirNoPdv = Task.Run(() => _servicos.Configuracao.ObterAsync()).GetAwaiter().GetResult().AbrirNoPdv;
        if (abrirNoPdv && Sessao.Pode(Permissao.Vender)) IrParaPdv(); else IrParaInicio();
        _pdv.TrocarClienteSolicitado += IrParaVendaCliente;
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
            case ItemMenu.VendaCliente: IrParaVendaCliente(); break;
            case ItemMenu.Caixa: IrParaCaixa(); break;
            case ItemMenu.Fechamento: IrParaFechamento(); break;
            case ItemMenu.Pedidos: IrParaPedidos(); break;
            case ItemMenu.MaisVendidos: IrParaMaisVendidos(); break;
            case ItemMenu.Desempenho: IrParaDesempenho(); break;
            case ItemMenu.EstoqueDaLoja: IrParaEstoque(); break;
            case ItemMenu.Fabrica: IrParaFabrica(); break;
            case ItemMenu.CalculadoraPrecos: IrParaCalculadoraPrecos(); break;
            case ItemMenu.Compras: IrParaCompras(); break;
            case ItemMenu.Fornecedores: IrParaFornecedores(); break;
            case ItemMenu.Etiquetas: IrParaEtiquetas(); break;
            case ItemMenu.Clientes: IrParaClientes(); break;
            case ItemMenu.Agenda: IrParaAgenda(); break;
            case ItemMenu.Financeiro: IrParaFinanceiro(); break;
            case ItemMenu.ContasESaldos: IrParaContasESaldos(); break;
            case ItemMenu.Relatorios: IrParaRelatorios(); break;
            case ItemMenu.CatalogoOnline: IrParaCatalogoOnline(); break;
            case ItemMenu.Funcionarios: IrParaFuncionarios(); break;
            case ItemMenu.Atividades: IrParaAtividades(); break;
            case ItemMenu.Configuracoes: IrParaConfiguracoes(); break;
        }
    }

    [RelayCommand] private void IrParaInicio() => Abrir(new InicioViewModel(_servicos.Inicio, _servicos.Backup, Ir), ItemMenu.Inicio);

    // Vendas
    [RelayCommand] private void IrParaPdv() => Abrir(_pdv, ItemMenu.Pdv);
    [RelayCommand]
    private void IrParaVendaCliente() => Abrir(new VendaClienteViewModel(_servicos.Clientes, async cliente =>
    {
        await _pdv.EntrarModoClienteAsync(cliente);
        IrParaPdv(); // o mesmo PDV, agora no modo cliente
    }), ItemMenu.VendaCliente);
    [RelayCommand] private void IrParaCaixa() => Abrir(new VendasViewModel(_servicos.Vendas, _servicos.Cupons, _servicos.Caixa), ItemMenu.Caixa);
    [RelayCommand] private void IrParaFechamento() => Abrir(new FechamentoViewModel(_servicos.Caixa), ItemMenu.Fechamento);
    [RelayCommand] private void IrParaPedidos() => Abrir(new PedidosViewModel(_servicos.Pedidos, _servicos.Produtos, _servicos.Clientes, _servicos.Cupons), ItemMenu.Pedidos);
    [RelayCommand] private void IrParaDesempenho() => Abrir(new DesempenhoVendasViewModel(_servicos.Desempenho), ItemMenu.Desempenho);
    [RelayCommand] private void IrParaMaisVendidos() => Abrir(new MaisVendidosViewModel(_servicos.Relatorios), ItemMenu.MaisVendidos);

    // Estoque
    [RelayCommand] private void IrParaEstoque() => Abrir(new EstoqueLojaViewModel(_servicos.Produtos, _servicos.Estoque), ItemMenu.EstoqueDaLoja);
    [RelayCommand] private void IrParaFabrica() => Abrir(new FabricaViewModel(_servicos.Producao), ItemMenu.Fabrica);
    [RelayCommand] private void IrParaCalculadoraPrecos() => Abrir(new CalculadoraPrecosViewModel(_servicos.Produtos, new NexusLS.Core.Services.ParametrosCalculadoraStore(NexusLS.Core.Services.ParametrosCalculadoraStore.CaminhoPadrao())), ItemMenu.CalculadoraPrecos);
    [RelayCommand] private void IrParaCompras() => Abrir(new ComprasViewModel(_servicos.Compras, _servicos.Produtos, _servicos.Fornecedores), ItemMenu.Compras);
    [RelayCommand] private void IrParaFornecedores() => Abrir(new FornecedoresViewModel(_servicos.Fornecedores), ItemMenu.Fornecedores);
    [RelayCommand] private void IrParaEtiquetas() => Abrir(new EtiquetasViewModel(_servicos.Produtos, _servicos.ModelosEtiqueta), ItemMenu.Etiquetas);

    // Gestão
    [RelayCommand] private void IrParaAgenda() => Abrir(new AgendaViewModel(_servicos.Agenda), ItemMenu.Agenda);
    [RelayCommand] private void IrParaClientes() => Abrir(new ClientesViewModel(_servicos.Clientes), ItemMenu.Clientes);
    [RelayCommand] private void IrParaFinanceiro() => Abrir(new FinanceiroViewModel(_servicos.Financeiro), ItemMenu.Financeiro);
    [RelayCommand] private void IrParaContasESaldos() => Abrir(new ContasESaldosViewModel(_servicos.Posicao, _servicos.ContasFinanceiras, _servicos.Cartoes, _servicos.Cheques), ItemMenu.ContasESaldos);
    [RelayCommand] private void IrParaRelatorios() => Abrir(new RelatoriosViewModel(_servicos.Relatorios), ItemMenu.Relatorios);
    [RelayCommand]
    private void IrParaCatalogoOnline()
    {
        var erp = new ImportacaoErpViewModel(_servicos.ErpConfig, _servicos.ContaErp, _servicos.CriarImportadorErp)
        {
            PedirLogin = () => Views.LoginFirebaseWindow.PedirErp(_servicos.ContaErp, System.Windows.Application.Current?.MainWindow)
        };
        Abrir(new CatalogoOnlineViewModel(_servicos.Firebase, _servicos.Sincronizador, _servicos.SincronizacaoRepo, _servicos.DestinoCatalogo, _servicos.Configuracao, _servicos.Operacional, erp), ItemMenu.CatalogoOnline);
    }
    [RelayCommand] private void IrParaFuncionarios() => Abrir(new FuncionariosViewModel(_servicos.Funcionarios), ItemMenu.Funcionarios);
    [RelayCommand] private void IrParaAtividades() => Abrir(new AtividadesViewModel(_servicos.Atividades), ItemMenu.Atividades);
    [RelayCommand]
    private void IrParaConfiguracoes()
    {
        var dialogos = new NexusLS.App.Backup.DialogosBackupWpf();
        var limpar = new LimparDadosViewModel(_servicos.Limpeza, SouAdministrador, dialogos.Confirmar, () =>
        {
            // O carrinho guardado aponta para produtos que acabaram de ser apagados: sai junto.
            try { File.Delete(NexusLS.Core.Services.RascunhoStore.CaminhoPadrao()); } catch (IOException) { }
            dialogos.Reiniciar();
        });
        Abrir(new ConfiguracoesViewModel(_servicos.Configuracao, _servicos.Cupons, _servicos.Backup, dialogos, limpar, _servicos.Atualizacao), ItemMenu.Configuracoes);
    }

    /// <summary>Apagar dados é só do administrador. Com o login desligado (ninguém cadastrado) vale como administrador.</summary>
    private static bool SouAdministrador() =>
        !Sessao.LoginAtivo || Sessao.Atual?.Perfil == PerfilFuncionario.Administrador;

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
        ItemMenu.Pdv or ItemMenu.VendaCliente => Sessao.Pode(Permissao.Vender),
        ItemMenu.Caixa or ItemMenu.Fechamento => Sessao.Pode(Permissao.Caixa),
        ItemMenu.Pedidos => Sessao.Pode(Permissao.Vender),
        ItemMenu.MaisVendidos or ItemMenu.Desempenho or ItemMenu.Relatorios => Sessao.Pode(Permissao.Relatorios),
        ItemMenu.EstoqueDaLoja or ItemMenu.Etiquetas or ItemMenu.CalculadoraPrecos or ItemMenu.Fabrica => Sessao.Pode(Permissao.Estoque),
        ItemMenu.Compras or ItemMenu.Fornecedores => Sessao.Pode(Permissao.Compras),
        ItemMenu.Clientes => Sessao.Pode(Permissao.Clientes),
        ItemMenu.Agenda => true,
        ItemMenu.Financeiro or ItemMenu.ContasESaldos => Sessao.Pode(Permissao.Financeiro),
        ItemMenu.Funcionarios => Sessao.Pode(Permissao.Funcionarios),
        ItemMenu.Configuracoes or ItemMenu.CatalogoOnline or ItemMenu.Atividades => Sessao.Pode(Permissao.Configuracoes),
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
