using System.Text.Json;

namespace NexusLS.Core.Consultor;

/// <summary>
/// Cartões que o usuário dispensou, guardados num JSON deste computador (não vai para o banco nem para a nuvem; não exige
/// migração). Cada dispensa vale 7 dias: depois disso o cartão pode voltar, se a situação ainda existir.
/// </summary>
public sealed class DispensasConsultorStore
{
    public const int DiasDeDispensa = 7;

    private sealed class Arquivo
    {
        /// <summary>Id do cartão → até quando fica escondido.</summary>
        public Dictionary<string, DateTime> Dispensadas { get; set; } = new();
    }

    private readonly string _caminho;
    private readonly object _trava = new();

    public DispensasConsultorStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "consultor.json");

    /// <summary>Esconde o cartão por 7 dias a partir de <paramref name="agora"/>.</summary>
    public void Dispensar(string id, DateTime agora)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        lock (_trava)
        {
            var a = Ler();
            a.Dispensadas[id] = agora.AddDays(DiasDeDispensa);
            Gravar(a);
        }
    }

    /// <summary>Os ids que ainda estão escondidos em <paramref name="agora"/>.</summary>
    public IReadOnlySet<string> Ativas(DateTime agora)
    {
        lock (_trava)
            return Ler().Dispensadas.Where(p => p.Value > agora).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Traz de volta todos os cartões dispensados.</summary>
    public void RestaurarTodas()
    {
        lock (_trava) Gravar(new Arquivo());
    }

    private Arquivo Ler()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<Arquivo>(File.ReadAllText(_caminho)) is { Dispensadas: not null } a ? a : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    private void Gravar(Arquivo a)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_caminho))!);
            var tmp = _caminho + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(a, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* não conseguiu guardar: o cartão volta na próxima abertura, sem travar o programa */ }
    }
}
