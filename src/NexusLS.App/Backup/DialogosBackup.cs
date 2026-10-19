using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;

namespace NexusLS.App.Backup;

/// <summary>
/// As janelas que o backup precisa (escolher pasta ou arquivo, confirmar, reiniciar). Ficam atrás de uma
/// interface para o ViewModel poder ser testado sem abrir janela de verdade.
/// </summary>
public interface IDialogosBackup
{
    string? EscolherPasta(string? pastaAtual);
    string? EscolherArquivoBackup(string? pastaInicial);
    bool Confirmar(string texto);
    void Reiniciar();
}

public class DialogosBackupWpf : IDialogosBackup
{
    public string? EscolherPasta(string? pastaAtual)
    {
        var janela = new OpenFolderDialog { Title = "Pasta dos backups" };
        if (!string.IsNullOrWhiteSpace(pastaAtual) && System.IO.Directory.Exists(pastaAtual))
            janela.InitialDirectory = pastaAtual;
        return janela.ShowDialog() == true ? janela.FolderName : null;
    }

    public string? EscolherArquivoBackup(string? pastaInicial)
    {
        var janela = new OpenFileDialog
        {
            Title = "Escolha o backup para restaurar",
            Filter = "Backup do Nexus LS (*.db)|*.db",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(pastaInicial) && System.IO.Directory.Exists(pastaInicial))
            janela.InitialDirectory = pastaInicial;
        return janela.ShowDialog() == true ? janela.FileName : null;
    }

    public bool Confirmar(string texto) =>
        MessageBox.Show(texto, "Nexus LS", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Abre o programa de novo e fecha este (depois de restaurar, para tudo recarregar do banco novo).</summary>
    public void Reiniciar()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
