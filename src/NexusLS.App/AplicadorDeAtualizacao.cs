using System.Diagnostics;
using System.IO;
using System.Windows;
using NexusLS.Core.Atualizacao;

namespace NexusLS.App;

/// <summary>Instala a atualização já baixada e conferida: troca o programa, abre o novo e fecha este.</summary>
public static class AplicadorDeAtualizacao
{
    public static Task AplicarEReiniciarAsync(string arquivoNovo)
    {
        var exe = Environment.ProcessPath
                  ?? throw new InvalidOperationException("Não foi possível descobrir onde o programa está instalado.");
        if (!string.Equals(Path.GetFileName(exe), "NexusLS.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O programa não está rodando como NexusLS.exe (modo de desenvolvimento); a atualização automática só vale no programa instalado.");

        try
        {
            AtualizadorService.TrocarExecutavel(exe, arquivoNovo);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Sem permissão para trocar o programa nesta pasta. Instale o Nexus LS numa pasta sua (por exemplo em Documentos) ou execute como administrador.");
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
        Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());
        return Task.CompletedTask;
    }
}
