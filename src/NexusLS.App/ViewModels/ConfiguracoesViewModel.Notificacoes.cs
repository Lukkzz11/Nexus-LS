using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Página "Notificações": quais avisos o Nexus mostra ao abrir (contas a pagar e backup atrasado; o de estoque fica em Estoque e produtos).</summary>
public partial class ConfiguracoesViewModel
{
    [ObservableProperty] private bool _avisarContas = true;
    [ObservableProperty] private string _diasContasTexto = "3";
    [ObservableProperty] private bool _avisarBackupAtrasado = true;
    [ObservableProperty] private bool _avisarCrediario = true;
    [ObservableProperty] private string _diasBackupTexto = "3";
    [ObservableProperty] private string _mensagemNotificacoes = "";
    [ObservableProperty] private bool _mensagemNotificacoesEhErro;

    public void UsarNotificacoes()
    {
        if (_seguranca is null) return;
        var c = _seguranca.Carregar();
        AvisarContas = c.AvisarContasAoAbrir;
        DiasContasTexto = c.DiasAvisoContas.ToString();
        AvisarBackupAtrasado = c.AvisarBackupAtrasado;
        AvisarCrediario = c.AvisarCrediarioAoAbrir;
        DiasBackupTexto = c.DiasBackupAtrasado.ToString();
    }

    [RelayCommand]
    private void SalvarNotificacoes() => SalvarRegras(
        c =>
        {
            if (!int.TryParse(DiasContasTexto.Trim(), out var diasContas))
                throw new RegraDeNegocioException("Digite os dias das contas só com números (0 = só as que vencem hoje).");
            if (!int.TryParse(DiasBackupTexto.Trim(), out var diasBackup))
                throw new RegraDeNegocioException("Digite os dias do backup só com números.");
            c.AvisarContasAoAbrir = AvisarContas;
            c.DiasAvisoContas = diasContas;
            c.AvisarBackupAtrasado = AvisarBackupAtrasado;
            c.AvisarCrediarioAoAbrir = AvisarCrediario;
            c.DiasBackupAtrasado = diasBackup;
        },
        (m, erro) => { MensagemNotificacoesEhErro = erro; MensagemNotificacoes = m; },
        () => "Salvo. Vale na próxima vez que o Nexus abrir.");
}
