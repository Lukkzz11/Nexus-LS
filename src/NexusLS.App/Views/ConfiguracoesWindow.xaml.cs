using System.IO;
using System.Windows;
using System.Windows.Input;
using NexusLS.App.Aparencia;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;

namespace NexusLS.App.Views;

/// <summary>
/// As Configurações numa janela só delas (fora do menu lateral e da Gestão). Existe no máximo uma: pedir de novo
/// só traz a que já está aberta para a frente. É redimensionável, centraliza no dono e o Esc fecha.
/// </summary>
public partial class ConfiguracoesWindow : Window
{
    private static ConfiguracoesWindow? _aberta;

    /// <summary>A janela aberta agora, se houver (usado pelos testes).</summary>
    public static ConfiguracoesWindow? Aberta => _aberta;

    public ConfiguracoesWindow(ConfiguracoesViewModel vm, double zoom = 1.0)
    {
        InitializeComponent();
        // O ícone vem do programa; num host sem esse recurso (testes) a janela abre sem ele.
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/nexus.ico")); }
        catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException) { }
        DataContext = vm;
        AplicarZoom(zoom);
        // KeyDown (e não Preview): uma lista aberta ou um campo que usa o Esc resolve primeiro.
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(); }
        };
        Closed += (_, _) => { if (ReferenceEquals(_aberta, this)) _aberta = null; };
    }

    /// <summary>Tamanho das telas (1,0 = normal): o mesmo ajuste de Configurações > Aparência vale aqui.</summary>
    public void AplicarZoom(double zoom) =>
        Raiz.LayoutTransform = Math.Abs(zoom - 1.0) < 0.001 ? System.Windows.Media.Transform.Identity : new System.Windows.Media.ScaleTransform(zoom, zoom);

    /// <summary>
    /// Abre as Configurações. Se já houver uma janela aberta, traz ela para a frente em vez de abrir outra.
    /// <paramref name="modal"/> existe para quando a chamada vem de dentro da Gestão com o PIN de um gerente:
    /// a sessão dele só vale enquanto a janela estiver aberta.
    /// </summary>
    public static ConfiguracoesWindow Abrir(ContainerDeServicos servicos, Window? dono, double zoom, Action<double> aoMudarZoom,
        Func<string> licencaTexto, Func<bool> souAdministrador, bool modal = false)
        => AbrirCom(() => CriarViewModel(servicos, z => { aoMudarZoom(z); _aberta?.AplicarZoom(z); }, licencaTexto, souAdministrador), dono, zoom, modal);

    /// <summary>A regra de "uma janela só", separada de como o ViewModel é montado (para poder testar).</summary>
    public static ConfiguracoesWindow AbrirCom(Func<ConfiguracoesViewModel> criarViewModel, Window? dono, double zoom, bool modal = false)
    {
        if (_aberta is { } existente)
        {
            if (existente.WindowState == WindowState.Minimized) existente.WindowState = WindowState.Normal;
            existente.Activate();
            return existente;
        }

        var janela = new ConfiguracoesWindow(criarViewModel(), zoom);
        if (dono is not null && dono.IsLoaded) janela.Owner = dono;
        _aberta = janela;
        if (modal) janela.ShowDialog(); else janela.Show();
        return janela;
    }

    /// <summary>Monta o ViewModel das Configurações com todos os serviços (o mesmo que a Gestão montava antes).</summary>
    public static ConfiguracoesViewModel CriarViewModel(ContainerDeServicos servicos, Action<double> aoMudarZoom,
        Func<string> licencaTexto, Func<bool> souAdministrador)
    {
        var dialogos = new NexusLS.App.Backup.DialogosBackupWpf();
        var limpar = new LimparDadosViewModel(servicos.Limpeza, souAdministrador, dialogos.Confirmar, () =>
        {
            // O carrinho guardado aponta para produtos que acabaram de ser apagados: sai junto.
            try { File.Delete(RascunhoStore.CaminhoPadrao()); } catch (IOException) { }
            dialogos.Reiniciar();
        });
        var config = new ConfiguracoesViewModel(servicos.Configuracao, servicos.Cupons, servicos.Backup, dialogos, limpar, servicos.Atualizacao);
        config.UsarPreferencias(servicos.Preferencias, aoMudarZoom, licencaTexto);
        config.AoMudarTema = GerenciadorDeTema.Definir;
        config.AbrirPasta = pasta => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + pasta + "\"") { UseShellExecute = true });
        config.UsarSeguranca(servicos.Seguranca);
        config.UsarComissoes(servicos.Comissoes, servicos.Funcionarios);
        config.UsarEstoque(servicos.Estoque);
        config.UsarNotificacoes();
        config.UsarComunicacao();
        config.UsarEtiquetas(servicos.ModelosEtiqueta);
        config.UsarFiscal();
        config.UsarConferenciaNfce(new NexusLS.Core.Fiscal.ConferenciaNfce(servicos.FabricaBanco));
        config.EscolherCaminhoXmlConferencia = nome =>
        {
            var d = new Microsoft.Win32.SaveFileDialog { FileName = nome, DefaultExt = ".xml", Filter = "XML de conferência (sem validade fiscal)|*.xml", Title = "Salvar XML de conferência (SEM VALIDADE FISCAL)" };
            return d.ShowDialog() == true ? d.FileName : null;
        };
        config.UsarResumoCelular(servicos.Inicio);
        config.UsarIntegracoes(servicos.Contador, () => new (string, string)[]
        {
            ("Google Drive (backup e agenda)", servicos.Google.Situacao()),
            ("Nuvem Firebase (catálogo e sincronização)", servicos.Firebase.Carregar().EstaCompleta ? "Configurada." : "Ainda não configurada."),
            ("Mercado Pago (Pix)", servicos.PagamentosConfig.Ler() is { MercadoPagoAtivo: true, TemToken: true } ? "Ativo." : "Desligado ou sem token."),
            ("ERP (importação e envio)", servicos.ErpConfig.Carregar().EstaCompleta ? "Configurado." : "Ainda não configurado."),
        });
        config.UsarGoogle(servicos.Google, url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }));
        return config;
    }

    /// <summary>Fecha a janela de Configurações, se estiver aberta (usado ao bloquear por inatividade).</summary>
    public static void FecharSeAberta() => _aberta?.Close();
}
