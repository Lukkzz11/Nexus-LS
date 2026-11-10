using System.Diagnostics;
using System.IO;
using System.Windows;
using NexusLS.Core.Atualizacao;

namespace NexusLS.Atualizador;

/// <summary>
/// O próprio Atualizador também se atualiza: compara com o atualizador.exe do release, baixa conferindo o SHA-256,
/// troca o seu executável e reabre. Qualquer falha (sem internet, arquivo inválido) é ignorada: nunca atrapalha o uso.
/// </summary>
public partial class MainWindow
{
    private const string NomeExeAtualizador = "atualizador.exe";

    private async Task AutoAtualizarAtualizadorAsync()
    {
        try
        {
            var meuExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(meuExe) || !File.Exists(meuExe)) return;
            if (!string.Equals(Path.GetFileName(meuExe), NomeExeAtualizador, StringComparison.OrdinalIgnoreCase)) return; // rodando via dotnet/depuração

            AtualizadorService.LimparRestos(meuExe);   // sobra da troca anterior

            var minha = VersaoPrograma.TryParse((FileVersionInfo.GetVersionInfo(meuExe).ProductVersion ?? "1.0.0").Split('+')[0], out var v) ? v : default;
            var fonte = new FonteGitHub(HttpCompartilhado, ConfiguracaoAtualizacao.Repositorio(), NomeExeAtualizador, NomeExeAtualizador + ".sha256");
            var servico = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), minha);
            var res = await servico.VerificarAsync(meuExe).ConfigureAwait(false);
            if (res.Estado != EstadoVerificacao.Disponivel || res.Nova is null) return;

            var novo = await servico.BaixarAsync(res.Nova, null, "atualizador", CancellationToken.None).ConfigureAwait(false);
            AtualizadorService.TrocarExecutavel(meuExe, novo);

            await Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(this, $"O Atualizador foi atualizado para a versão {res.Nova.Versao} e vai reabrir agora.",
                    "Nexus Atualizador", MessageBoxButton.OK, MessageBoxImage.Information);
                Process.Start(new ProcessStartInfo(meuExe) { UseShellExecute = true });
                Application.Current.Shutdown();
            });
        }
        catch
        {
            // Sem internet ou arquivo recusado: o atualizador atual continua funcionando normalmente.
        }
    }
}
