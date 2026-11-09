using System.Diagnostics;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Licença do programa: estado no menu lateral, validação em nuvem e ativação por arquivo.</summary>
public partial class MainViewModel
{
    private static readonly HttpClient _httpLicenca = new() { Timeout = TimeSpan.FromSeconds(12) };

    private LicencaService _licenca = new(LicencaService.CaminhoPadrao());
    private readonly ServicoLicencaNuvem _licencaNuvem = new(
        new FonteLicencaComReserva(new FonteLicencaCloudFunction(_httpLicenca), new FonteLicencaFirestore(_httpLicenca)),
        ServicoLicencaNuvem.CaminhoPadrao());

    /// <summary>A janela define como escolher o arquivo de licença (nulo = cancelou).</summary>
    public Func<string?> EscolherArquivoDeLicenca { get; set; } = () => null;

    /// <summary>Chamado quando uma licença em nuvem vinculada a este PC é bloqueada, vencida ou desvinculada no servidor.</summary>
    public Action<string>? AoDetectarLicencaInvalidaNaNuvem { get; set; }

    /// <summary>Ação para iniciar processos externos (permite interceptar em testes).</summary>
    public Action<string, string?> ExecutarProcessoExterno { get; set; } = (caminhoOuUrl, argumentos) =>
    {
        try
        {
            var psi = new ProcessStartInfo(caminhoOuUrl)
            {
                UseShellExecute = true,
                Arguments = argumentos ?? ""
            };
            if (File.Exists(caminhoOuUrl))
            {
                psi.WorkingDirectory = Path.GetDirectoryName(caminhoOuUrl)!;
            }
            Process.Start(psi);
        }
        catch { }
    };

    [ObservableProperty] private string _licencaTexto = "";
    [ObservableProperty] private string _licencaMensagem = "";

    public string CodigoDestaMaquina => _licenca.CodigoDestaMaquina;

    /// <summary>Troca o serviço (testes usam uma pasta temporária) e relê o estado.</summary>
    public void UsarLicenca(LicencaService licenca)
    {
        _licenca = licenca;
        AtualizarLicenca();
    }

    public void AtualizarLicenca()
    {
        LicencaTexto = _licenca.Avaliar().Mensagem;
        // Licença ativada pelo atualizador (Firebase): quando existe, é a que vale na tela.
        var nuvem = _licencaNuvem.AvaliarLocal();
        if (nuvem.Estado != EstadoLicencaNuvem.NaoEncontrada)
        {
            LicencaTexto = nuvem.Mensagem;
        }
    }

    /// <summary>
    /// Confere silenciosamente na nuvem (Firebase) se a licença deste PC continua ativa,
    /// atualizando o cache local ou disparando o bloqueio caso tenha sido revogada no License Manager.
    /// </summary>
    public async Task VerificarLicencaNuvemAsync()
    {
        try
        {
            var antes = _licencaNuvem.AvaliarLocal();
            if (antes.Estado == EstadoLicencaNuvem.NaoEncontrada) return;

            var agora = await _licencaNuvem.VerificarAsync();
            LicencaTexto = agora.Mensagem;

            if (!agora.Ativa && agora.Estado is EstadoLicencaNuvem.Bloqueada
                or EstadoLicencaNuvem.Vencida
                or EstadoLicencaNuvem.OutraMaquina
                or EstadoLicencaNuvem.ChaveInvalida)
            {
                AoDetectarLicencaInvalidaNaNuvem?.Invoke(agora.Mensagem);
            }
        }
        catch
        {
            // Falha transitória de rede não interrompe a operação local dentro do prazo de tolerância offline.
        }
    }

    [RelayCommand]
    private void AtivarLicenca()
    {
        var arquivo = EscolherArquivoDeLicenca();
        if (arquivo is null) return;
        try
        {
            var r = _licenca.Instalar(arquivo);
            LicencaTexto = r.Mensagem;
            LicencaMensagem = "Licença ativada.";
        }
        catch (RegraDeNegocioException ex)
        {
            LicencaMensagem = ex.Message;
        }
    }

    [RelayCommand]
    public void AbrirAtualizador()
    {
        var pastaApp = AppContext.BaseDirectory;
        var caminhos = new[]
        {
            Path.Combine(pastaApp, "atualizador.exe"),
            Path.Combine(pastaApp, "..", "atualizador.exe"),
            @"C:\Nexus\atualizador.exe",
            @"C:\NexusLS\atualizador.exe",
            @"C:\NexusLS\publicar\atualizador.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NexusLS", "atualizador.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "atualizador.exe")
        };
        var exe = caminhos.FirstOrDefault(File.Exists);
        if (exe != null)
        {
            ExecutarProcessoExterno(exe, "--licenca");
        }
        else
        {
            LicencaMensagem = "Executável atualizador.exe não encontrado na pasta do sistema.";
        }
    }

    public string VersaoTexto => "Nexus v" + NexusLS.Core.Atualizacao.VersaoPrograma.Atual().ToString();

    [ObservableProperty] private string _bancoTexto = "";

    /// <summary>A janela define como abrir a tela de conexão (devolve verdadeiro se algo foi salvo).</summary>
    public Func<bool> AbrirConexaoBanco { get; set; } = () => false;

    public void AtualizarBanco()
    {
        var c = _servicos.Firebase.Carregar();
        BancoTexto = c.EstaCompleta ? $"Conectado: {c.ProjectId}" : "Não conectado";
    }

    [RelayCommand]
    private void AbrirConexaoBancoDeDados()
    {
        if (AbrirConexaoBanco()) AtualizarBanco();
    }

    [RelayCommand]
    public void AbrirPortalWeb()
    {
        AbrirAtualizador();
    }
}
