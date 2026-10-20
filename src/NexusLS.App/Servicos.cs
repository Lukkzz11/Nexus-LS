using NexusLS.App.Impressao;
using NexusLS.Core.Data;
using NexusLS.Core.Services;

namespace NexusLS.App;

/// <summary>
/// Ponto único onde os serviços são criados. Existem frameworks de "injeção de dependência"
/// para isso, mas num app pequeno como este uma classe simples já resolve.
/// </summary>
public static class Servicos
{
    private static readonly FabricaContexto Fabrica = new(App.OpcoesBanco);

    public static ProdutoService Produtos { get; } = new(Fabrica);
    public static EstoqueService Estoque { get; } = new(Fabrica);
    public static VendaService Vendas { get; } = new(Fabrica);
    public static NexusLS.Core.Etiquetas.ModeloEtiquetaStore ModelosEtiqueta { get; } = new(NexusLS.Core.Etiquetas.ModeloEtiquetaStore.CaminhoPadrao());
    public static CaixaService Caixa { get; } = new(Fabrica);
    public static ClienteService Clientes { get; } = new(Fabrica);
    public static ConfiguracaoService Configuracao { get; } = new(Fabrica);
    public static InicioService Inicio { get; } = new(Fabrica);
    public static RelatorioService Relatorios { get; } = new(Fabrica);
    public static FinanceiroService Financeiro { get; } = new(Fabrica);
    public static FornecedorService Fornecedores { get; } = new(Fabrica);
    public static CompraService Compras { get; } = new(Fabrica);
    public static PedidoService Pedidos { get; } = new(Fabrica);
    public static FuncionarioService Funcionarios { get; } = new(Fabrica);
    public static CupomService Cupons { get; } = new(Fabrica, Configuracao, new ImpressoraWindows());
    public static NexusLS.Core.Backup.GestorBackup Backup { get; } = new(
        new NexusLS.Core.Backup.BackupService(CaminhoBanco.Padrao()),
        new NexusLS.Core.Backup.ConfiguracaoBackupStore(NexusLS.Core.Backup.ConfiguracaoBackupStore.CaminhoPadrao()));
    private static readonly System.Net.Http.HttpClient HttpFirebase = new() { Timeout = TimeSpan.FromSeconds(30) };
    public static NexusLS.Core.Sincronizacao.Firebase.ConfiguracaoFirebaseStore Firebase { get; } =
        new(NexusLS.Core.Sincronizacao.Firebase.ConfiguracaoFirebaseStore.CaminhoPadrao(), new NexusLS.App.Seguranca.ProtetorDpapi());
    public static NexusLS.Core.Sincronizacao.SincronizacaoRepositorio SincronizacaoRepo { get; } = new(Fabrica);
    public static NexusLS.Core.Sincronizacao.IDestinoCatalogo DestinoCatalogo { get; } =
        new NexusLS.Core.Sincronizacao.Firebase.DestinoFirestoreConfiguravel(Firebase, HttpFirebase);
    public static NexusLS.Core.Sincronizacao.SincronizadorCatalogo Sincronizador { get; } = new(SincronizacaoRepo, DestinoCatalogo);
    public static RascunhoService Rascunho { get; } = new(new RascunhoStore(RascunhoStore.CaminhoPadrao()), Fabrica);
}
