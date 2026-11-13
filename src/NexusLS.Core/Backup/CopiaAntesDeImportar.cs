namespace NexusLS.Core.Backup;

/// <summary>
/// Gancho para fazer uma cópia do banco antes de qualquer importação (produtos, clientes, ERP). O programa liga o gancho ao
/// backup real; nos testes fica vazio. Se a cópia falhar a importação segue, mas o erro não é engolido em silêncio: fica em UltimoErro.
/// </summary>
public static class CopiaAntesDeImportar
{
    public static Func<Task>? Gancho { get; set; }
    public static string? UltimoErro { get; private set; }

    public static async Task ExecutarAsync()
    {
        UltimoErro = null;
        if (Gancho is null) return;
        try { await Gancho().ConfigureAwait(false); }
        catch (Exception e) { UltimoErro = e.Message; }
    }
}
