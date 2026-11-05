using Microsoft.EntityFrameworkCore;
using NexusLS.App.Impressao;
using NexusLS.Core.Data;
using NexusLS.Core.Services;

namespace NexusLS.App;

/// <summary>
/// Ponto único onde os serviços são criados (a "raiz de composição"). É um objeto comum, não estático:
/// quem precisa de serviços recebe este objeto (ou o serviço) pelo construtor. O programa cria UM só,
/// em <see cref="App.Servicos"/>; testes podem criar outro apontando para um banco de teste.
/// </summary>
public sealed class ContainerDeServicos
{
    private readonly System.Net.Http.HttpClient _httpFirebase = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly System.Net.Http.HttpClient _httpAtualizacao = new() { Timeout = TimeSpan.FromSeconds(20) };

    public ProdutoService Produtos { get; }
    public EstoqueService Estoque { get; }
    public VendaService Vendas { get; }
    public NexusLS.Core.Etiquetas.ModeloEtiquetaStore ModelosEtiqueta { get; }
    public CaixaService Caixa { get; }
    public ClienteService Clientes { get; }
    public ConfiguracaoService Configuracao { get; }
    public InicioService Inicio { get; }
    public RelatorioService Relatorios { get; }
    public DemonstrativoService Demonstrativo { get; }
    public DesempenhoService Desempenho { get; }
    public ProducaoService Producao { get; }
    public AgendaService Agenda { get; }
    public FinanceiroService Financeiro { get; }
    public CategoriaFinanceiraService CategoriasFinanceiras { get; }
    public FornecedorService Fornecedores { get; }
    public CompraService Compras { get; }
    public PedidoService Pedidos { get; }
    public FuncionarioService Funcionarios { get; }
    public AtividadeService Atividades { get; }
    public ContaFinanceiraService ContasFinanceiras { get; }
    public LiquidacaoCartaoService Cartoes { get; }
    public ChequeService Cheques { get; }
    public PosicaoFinanceiraService Posicao { get; }
    public CupomService Cupons { get; }
    public NexusLS.Core.Backup.GestorBackup Backup { get; }
    public NexusLS.Core.Atualizacao.AtualizadorService Atualizador { get; }
    public NexusLS.App.ViewModels.AtualizacaoViewModel Atualizacao { get; }
    public LimpezaService Limpeza { get; }
    public NexusLS.Core.Sincronizacao.Firebase.ConfiguracaoFirebaseStore Firebase { get; }
    public NexusLS.Core.Sincronizacao.SincronizacaoRepositorio SincronizacaoRepo { get; }
    public NexusLS.Core.Sincronizacao.IDestinoCatalogo DestinoCatalogo { get; }
    public NexusLS.Core.Sincronizacao.SincronizadorCatalogo Sincronizador { get; }
    public FotoProdutoService Fotos { get; }
    public NexusLS.Core.Google.GoogleDriveBackup Google { get; }
    public NexusLS.Core.Google.AgendaGoogle AgendaNoGoogle { get; }
    public PreferenciasStore Preferencias { get; }
    public NexusLS.Core.Vitrine.VitrineStore Vitrine { get; }
    public NexusLS.Core.Vitrine.IPublicadorVitrine PublicadorVitrine { get; }
    public NexusLS.Core.Vitrine.ServicoCatalogoWeb CatalogoWeb { get; }
    public RascunhoService Rascunho { get; }
    public NexusLS.Core.Sincronizacao.Firebase.GestorLogin Login { get; }
    public NexusLS.Core.Sincronizacao.ProcessadorFila Fila { get; }
    public NexusLS.Core.Sincronizacao.Operacional.ConfiguracaoOperacionalStore Operacional { get; }
    public NexusLS.Core.Sincronizacao.Operacional.ProcessadorFilaOperacional FilaOperacional { get; }
    public NexusLS.App.ViewModels.SincronizacaoViewModel Sincronizacao { get; }
    public NexusLS.Core.Importacao.Erp.ConfiguracaoErpStore ErpConfig { get; }
    public NexusLS.Core.Importacao.Erp.ContaErp ContaErp { get; }
    public Func<NexusLS.Core.Importacao.Erp.ImportadorErp> CriarImportadorErp { get; }

    public ContainerDeServicos(DbContextOptions<PdvDbContext> opcoesBanco)
    {
        var fabrica = new FabricaContexto(opcoesBanco);

        Produtos = new(fabrica);
        Estoque = new(fabrica);
        Vendas = new(fabrica);
        ModelosEtiqueta = new(NexusLS.Core.Etiquetas.ModeloEtiquetaStore.CaminhoPadrao());
        Caixa = new(fabrica);
        Clientes = new(fabrica);
        Configuracao = new(fabrica);
        Inicio = new(fabrica);
        Relatorios = new(fabrica);
        Demonstrativo = new(fabrica);
        Desempenho = new(fabrica);
        Producao = new(fabrica, Produtos);
        Agenda = new(fabrica);
        Financeiro = new(fabrica);
        CategoriasFinanceiras = new(fabrica);
        Fornecedores = new(fabrica);
        Compras = new(fabrica);
        Pedidos = new(fabrica);
        Funcionarios = new(fabrica);
        Atividades = new(fabrica);
        ContasFinanceiras = new(fabrica);
        Cartoes = new(fabrica);
        Cheques = new(fabrica);
        Posicao = new(fabrica);
        Cupons = new(fabrica, Configuracao, new ImpressoraWindows());
        Backup = new(
            new NexusLS.Core.Backup.BackupService(CaminhoBanco.Padrao()),
            new NexusLS.Core.Backup.ConfiguracaoBackupStore(NexusLS.Core.Backup.ConfiguracaoBackupStore.CaminhoPadrao()));
        Atualizador = new(
            new NexusLS.Core.Atualizacao.FonteGitHub(_httpAtualizacao, NexusLS.Core.Atualizacao.ConfiguracaoAtualizacao.Repositorio()),
            NexusLS.Core.Atualizacao.AtualizadorService.PastaPadrao(),
            NexusLS.Core.Atualizacao.VersaoPrograma.Atual());
        Atualizacao = new(Atualizador, NexusLS.App.AplicadorDeAtualizacao.AplicarEReiniciarAsync);
        Limpeza = new(fabrica,
            async () => (await Backup.FazerAsync(NexusLS.Core.Backup.MotivoBackup.PreLimpeza)).Caminho);
        Firebase = new(NexusLS.Core.Sincronizacao.Firebase.ConfiguracaoFirebaseStore.CaminhoPadrao(), new NexusLS.App.Seguranca.ProtetorDpapi());
        SincronizacaoRepo = new(fabrica);
        DestinoCatalogo = new NexusLS.Core.Sincronizacao.Firebase.DestinoFirestoreConfiguravel(Firebase, _httpFirebase);
        Sincronizador = new(SincronizacaoRepo, DestinoCatalogo);
        Vitrine = new(NexusLS.Core.Vitrine.VitrineStore.CaminhoPadrao());
        PublicadorVitrine = new NexusLS.Core.Vitrine.PublicadorVitrineConfiguravel(Firebase, _httpFirebase);
        Preferencias = new(PreferenciasStore.CaminhoPadrao());
        CatalogoWeb = new(Produtos, Configuracao, new NexusLS.Core.Vitrine.VitrineCompletaStore(NexusLS.Core.Vitrine.VitrineCompletaStore.CaminhoPadrao()),
            NexusLS.Core.Vitrine.ServicoCatalogoWeb.PastaImagensPadrao(),
            new NexusLS.Core.Sincronizacao.Firebase.EnviadorFotoConfiguravel(Firebase, _httpFirebase),
            (NexusLS.Core.Vitrine.IPublicadorVitrineCompleta)PublicadorVitrine);
        NexusLS.App.ViewModels.PdvViewModel.DescontoPorPecaPadrao = () => Preferencias.Carregar().DescontoPorPecaCentavos;
        var googleStore = new NexusLS.Core.Google.GoogleStore(NexusLS.Core.Google.GoogleStore.CaminhoPadrao(), new NexusLS.App.Seguranca.ProtetorDpapi());
        var googleAuth = new NexusLS.Core.Google.GoogleOAuth(_httpFirebase, googleStore);
        Google = new(googleStore, googleAuth, new NexusLS.Core.Google.DriveRest(_httpFirebase, googleAuth));
        AgendaNoGoogle = new(googleStore, new NexusLS.Core.Google.CalendarRest(_httpFirebase, googleAuth));
        Fotos = new(fabrica, new NexusLS.App.Imagens.ProcessadorImagemWpf(), FotoProdutoService.PastaPadrao(),
            new NexusLS.Core.Sincronizacao.Firebase.EnviadorFotoConfiguravel(Firebase, _httpFirebase));
        var protetor = new NexusLS.App.Seguranca.ProtetorDpapi();
        Operacional = new(NexusLS.Core.Sincronizacao.Operacional.ConfiguracaoOperacionalStore.CaminhoPadrao());
        Login = new(Firebase, _httpFirebase, new NexusLS.Core.Sincronizacao.Firebase.SessaoStore(NexusLS.Core.Sincronizacao.Firebase.SessaoStore.CaminhoPadrao()), protetor,
            operacional: Operacional);
        var destinoFila = new NexusLS.Core.Sincronizacao.Firebase.DestinoFilaFirestore(Login, Firebase, _httpFirebase, DestinoCatalogo);
        Fila = new(fabrica, SincronizacaoRepo, destinoFila);
        var destinoOperacional = new NexusLS.Core.Sincronizacao.Operacional.DestinoOperacionalConfiguravel(Operacional, Login, _httpFirebase);
        FilaOperacional = new(fabrica, new NexusLS.Core.Sincronizacao.Operacional.MontadorOperacional(fabrica), destinoOperacional);
        Sincronizacao = new(new NexusLS.Core.Sincronizacao.MonitorSincronizacao(Fila, () => Login.Configurado, () => destinoFila.PodeEnviar,
                FilaOperacional, () => destinoOperacional.PodeEnviar), Login,
            () => NexusLS.App.Seguranca.Sessao.Atual?.Nome);
        ErpConfig = new(NexusLS.Core.Importacao.Erp.ConfiguracaoErpStore.CaminhoPadrao());
        ContaErp = new(ErpConfig, _httpFirebase, protetor);
        CriarImportadorErp = () => new NexusLS.Core.Importacao.Erp.ImportadorErp(fabrica, ContaErp.CriarFonte());
        Rascunho = new(new RascunhoStore(RascunhoStore.CaminhoPadrao()), fabrica);
    }
}
