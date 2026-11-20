using System.Diagnostics;
using System.IO;
using System.Net.Http;
using NexusLS.Core.Atualizacao;

namespace NexusLS.Atualizador;

public partial class MainWindow
{
    private const string ExeLojaOnline = "NexusLojaOnline.exe";

    /// <summary>
    /// A Loja Online é opcional: só atualiza se o programa já estiver instalado (na pasta do Hub ou do BarTender).
    /// Devolve uma linha para o resumo, ou null quando a loja não está instalada neste computador.
    /// </summary>
    private async Task<string?> AtualizarLojaOnlineSeInstaladaAsync()
    {
        var exe = LocalizarLojaOnline();
        if (exe is null) return null;

        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exe);
            VersaoPrograma.TryParse(vi.ProductVersion?.Split('+')[0] ?? "1.0.0", out var atual);
            var fonte = new FonteGitHub(HttpCompartilhado, ConfiguracaoAtualizacao.Repositorio(), ExeLojaOnline, ExeLojaOnline + ".sha256");
            var servico = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), atual);
            var res = await servico.VerificarAsync(exe);
            if (res.Estado != EstadoVerificacao.Disponivel || res.Nova is null)
                return $"• Nexus Loja Online: atualizada (v{res.Atual})";

            var baixado = await servico.BaixarAsync(res.Nova, new Progress<double>(), "NexusLojaOnline");
            if (!FecharProcessoSeguro("NexusLojaOnline"))
                return "• Nexus Loja Online: feche o programa e verifique de novo para atualizar";

            File.Copy(exe, Path.Combine(Path.GetDirectoryName(exe)!, "NexusLojaOnline.anterior.exe"), overwrite: true);
            AtualizadorService.TrocarExecutavel(exe, baixado);
            return $"• Nexus Loja Online: atualizada para v{res.Nova.Versao}";
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException)
        {
            return "• Nexus Loja Online: não foi possível atualizar (" + ex.Message + ")";
        }
    }

    private string? LocalizarLojaOnline()
    {
        var pastas = new List<string> { AppContext.BaseDirectory };
        var bt = TxtCaminhoBarTenderExe.Text.Trim();
        if (!string.IsNullOrWhiteSpace(bt)) pastas.Add(Path.GetDirectoryName(bt) ?? "");
        var nexus = TxtCaminhoExe.Text.Trim();
        if (!string.IsNullOrWhiteSpace(nexus)) pastas.Add(Path.GetDirectoryName(nexus) ?? "");
        pastas.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus"));

        return pastas.Where(p => p.Length > 0).Select(p => Path.Combine(p, ExeLojaOnline)).FirstOrDefault(File.Exists);
    }
}
