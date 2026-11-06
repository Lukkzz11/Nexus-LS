using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Licença do programa: estado no menu lateral e ativação por arquivo. O uso não é bloqueado enquanto <see cref="LicencaService.Obrigatoria"/> for falso.</summary>
public partial class MainViewModel
{
    private LicencaService _licenca = new(LicencaService.CaminhoPadrao());

    /// <summary>A janela define como escolher o arquivo de licença (nulo = cancelou).</summary>
    public Func<string?> EscolherArquivoDeLicenca { get; set; } = () => null;

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
    var nuvem = new NexusLS.Core.Licenciamento.ServicoLicencaNuvem(
        new NexusLS.Core.Licenciamento.FonteLicencaFirestore(new System.Net.Http.HttpClient()),
        NexusLS.Core.Licenciamento.ServicoLicencaNuvem.CaminhoPadrao()).AvaliarLocal();
    if (nuvem.Estado != NexusLS.Core.Licenciamento.EstadoLicencaNuvem.NaoEncontrada) LicencaTexto = nuvem.Mensagem;
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
            @"C:\NexusLS\publicar\atualizador.exe"
        };
        var exe = caminhos.FirstOrDefault(File.Exists);
        if (exe != null)
        {
            ExecutarProcessoExterno(exe, null);
        }
        else
        {
            AbrirPortalWeb();
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
        var url = "https://github.com/Lukkzz11/Nexus-LS---Update";
        ExecutarProcessoExterno(url, null);
    }
}
