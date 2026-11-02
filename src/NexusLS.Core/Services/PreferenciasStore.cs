using System.Text.Json;

namespace NexusLS.Core.Services;

/// <summary>Preferências deste computador (não vão para o banco nem para a nuvem).</summary>
public class Preferencias
{
    /// <summary>Tamanho das telas em % (90 a 150). 100 = normal.</summary>
    public int ZoomPercentual { get; set; } = 100;

    /// <summary>Desconto sugerido por peça na Venda Cliente, em centavos.</summary>
    public long DescontoPorPecaCentavos { get; set; } = 200;

    public static readonly int[] ZoomsPermitidos = { 90, 100, 110, 125, 150 };

    public void Normalizar()
    {
        if (ZoomPercentual is < 80 or > 160) ZoomPercentual = 100;
        if (DescontoPorPecaCentavos is < 0 or > 100_000) throw new RegraDeNegocioException("O desconto por peça deve ficar entre R$ 0,00 e R$ 1.000,00.");
    }
}

public class PreferenciasStore
{
    private readonly string _caminho;
    public PreferenciasStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "preferencias.json");

    public Preferencias Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            var p = JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(_caminho)) ?? new();
            if (p.ZoomPercentual is < 80 or > 160) p.ZoomPercentual = 100;
            if (p.DescontoPorPecaCentavos is < 0 or > 100_000) p.DescontoPorPecaCentavos = 200;
            return p;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Salvar(Preferencias p)
    {
        p.Normalizar();
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(p, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}
