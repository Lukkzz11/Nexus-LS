using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Google;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Parte "Google Drive" da aba Backup: credenciais do cliente OAuth, conexão pelo navegador e envio do backup.</summary>
public partial class ConfiguracoesViewModel
{
    private GoogleDriveBackup? _google;
    private Action<string> _abrirNavegador = _ => { };

    public bool TemGoogle => _google is not null;

    [ObservableProperty] private string _googleClientId = "";
    [ObservableProperty] private string _googleSegredoNovo = "";
    [ObservableProperty] private string _googleSituacao = "";
    [ObservableProperty] private string _mensagemGoogle = "";
    [ObservableProperty] private bool _mensagemGoogleEhErro;
    [ObservableProperty] private bool _googleOcupado;

    /// <summary><paramref name="abrirNavegador"/> abre a URL no navegador padrão (a tela/o programa fornece).</summary>
    public void UsarGoogle(GoogleDriveBackup google, Action<string> abrirNavegador)
    {
        _google = google;
        _abrirNavegador = abrirNavegador;
        GoogleClientId = google.Configuracao.ClientId;
        GoogleSituacao = google.Situacao();
        OnPropertyChanged(nameof(TemGoogle));
    }

    private void MostrarGoogle(string texto, bool erro)
    {
        MensagemGoogleEhErro = erro;
        MensagemGoogle = texto;
        if (_google is not null) GoogleSituacao = _google.Situacao();
    }

    private async Task ExecutarGoogleAsync(Func<Task> acao)
    {
        if (_google is null || GoogleOcupado) return;
        GoogleOcupado = true;
        try { await acao(); }
        catch (RegraDeNegocioException ex) { MostrarGoogle(ex.Message, true); }
        catch (Exception ex) { MostrarGoogle("Erro inesperado: " + ex.Message, true); }
        finally { GoogleOcupado = false; }
    }

    [RelayCommand]
    private Task SalvarGoogleAsync() => ExecutarGoogleAsync(() =>
    {
        _google!.SalvarCredenciais(GoogleClientId, GoogleSegredoNovo);
        GoogleSegredoNovo = "";
        MostrarGoogle("Credenciais salvas. Agora clique em Conectar ao Google.", false);
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task ConectarGoogleAsync() => ExecutarGoogleAsync(async () =>
    {
        MostrarGoogle("Autorize no navegador que abriu. Esta tela espera até 3 minutos.", false);
        await _google!.ConectarAsync(_abrirNavegador);
        MostrarGoogle("Conectado ao Google Drive.", false);
    });

    [RelayCommand]
    private Task DesconectarGoogleAsync() => ExecutarGoogleAsync(() =>
    {
        _google!.Desconectar();
        MostrarGoogle("Desconectado. Os arquivos que já estão no Drive continuam lá.", false);
        return Task.CompletedTask;
    });

    /// <summary>Envia o backup selecionado na lista (ou o mais recente) para a pasta "Nexus LS Backups" do Drive.</summary>
    [RelayCommand]
    private Task EnviarBackupAoDriveAsync() => ExecutarGoogleAsync(async () =>
    {
        var item = BackupSelecionado ?? Backups.FirstOrDefault()
            ?? throw new RegraDeNegocioException("Ainda não há backup. Clique em Fazer backup agora e depois envie.");
        MostrarGoogle($"Enviando {item.Info.Nome}...", false);
        var pasta = await _google!.EnviarBackupAsync(item.Info.Caminho);
        MostrarGoogle($"Backup enviado para a pasta \"{pasta}\" do seu Google Drive.", false);
    });
}
