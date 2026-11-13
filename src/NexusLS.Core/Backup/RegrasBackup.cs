using System.Globalization;
using System.Text.RegularExpressions;

namespace NexusLS.Core.Backup;

/// <summary>Por que o backup foi feito. Vira parte do nome do arquivo.</summary>
public enum MotivoBackup { Manual, Automatico, PreAtualizacao, PreRestauracao, PreLimpeza, PreImportacao }

/// <summary>Como está a proteção dos dados, para o aviso da tela inicial.</summary>
public enum SituacaoBackup { Nunca, EmDia, Atrasado, SoManual }

/// <summary>Um arquivo de backup encontrado na pasta.</summary>
public record InfoBackup(string Caminho, DateTime Data, MotivoBackup Motivo, long Bytes)
{
    public string Nome => Path.GetFileName(Caminho);
}

/// <summary>
/// Regras puras do backup (nomes, quando fazer, o que apagar): não mexem em arquivo nem em banco,
/// por isso são fáceis de testar. Datas sempre no horário LOCAL.
/// </summary>
public static class RegrasBackup
{
    private static readonly Regex Padrao =
        new(@"^nexus_(\d{4}-\d{2}-\d{2})_(\d{6})_([a-z\-]+?)(?:_\d+)?\.db$", RegexOptions.Compiled);

    private static string Sufixo(MotivoBackup m) => m switch
    {
        MotivoBackup.Manual => "manual",
        MotivoBackup.Automatico => "auto",
        MotivoBackup.PreAtualizacao => "pre-atualizacao",
        MotivoBackup.PreLimpeza => "pre-limpeza",
        MotivoBackup.PreImportacao => "pre-importacao",
        _ => "pre-restauracao"
    };

    /// <summary>nexus_2026-10-19_143205_auto.db</summary>
    public static string NomeArquivo(DateTime agora, MotivoBackup motivo) =>
        $"nexus_{agora:yyyy-MM-dd}_{agora:HHmmss}_{Sufixo(motivo)}.db";

    /// <summary>Lê data e motivo do nome. Arquivo que não segue o padrão é ignorado (nunca é listado nem apagado).</summary>
    public static bool TentarLerNome(string nomeArquivo, out DateTime data, out MotivoBackup motivo)
    {
        data = default;
        motivo = default;
        var m = Padrao.Match(nomeArquivo);
        if (!m.Success) return false;
        if (!DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyy-MM-ddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out data)) return false;
        switch (m.Groups[3].Value)
        {
            case "manual": motivo = MotivoBackup.Manual; return true;
            case "auto": motivo = MotivoBackup.Automatico; return true;
            case "pre-atualizacao": motivo = MotivoBackup.PreAtualizacao; return true;
            case "pre-restauracao": motivo = MotivoBackup.PreRestauracao; return true;
            case "pre-limpeza": motivo = MotivoBackup.PreLimpeza; return true;
            case "pre-importacao": motivo = MotivoBackup.PreImportacao; return true;
            default: return false;
        }
    }

    /// <summary>Hora de fazer o backup automático? Intervalo 0 = desligado. Nunca fez = faz.</summary>
    public static bool Devido(DateTime? ultimoBackup, DateTime agora, int intervaloHoras)
    {
        if (intervaloHoras <= 0) return false;
        if (ultimoBackup is null) return true;
        return agora - ultimoBackup.Value >= TimeSpan.FromHours(intervaloHoras);
    }

    /// <summary>
    /// Nunca = ainda não há backup. Com o automático desligado, só manual. Atrasado = passou o DOBRO do intervalo
    /// escolhido sem backup (uma folga, para não alarmar à toa quando o computador ficou desligado).
    /// </summary>
    public static SituacaoBackup Situacao(DateTime? ultimoBackup, DateTime agora, int intervaloHoras)
    {
        if (ultimoBackup is null) return SituacaoBackup.Nunca;
        if (intervaloHoras <= 0) return SituacaoBackup.SoManual;
        return agora - ultimoBackup.Value > TimeSpan.FromHours(intervaloHoras * 2L)
            ? SituacaoBackup.Atrasado
            : SituacaoBackup.EmDia;
    }

    /// <summary>
    /// Quais backups apagar para sobrar só os <paramref name="manter"/> mais recentes dos comuns (manual e automático).
    /// Os de segurança (antes de atualizar ou de restaurar) nunca são apagados por aqui.
    /// </summary>
    public static IReadOnlyList<InfoBackup> SelecionarParaApagar(IEnumerable<InfoBackup> todos, int manter)
    {
        if (manter < 1) return Array.Empty<InfoBackup>();
        return todos
            .Where(b => b.Motivo is MotivoBackup.Manual or MotivoBackup.Automatico)
            .OrderByDescending(b => b.Data)
            .Skip(manter)
            .ToList();
    }

    /// <summary>True se as duas pastas ficam no mesmo disco (backup no mesmo disco não protege de falha do disco).</summary>
    public static bool MesmoDisco(string caminhoA, string caminhoB)
    {
        var a = Path.GetPathRoot(Path.GetFullPath(caminhoA));
        var b = Path.GetPathRoot(Path.GetFullPath(caminhoB));
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
}
