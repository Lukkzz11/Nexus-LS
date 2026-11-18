using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Backup;
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

    // ---- Limpeza manual dos backups antigos no Drive: primeiro mostra, depois (com confirmação) apaga ----
    private PlanoLimpezaDrive? _planoDrive;

    public ObservableCollection<string> PreviaLimpezaDrive { get; } = new();
    [ObservableProperty] private string _resumoLimpezaDrive = "";
    [ObservableProperty] private bool _temPlanoLimpezaDrive;

    private static string TamanhoDrive(long bytes) =>
        bytes >= 1_048_576 ? $"{TextoBr.Numero(bytes / 1_048_576.0, "0.0")} MB" : $"{Math.Max(1, bytes / 1024)} KB";

    private void LimparPreviaDrive()
    {
        _planoDrive = null;
        PreviaLimpezaDrive.Clear();
        TemPlanoLimpezaDrive = false;
        ResumoLimpezaDrive = "";
    }

    /// <summary>Passo 1: só LISTA o que seria apagado (padrão: mais antigos que 30 dias, sempre mantendo ao menos 3). Não apaga nada.</summary>
    [RelayCommand]
    private Task LimparBackupsAntigosDriveAsync() => ExecutarGoogleAsync(async () =>
    {
        LimparPreviaDrive();
        MostrarGoogle("Consultando os backups do Google Drive...", false);
        var plano = await _google!.PlanejarLimpezaAsync();
        if (plano.Apagar.Count == 0)
        {
            MostrarGoogle($"Nada para limpar: não há backups com mais de {RegrasBackup.DiasPadraoNoDrive} dias além dos {RegrasBackup.MinimoNoDrive} mais recentes.", false);
            return;
        }
        _planoDrive = plano;
        foreach (var a in plano.Apagar.OrderBy(x => x.CriadoUtc))
            PreviaLimpezaDrive.Add($"{a.CriadoUtc.ToLocalTime():dd/MM/yyyy HH:mm}  ·  {a.Nome}  ·  {TamanhoDrive(a.Bytes)}");
        ResumoLimpezaDrive = $"{plano.Apagar.Count} backup(s) seriam apagados, liberando {TamanhoDrive(plano.BytesLiberados)}. {plano.Mantidos} ficam no Drive.";
        TemPlanoLimpezaDrive = true;
        MostrarGoogle("Confira a lista abaixo. Nada foi apagado ainda.", false);
    });

    /// <summary>Passo 2: só apaga depois de o usuário confirmar na janela.</summary>
    [RelayCommand]
    private Task ConfirmarLimpezaDriveAsync() => ExecutarGoogleAsync(async () =>
    {
        var plano = _planoDrive;
        if (plano is null || plano.Apagar.Count == 0) throw new RegraDeNegocioException("Clique primeiro em Limpar backups antigos do Drive para ver a lista.");
        var pergunta = $"Apagar DEFINITIVAMENTE {plano.Apagar.Count} backup(s) do seu Google Drive, liberando {TamanhoDrive(plano.BytesLiberados)}?" +
                       "\n\nEles não vão para a lixeira e não dá para desfazer. Continuar?";
        if (_dialogos is null || !_dialogos.Confirmar(pergunta)) { MostrarGoogle("Limpeza cancelada. Nada foi apagado.", false); return; }

        MostrarGoogle("Apagando do Google Drive...", false);
        var r = await _google!.ApagarAsync(plano.Apagar);
        LimparPreviaDrive();
        var texto = $"{r.Apagados.Count} backup(s) apagado(s) do Drive, {TamanhoDrive(r.BytesLiberados)} liberados." +
                    (r.JaApagados.Count > 0 ? $" {r.JaApagados.Count} já não existia(m)." : "");
        MostrarGoogle(r.Erro is null ? texto : texto + " A limpeza parou: " + r.Erro, r.Erro is not null);
    });

    [RelayCommand]
    private void CancelarLimpezaDrive()
    {
        LimparPreviaDrive();
        MostrarGoogle("Limpeza cancelada. Nada foi apagado.", false);
    }

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
