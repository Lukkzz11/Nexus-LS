using System.Text.Json;

namespace NexusLS.Core.Services;

/// <summary>
/// Guarda em arquivo os parâmetros da loja da calculadora de preços (impostos, comissão, despesas fixas, margem...),
/// para não digitar tudo de novo a cada uso. O custo da peça e o frete NÃO ficam aqui: mudam a cada produto.
/// </summary>
public class ParametrosCalculadoraStore
{
    private readonly string _caminho;

    public ParametrosCalculadoraStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "calculadora-parametros.json");

    public Dictionary<string, string> Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(); // arquivo ilegível: começa em branco, sem travar a tela
        }
    }

    public void Salvar(Dictionary<string, string> valores)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        File.WriteAllText(_caminho, JsonSerializer.Serialize(valores, new JsonSerializerOptions { WriteIndented = true }));
    }
}
