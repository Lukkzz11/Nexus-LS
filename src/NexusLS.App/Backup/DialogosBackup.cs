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

    // Backup completo (.nexusbackup). Implementação padrão vazia: os diálogos falsos dos testes não precisam saber disso.
    string? EscolherDestinoPacote(string nomeSugerido) => null;
    string? EscolherArquivoPacote() => null;
    string? PedirSenha(string titulo, string explicacao, bool confirmar) => null;
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
            Filter = "Backup do Nexus (*.db)|*.db",
            CheckFileExists = true
        };
        if (!string.IsNullOrWhiteSpace(pastaInicial) && System.IO.Directory.Exists(pastaInicial))
            janela.InitialDirectory = pastaInicial;
        return janela.ShowDialog() == true ? janela.FileName : null;
    }

    public string? EscolherDestinoPacote(string nomeSugerido)
    {
        var janela = new SaveFileDialog
        {
            Title = "Salvar o backup completo",
            FileName = nomeSugerido,
            Filter = "Backup completo do Nexus (*.nexusbackup)|*.nexusbackup",
            DefaultExt = ".nexusbackup"
        };
        return janela.ShowDialog() == true ? janela.FileName : null;
    }

    public string? EscolherArquivoPacote()
    {
        var janela = new OpenFileDialog
        {
            Title = "Abrir o backup completo",
            Filter = "Backup completo do Nexus (*.nexusbackup)|*.nexusbackup",
            CheckFileExists = true
        };
        return janela.ShowDialog() == true ? janela.FileName : null;
    }

    public string? PedirSenha(string titulo, string explicacao, bool confirmar) =>
        JanelaSenha.Pedir(titulo, explicacao, confirmar);

    public bool Confirmar(string texto) =>
        MessageBox.Show(texto, "Nexus", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    /// <summary>Abre o programa de novo e fecha este (depois de restaurar, para tudo recarregar do banco novo).</summary>
    public void Reiniciar()
    {
        var exe = Environment.ProcessPath;
        if (exe is not null) Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        Application.Current.Shutdown();
    }
}
