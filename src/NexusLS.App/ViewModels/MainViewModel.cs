using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>Os itens do menu lateral. O nome do item é também o "Tag" do botão: assim o menu destaca o da tela aberta.</summary>
public enum ItemMenu { Inicio, Pdv, VendaCliente, Caixa, Pedidos, MaisVendidos, Desempenho, EstoqueDaLoja, ResumoEstoque, Fabrica, CalculadoraPrecos, Compras, Fornecedores, Etiquetas, Clientes, Agenda, EditorCatalogo, Financeiro, ContasESaldos, Relatorios, CatalogoOnline, Funcionarios, Atividades, Configuracoes, Trocas, RelatorioFinanceiro, RelatorioProdutos, GestaoEmpresa, EntradasESaidas, Gaveta }

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

    /// <summary>Fator de tamanho das telas (1,0 = normal), vindo das Preferências.</summary>
    [ObservableProperty] private double _zoom = 1.0;

    [ObservableProperty]
    private ItemMenu _itemAtivo;

    public MainViewModel(ContainerDeServicos servicos)
    {
        _servicos = servicos;
        Zoom = _servicos.Preferencias.Carregar().ZoomPercentual / 100.0;
        _pdv = new(_servicos.Produtos, _servicos.Vendas, _servicos.Rascunho, _servicos.Clientes, _servicos.Cupons,
            configuracao: _servicos.Configuracao, caixa: _servicos.Caixa);
        _pdv.Pix = _servicos.Pix;
        _pdv.PixRepo = _servicos.PixRepo;
        _pdv.Trocas = _servicos.Trocas;
        _pdv.PixAutomaticoAtivo = () => _servicos.PagamentosConfig.Ler().TarifaMercadoPago().Ativo;
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
        if (item == ItemMenu.Configuracoes) { AbrirConfiguracoes(); return; }
        if (GestaoViewModel.EhDaGestao(item)) { AbrirGestaoCom(item); return; }
        if (!Permitido(item)) return;
        switch (item)
        {
            case ItemMenu.Inicio: IrParaInicio(); break;
            case ItemMenu.Pdv: IrParaPdv(); break;
            case ItemMenu.VendaCliente: IrParaVendaCliente(); break;
            case ItemMenu.Caixa: IrParaCaixa(); break;
            case ItemMenu.Pedidos: IrParaPedidos(); break;
            case ItemMenu.Trocas: IrParaTrocas(); break;
            case ItemMenu.MaisVendidos: IrParaMaisVendidos(); break;
            case ItemMenu.Desempenho: IrParaDesempenho(); break;
            case ItemMenu.EstoqueDaLoja: IrParaEstoque(); break;
            case ItemMenu.ResumoEstoque: IrParaResumoEstoque(); break;
            case ItemMenu.Fabrica: IrParaFabrica(); break;
            case ItemMenu.Etiquetas: IrParaEtiquetas(); break;
        }
    }

    [RelayCommand] private void IrParaInicio() => Abrir(new InicioViewModel(_servicos.Inicio, _servicos.Backup, Ir, null, _servicos.Agenda, new ConsultorViewModel(_servicos.Consultor, Ir)), ItemMenu.Inicio);

    // Vendas
    [RelayCommand] private void IrParaPdv() => Abrir(_pdv, ItemMenu.Pdv);
    [RelayCommand]
    private void IrParaVendaCliente() => Abrir(new VendaClienteViewModel(_servicos.Clientes, async cliente =>
    {
        await _pdv.EntrarModoClienteAsync(cliente);
        IrParaPdv(); // o mesmo PDV, agora no modo cliente
    }), ItemMenu.VendaCliente);
    [RelayCommand] private void IrParaCaixa() => Abrir(new VendasViewModel(_servicos.Vendas, _servicos.Cupons, _servicos.Caixa,
        new FechamentoViewModel(_servicos.Caixa, _servicos.Configuracao, _servicos.RelatorioCaixa), _servicos.Clientes, _servicos.Configuracao, _servicos.Pedidos), ItemMenu.Caixa);
    [RelayCommand] private void IrParaTrocas() => Abrir(new TrocasViewModel(_servicos.Trocas, _servicos.Cupons), ItemMenu.Trocas);
    [RelayCommand] private void IrParaPedidos() => Abrir(new PedidosViewModel(_servicos.Pedidos, _servicos.Produtos, _servicos.Clientes, _servicos.Cupons), ItemMenu.Pedidos);
    [RelayCommand] private void IrParaDesempenho() => Abrir(new DesempenhoVendasViewModel(_servicos.Desempenho, resumo: _servicos.ResumoVendas), ItemMenu.Desempenho);
    [RelayCommand] private void IrParaMaisVendidos() => Abrir(new MaisVendidosViewModel(_servicos.Relatorios), ItemMenu.MaisVendidos);

    // Estoque
    [RelayCommand] private void IrParaResumoEstoque() => Abrir(new ResumoEstoqueViewModel(_servicos.Produtos, _servicos.Relatorios, estoque: _servicos.Estoque), ItemMenu.ResumoEstoque);
    [RelayCommand] private void IrParaEstoque() => Abrir(new EstoqueLojaViewModel(_servicos.Produtos, _servicos.Estoque, _servicos.Fotos, _servicos.Configuracao,
        ajustesCatalogo: new NexusLS.Core.Vitrine.AjustesProdutoStore(NexusLS.Core.Vitrine.AjustesProdutoStore.CaminhoPadrao())), ItemMenu.EstoqueDaLoja);
    [RelayCommand] private void IrParaFabrica() => Abrir(new FabricaViewModel(_servicos.Producao), ItemMenu.Fabrica);
    [RelayCommand] private void IrParaEtiquetas()
    {
        var etiquetas = new EtiquetasViewModel(_servicos.Produtos, _servicos.ModelosEtiqueta);
        var cfg = _servicos.Seguranca.Carregar();
        etiquetas.AplicarPadroes(cfg.EtiquetaFormatoPadrao, cfg.EtiquetaPrecoAtacadoPadrao);
        Abrir(etiquetas, ItemMenu.Etiquetas);
    }

    // Gestão: abre numa janela própria, com PIN (ver GestaoWindow.Abrir)
    /// <summary>A MainWindow define: abrir a janela de Gestão precisa da janela dona e do zoom.</summary>
    public Action<ItemMenu>? AbrirJanelaGestao { get; set; }

    [RelayCommand] private void AbrirGestao() => AbrirGestaoCom(ItemMenu.Agenda);

    private void AbrirGestaoCom(ItemMenu item) => AbrirJanelaGestao?.Invoke(item);

    /// <summary>A MainWindow define: abre a janela própria das Configurações (fora do menu e da Gestão).</summary>
    public Action? AbrirJanelaConfiguracoes { get; set; }

    [RelayCommand]
    private void AbrirConfiguracoes()
    {
        // Quem não tem permissão de Configurações no balcão passa pelo PIN da Gestão, como antes.
        if (Sessao.Pode(Permissao.Configuracoes)) AbrirJanelaConfiguracoes?.Invoke();
        else AbrirGestaoCom(ItemMenu.Configuracoes);
    }

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
        ItemMenu.Caixa or ItemMenu.Trocas => Sessao.Pode(Permissao.Caixa),
        ItemMenu.Pedidos => Sessao.Pode(Permissao.Vender),
        ItemMenu.MaisVendidos or ItemMenu.Desempenho or ItemMenu.Relatorios => Sessao.Pode(Permissao.Relatorios),
        ItemMenu.EstoqueDaLoja or ItemMenu.ResumoEstoque or ItemMenu.Etiquetas or ItemMenu.Fabrica => Sessao.Pode(Permissao.Estoque),
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
    /// <summary>O botão Gestão. Só some quando a preferência pede e quem está no balcão não tem acesso a nada da gestão.</summary>
    public bool VerGestao => !(Sessao.LoginAtivo && !(Sessao.Atual?.PodeAlguma(Permissoes.Gestao) ?? false) && _servicos.Seguranca.Carregar().EsconderGestaoSemAcesso);

    public bool VerSecaoVendas => VerPdv || VerCaixa || VerRelatorios;
    public bool VerSecaoEstoque => VerEstoque;

    /// <summary>Nome desta loja (um computador = uma loja), mostrado de leve no menu lateral.</summary>
    public string LojaNomeTexto => _servicos.Seguranca.Carregar().LojaNome;
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
            nameof(VerRelatorios), nameof(VerConfiguracoes), nameof(VerFuncionarios), nameof(VerSecaoVendas), nameof(VerSecaoEstoque), nameof(LojaNomeTexto),
            nameof(VerSecaoGestao), nameof(VerGestao), nameof(TemUsuario), nameof(UsuarioTexto)
        }) OnPropertyChanged(nome);
    }
}
