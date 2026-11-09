using System.Globalization;
using System.Text;

namespace NexusLS.Core.Diagnostico;

/// <summary>
/// Log em arquivo texto, sem pacotes externos. Cada dia tem o seu arquivo
/// (<c>nexus-AAAA-MM-DD.log</c>) e só os 14 mais recentes são mantidos.
///
/// LGPD: este log é técnico. NUNCA passe para cá CPF, RG, telefone, endereço, e-mail,
/// senha/PIN, código de recuperação, conteúdo de cliente ou qualquer dado pessoal.
/// Prefira mensagens como "Falha ao salvar venda #12" em vez de "Falha ao salvar a venda da Maria".
/// Exceções são gravadas com a pilha para diagnóstico; evite repassar exceções cuja mensagem
/// carregue dados do cliente (ex.: comandos SQL com valores).
/// </summary>
public sealed class RegistroDeLogEmArquivo
{
    /// <summary>Quantidade de arquivos diários mantidos. Os mais antigos são apagados.</summary>
    public const int ArquivosMantidos = 14;

    private const string Prefixo = "nexus-";
    private const string PadraoArquivo = "nexus-????-??-??.log";

    private readonly string _pasta;
    private readonly Func<DateTime> _agora;
    private readonly object _trava = new();
    private DateTime? _ultimaLimpeza;

    /// <param name="pasta">Pasta dos logs. Nula/vazia usa <see cref="PastaPadrao"/>.</param>
    /// <param name="agora">Relógio injetável para os testes. Nulo usa <see cref="DateTime.Now"/>.</param>
    public RegistroDeLogEmArquivo(string? pasta = null, Func<DateTime>? agora = null)
    {
        _pasta = string.IsNullOrWhiteSpace(pasta) ? PastaPadrao() : pasta;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>%LOCALAPPDATA%\NexusLS\logs — fora da pasta do programa (que pode exigir administrador).</summary>
    public static string PastaPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "logs");

    public void Info(string mensagem) => Escrever("INFO", mensagem, null);

    public void Aviso(string mensagem) => Escrever("AVISO", mensagem, null);

    public void Erro(string mensagem, Exception? excecao = null) => Escrever("ERRO", mensagem, excecao);

    /// <summary>
    /// Grava uma linha. Qualquer falha (pasta inexistente, disco cheio, caminho inválido, falta de permissão)
    /// é engolida de propósito: registrar erro nunca pode derrubar o programa.
    /// </summary>
    private void Escrever(string nivel, string? mensagem, Exception? excecao)
    {
        try
        {
            var agora = _agora();
            var texto = new StringBuilder()
                .Append(agora.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture))
                .Append(" [").Append(nivel.PadRight(5)).Append("] ")
                .Append("(thread ").Append(Environment.CurrentManagedThreadId).Append(") ")
                .Append(mensagem ?? "")
                .AppendLine();
            if (excecao is not null) texto.AppendLine(excecao.ToString());

            lock (_trava)
            {
                Directory.CreateDirectory(_pasta);
                // AppendAllText abre/fecha o arquivo a cada linha: outra instância do programa não corrompe o log.
                File.AppendAllText(CaminhoDoArquivo(agora), texto.ToString());
                LimparAntigos(agora);
            }
        }
        catch (Exception)
        {
            // Silêncio proposital. O chamador nunca deve saber que o log falhou.
        }
    }

    private string CaminhoDoArquivo(DateTime agora) =>
        Path.Combine(_pasta, Prefixo + agora.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

    /// <summary>Retenção: no máximo uma varredura por dia; mantém os 14 nomes mais recentes.</summary>
    private void LimparAntigos(DateTime agora)
    {
        var dia = agora.Date;
        if (_ultimaLimpeza == dia) return;
        _ultimaLimpeza = dia;

        try
        {
            if (!Directory.Exists(_pasta)) return;
            var arquivos = Directory.GetFiles(_pasta, PadraoArquivo)
                .OrderByDescending(caminho => Path.GetFileName(caminho), StringComparer.Ordinal)
                .Skip(ArquivosMantidos)
                .ToList();

            foreach (var arquivo in arquivos)
            {
                try { File.Delete(arquivo); }
                catch (Exception) { /* arquivo em uso: tenta de novo no dia seguinte */ }
            }
        }
        catch (Exception)
        {
            // Limpeza é higiene; nunca impede o log de funcionar.
        }
    }
}

/// <summary>
/// Fachada simples usada pelo programa: <c>RegistroDeLog.Info("...")</c>. A instância padrão
/// grava em %LOCALAPPDATA%\NexusLS\logs. Para testes, instancie <see cref="RegistroDeLogEmArquivo"/>
/// com uma pasta temporária e um relógio fixo.
/// </summary>
public static class RegistroDeLog
{
    public static RegistroDeLogEmArquivo Padrao { get; } = new();

    public static string PastaPadrao => RegistroDeLogEmArquivo.PastaPadrao();

    public static void Info(string mensagem) => Padrao.Info(mensagem);

    public static void Aviso(string mensagem) => Padrao.Aviso(mensagem);

    public static void Erro(string mensagem, Exception? excecao = null) => Padrao.Erro(mensagem, excecao);
}
