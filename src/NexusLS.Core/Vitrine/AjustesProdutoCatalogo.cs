using System.Text.Json;

namespace NexusLS.Core.Vitrine;

/// <summary>
/// Como um produto aparece no catálogo online: se aparece, se é destaque e o preço promocional (centavos, 0 = sem promoção).
/// Fica à parte do cadastro do produto: é decisão da vitrine, não do estoque nem do PDV.
/// </summary>
public record AjusteProdutoCatalogo(bool Visivel = true, bool Destaque = false, long PromocaoCentavos = 0);

/// <summary>Guarda os ajustes por produto (pelo Guid) num arquivo desta instalação.</summary>
public class AjustesProdutoStore
{
    private readonly string _caminho;
    public AjustesProdutoStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "catalogo-produtos.json");

    public Dictionary<string, AjusteProdutoCatalogo> Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, AjusteProdutoCatalogo>>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Salvar(Dictionary<string, AjusteProdutoCatalogo> ajustes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(ajustes, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}
