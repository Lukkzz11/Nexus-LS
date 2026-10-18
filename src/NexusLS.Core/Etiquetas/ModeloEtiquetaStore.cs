using System.Text.Json;

namespace NexusLS.Core.Etiquetas;

/// <summary>
/// Modelos de papel salvos pelo usuário (bobinas de tamanho próprio), num arquivo JSON.
/// Os três modelos de fábrica ficam no código e não vão para o arquivo.
/// </summary>
public class ModeloEtiquetaStore
{
    public const double MinimoMm = 20;
    public const double MaximoMm = 200;

    private readonly string _caminho;

    public ModeloEtiquetaStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "etiquetas.json");

    /// <summary>Os modelos salvos. Arquivo ausente ou estragado vira lista vazia (nunca derruba o programa).</summary>
    public List<FormatoEtiqueta> Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<List<FormatoEtiqueta>>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    /// <summary>Acrescenta uma bobina com nome e medidas próprios. Devolve o modelo criado.</summary>
    public FormatoEtiqueta AdicionarBobina(string nome, double larguraMm, double alturaMm)
    {
        nome = (nome ?? "").Trim();
        if (nome.Length == 0)
            throw new Services.RegraDeNegocioException("Dê um nome ao modelo.");
        if (larguraMm < MinimoMm || larguraMm > MaximoMm || alturaMm < MinimoMm || alturaMm > MaximoMm)
            throw new Services.RegraDeNegocioException($"Largura e altura devem ficar entre {MinimoMm} e {MaximoMm} mm.");

        var existentes = Carregar();
        var nomeCompleto = $"{nome} ({larguraMm:0.##} × {alturaMm:0.##} mm)";
        if (FormatoEtiqueta.Todos.Concat(existentes).Any(f => string.Equals(f.Nome, nomeCompleto, StringComparison.OrdinalIgnoreCase)))
            throw new Services.RegraDeNegocioException("Já existe um modelo com esse nome e essas medidas.");

        var modelo = new FormatoEtiqueta(nomeCompleto, larguraMm, alturaMm, 1, 1, larguraMm, alturaMm, 0, 0, 0, 0);
        existentes.Add(modelo);
        Gravar(existentes);
        return modelo;
    }

    public void Remover(FormatoEtiqueta modelo)
    {
        var lista = Carregar();
        if (lista.RemoveAll(f => f.Nome == modelo.Nome) > 0) Gravar(lista);
    }

    private void Gravar(List<FormatoEtiqueta> lista)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var temporario = _caminho + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(lista, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporario, _caminho, overwrite: true);
    }
}
