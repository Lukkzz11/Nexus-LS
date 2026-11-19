using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>
/// A janela de Gestão: cada item do menu abre a sua tela (agenda, clientes, financeiro, relatórios, compras, funcionários, empresa e configurações).
/// Fica fora do menu do balcão e só abre com o PIN de um gerente ou administrador (ver GestaoWindow.Abrir).
/// </summary>
public partial class GestaoViewModel : ObservableObject
{
    private readonly ContainerDeServicos _servicos;
    private readonly Action<double> _aoMudarZoom;
    private readonly Func<string> _licencaTexto;

    [ObservableProperty] private object? _telaAtual;
    [ObservableProperty] private ItemMenu _itemAtivo;
    [ObservableProperty] private double _zoom = 1.0;

    public GestaoViewModel(ContainerDeServicos servicos, double zoom, Action<double> aoMudarZoom, Func<string> licencaTexto)
    {
        _servicos = servicos;
        Zoom = zoom;
        _aoMudarZoom = aoMudarZoom;
        _licencaTexto = licencaTexto;
        Sessao.Mudou += AtualizarPermissoes;
    }

    /// <summary>Solta o ouvinte da sessão ao fechar a janela.</summary>
    public void Encerrar() => Sessao.Mudou -= AtualizarPermissoes;

    /// <summary>Quais itens da Gestão cada permissão libera. A Agenda é de quem entrou na Gestão.</summary>
    public static bool Permitido(ItemMenu item) => item switch
    {
        ItemMenu.Agenda => true,
        ItemMenu.Clientes => Sessao.Pode(Permissao.Clientes),
        ItemMenu.Financeiro or ItemMenu.ContasESaldos => Sessao.Pode(Permissao.Financeiro),
        ItemMenu.EntradasESaidas or ItemMenu.RelatorioFinanceiro => Sessao.Pode(Permissao.Financeiro),
        ItemMenu.Relatorios or ItemMenu.RelatorioProdutos => Sessao.Pode(Permissao.Relatorios),
        ItemMenu.Compras or ItemMenu.Fornecedores => Sessao.Pode(Permissao.Compras),
        ItemMenu.CalculadoraPrecos => Sessao.Pode(Permissao.Estoque),
        ItemMenu.Funcionarios => Sessao.Pode(Permissao.Funcionarios),
        // Gestão da empresa (lojas e preços) é só do administrador: quem tem Configurações.
        ItemMenu.Configuracoes or ItemMenu.CatalogoOnline or ItemMenu.GestaoEmpresa or ItemMenu.Atividades => Sessao.Pode(Permissao.Configuracoes),
        _ => false
    };

    /// <summary>Itens que esta janela sabe abrir (o MainViewModel manda para cá os atalhos do Início).</summary>
    public static bool EhDaGestao(ItemMenu item) => item is ItemMenu.Agenda or ItemMenu.Clientes or ItemMenu.Financeiro or ItemMenu.ContasESaldos
        or ItemMenu.Relatorios or ItemMenu.Funcionarios or ItemMenu.Configuracoes or ItemMenu.CatalogoOnline or ItemMenu.Atividades
        or ItemMenu.Compras or ItemMenu.Fornecedores or ItemMenu.CalculadoraPrecos
        or ItemMenu.RelatorioFinanceiro or ItemMenu.RelatorioProdutos or ItemMenu.GestaoEmpresa or ItemMenu.EntradasESaidas;

    public bool VerClientes => Sessao.Pode(Permissao.Clientes);
    public bool VerFinanceiro => Sessao.Pode(Permissao.Financeiro);
    public bool VerRelatorios => Sessao.Pode(Permissao.Relatorios);
    public bool VerConfiguracoes => Sessao.Pode(Permissao.Configuracoes);
    // Os títulos de grupo do menu só aparecem se sobrou algum item dentro.
    public bool VerGrupoEstoque => VerRelatorios || VerCompras || VerEstoque;
    public bool VerGrupoPessoas => VerFuncionarios || VerConfiguracoes;
    public bool VerGrupoEmpresa => VerConfiguracoes || VerFinanceiro;
    public bool VerCompras => Sessao.Pode(Permissao.Compras);
    public bool VerEstoque => Sessao.Pode(Permissao.Estoque);
    public bool VerFuncionarios => Sessao.Pode(Permissao.Funcionarios);
    public string UsuarioTexto => Sessao.Atual is { } f ? $"{f.Nome} · {Permissoes.Nome(f.Perfil)}" : "Sem login (acesso total)";

    private void AtualizarPermissoes()
    {
        foreach (var nome in new[] { nameof(VerClientes), nameof(VerFinanceiro), nameof(VerRelatorios), nameof(VerConfiguracoes), nameof(VerCompras), nameof(VerEstoque), nameof(VerGrupoEstoque), nameof(VerGrupoPessoas), nameof(VerGrupoEmpresa), nameof(VerFuncionarios), nameof(UsuarioTexto) })
            OnPropertyChanged(nome);
    }

    public void Ir(ItemMenu item)
    {
        if (!Permitido(item)) item = ItemMenu.Agenda;
        switch (item)
        {
            case ItemMenu.Clientes: IrParaClientes(); break;
            case ItemMenu.Financeiro: IrParaFinanceiro(); break;
            case ItemMenu.ContasESaldos: IrParaContasESaldos(); break;
            case ItemMenu.Relatorios: IrParaRelatorios(); break;
            case ItemMenu.Funcionarios: IrParaFuncionarios(); break;
            case ItemMenu.Atividades: IrParaAtividades(); break;
            case ItemMenu.Configuracoes: IrParaConfiguracoes(); break;
            case ItemMenu.CatalogoOnline: IrParaCatalogoOnline(); break;
            case ItemMenu.Compras: IrParaCompras(); break;
            case ItemMenu.Fornecedores: IrParaFornecedores(); break;
            case ItemMenu.CalculadoraPrecos: IrParaCalculadoraPrecos(); break;
            case ItemMenu.RelatorioFinanceiro: IrParaRelatorioFinanceiro(); break;
            case ItemMenu.RelatorioProdutos: IrParaRelatorioProdutos(); break;
            case ItemMenu.GestaoEmpresa: IrParaGestaoEmpresa(); break;
            case ItemMenu.EntradasESaidas: IrParaEntradasESaidas(); break;
            default: IrParaAgenda(); break;
        }
    }

    // O editor do catálogo vai virar outro programa: o código dele continua no projeto, mas não há mais entrada de menu.
    [RelayCommand] private void IrParaCompras() => Abrir(new ComprasViewModel(_servicos.Compras, _servicos.Produtos, _servicos.Fornecedores), ItemMenu.Compras);
    [RelayCommand] private void IrParaFornecedores() => Abrir(new FornecedoresViewModel(_servicos.Fornecedores), ItemMenu.Fornecedores);
    [RelayCommand] private void IrParaCalculadoraPrecos() => Abrir(new CalculadoraPrecosViewModel(_servicos.Produtos,
        new NexusLS.Core.Services.ParametrosCalculadoraStore(NexusLS.Core.Services.ParametrosCalculadoraStore.CaminhoPadrao())), ItemMenu.CalculadoraPrecos);
    [RelayCommand] private void IrParaRelatorioFinanceiro() => Abrir(new RelatorioFinanceiroViewModel(_servicos.RelatorioFinanceiro), ItemMenu.RelatorioFinanceiro);
    [RelayCommand] private void IrParaRelatorioProdutos() => Abrir(new RelatorioProdutosViewModel(_servicos.RelatorioProdutos), ItemMenu.RelatorioProdutos);
    [RelayCommand] private void IrParaEntradasESaidas() => Abrir(CriarEntradasESaidas(), ItemMenu.EntradasESaidas);

    [RelayCommand]
    private void IrParaGestaoEmpresa() => Abrir(new GestaoEmpresaViewModel(
        new LojasViewModel(_servicos.RelatorioFinanceiro, () => _servicos.Seguranca.Carregar()),
        Sessao.Pode(Permissao.Financeiro) ? CriarEntradasESaidas() : null), ItemMenu.GestaoEmpresa);

    private EntradasESaidasViewModel CriarEntradasESaidas() => new(_servicos.LancamentosEmpresa, _servicos.AnexosEmpresa);
    [RelayCommand] private void IrParaAgenda() => Abrir(new AgendaViewModel(_servicos.Agenda, null, _servicos.AgendaNoGoogle), ItemMenu.Agenda);
    [RelayCommand] private void IrParaClientes()
    {
        var clientes = new ClientesViewModel(_servicos.Clientes, _servicos.TitulosPrazo);
        _ = CarregarNomeLojaAsync(clientes);
        Abrir(clientes, ItemMenu.Clientes);
    }

    private async Task CarregarNomeLojaAsync(ClientesViewModel clientes)
    {
        try { clientes.NomeLoja = (await _servicos.Configuracao.ObterAsync()).NomeLoja; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* sem o nome, a mensagem usa "nossa loja" */ }
    }
    [RelayCommand] private void IrParaFinanceiro() => Abrir(new FinanceiroViewModel(_servicos.Financeiro, null, _servicos.CategoriasFinanceiras), ItemMenu.Financeiro);
    [RelayCommand] private void IrParaContasESaldos() => Abrir(new ContasESaldosViewModel(_servicos.Posicao, _servicos.ContasFinanceiras, _servicos.Cartoes, _servicos.Cheques, chequesEmitidos: _servicos.ChequesEmitidos), ItemMenu.ContasESaldos);
    [RelayCommand] private void IrParaRelatorios() => Abrir(new RelatoriosViewModel(_servicos.Relatorios, null, _servicos.Demonstrativo), ItemMenu.Relatorios);
    [RelayCommand] private void IrParaFuncionarios() => Abrir(new FuncionariosViewModel(_servicos.Funcionarios), ItemMenu.Funcionarios);
    [RelayCommand] private void IrParaAtividades() => Abrir(new AtividadesViewModel(_servicos.Atividades), ItemMenu.Atividades);

    [RelayCommand]
    private void IrParaCatalogoOnline()
    {
        var erp = new ImportacaoErpViewModel(_servicos.ErpConfig, _servicos.ContaErp, _servicos.CriarImportadorErp)
        {
            PedirLogin = () => Views.LoginFirebaseWindow.PedirErp(_servicos.ContaErp, System.Windows.Application.Current?.MainWindow),
            FabricaBanco = _servicos.FabricaBanco,
            EscolherArquivoLife = () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Escolha o arquivo exportado pelo Life", Filter = "Exportação do Life (*.json)|*.json|Todos os arquivos|*.*" };
                return d.ShowDialog() == true ? d.FileName : null;
            }
        };
        Abrir(new CatalogoOnlineViewModel(_servicos.Firebase, _servicos.Sincronizador, _servicos.SincronizacaoRepo, _servicos.DestinoCatalogo, _servicos.Configuracao, _servicos.Operacional, erp, _servicos.Fotos), ItemMenu.CatalogoOnline);
    }

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
        var config = new ConfiguracoesViewModel(_servicos.Configuracao, _servicos.Cupons, _servicos.Backup, dialogos, limpar, _servicos.Atualizacao);
        config.UsarPreferencias(_servicos.Preferencias, z => { Zoom = z; _aoMudarZoom(z); }, _licencaTexto);
        config.AbrirPasta = pasta => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + pasta + "\"") { UseShellExecute = true });
        config.UsarSeguranca(_servicos.Seguranca);
        config.UsarComissoes(_servicos.Comissoes, _servicos.Funcionarios);
        config.UsarEstoque(_servicos.Estoque);
        config.UsarNotificacoes();
        config.UsarComunicacao();
        config.UsarEtiquetas(_servicos.ModelosEtiqueta);
        config.UsarFiscal();
        config.UsarConferenciaNfce(new NexusLS.Core.Fiscal.ConferenciaNfce(_servicos.FabricaBanco));
        config.EscolherCaminhoXmlConferencia = nome =>
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = nome, DefaultExt = ".xml", Filter = "XML de conferência (sem validade fiscal)|*.xml", Title = "Salvar XML de conferência (SEM VALIDADE FISCAL)" };
            return d.ShowDialog() == true ? d.FileName : null;
        };
        config.UsarResumoCelular(_servicos.Inicio);
        config.UsarIntegracoes(_servicos.Contador, () => new (string, string)[]
        {
            ("Google Drive (backup e agenda)", _servicos.Google.Situacao()),
            ("Nuvem Firebase (catálogo e sincronização)", _servicos.Firebase.Carregar().EstaCompleta ? "Configurada." : "Ainda não configurada."),
            ("Mercado Pago (Pix)", _servicos.PagamentosConfig.Ler() is { MercadoPagoAtivo: true, TemToken: true } ? "Ativo." : "Desligado ou sem token."),
            ("ERP (importação e envio)", _servicos.ErpConfig.Carregar().EstaCompleta ? "Configurado." : "Ainda não configurado."),
        });
        config.UsarGoogle(_servicos.Google, url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }));
        Abrir(config, ItemMenu.Configuracoes);
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
}
