using System.IO;
using System.Text.Json;

namespace NexusLS.BarTender.Services;

/// <summary>
/// Preferências do BarTender que sobrevivem ao fechar o programa. Hoje guarda o banco
/// escolhido, para o operador não ter que procurar o arquivo toda vez que abrir.
/// </summary>
public sealed class PreferenciasBarTender
{
    public string? CaminhoBanco { get; set; }

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLS",
        "bartender-preferencias.json");

    public static PreferenciasBarTender Carregar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao();

        try
        {
            if (!File.Exists(caminho)) return new PreferenciasBarTender();

            var json = File.ReadAllText(caminho);
            return JsonSerializer.Deserialize<PreferenciasBarTender>(json) ?? new PreferenciasBarTender();
        }
        catch (Exception ex)
        {
            App.Log($"PreferenciasBarTender.Carregar: {ex.Message}");
            return new PreferenciasBarTender();
        }
    }

    public void Salvar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao();

        try
        {
            var pasta = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrEmpty(pasta))
            {
                Directory.CreateDirectory(pasta);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            var temporario = caminho + ".tmp";
            File.WriteAllText(temporario, json);

            if (File.Exists(caminho))
            {
                File.Replace(temporario, caminho, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporario, caminho);
            }
        }
        catch (Exception ex)
        {
            App.Log($"PreferenciasBarTender.Salvar: {ex.Message}");
        }
    }
}
