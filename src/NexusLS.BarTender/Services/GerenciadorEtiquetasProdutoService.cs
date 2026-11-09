using System.IO;
using System.Text.Json;
using NexusLS.BarTender.Models.Designer;

namespace NexusLS.BarTender.Services;

public static class GerenciadorEtiquetasProdutoService
{
    private static readonly string PastaEtiquetas = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLS",
        "EtiquetasProdutos"
    );

    static GerenciadorEtiquetasProdutoService()
    {
        try
        {
            if (!Directory.Exists(PastaEtiquetas))
            {
                Directory.CreateDirectory(PastaEtiquetas);
            }
        }
        catch { }
    }

    private static string ObterCaminhoArquivo(int produtoId) =>
        Path.Combine(PastaEtiquetas, $"produto_{produtoId}.json");

    public static bool TemEtiquetaPersonalizada(int produtoId)
    {
        try
        {
            return File.Exists(ObterCaminhoArquivo(produtoId));
        }
        catch
        {
            return false;
        }
    }

    public static (bool TemPersonalizada, string NomeModelo, string Dimensoes) ObterInfoEtiqueta(int produtoId)
    {
        try
        {
            var caminho = ObterCaminhoArquivo(produtoId);
            if (!File.Exists(caminho))
            {
                return (false, "Padrão do Sistema", "50 × 30 mm");
            }

            var json = File.ReadAllText(caminho);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var nome = root.TryGetProperty("Nome", out var pNome) ? pNome.GetString() ?? "Personalizada" : "Personalizada";
            var larg = root.TryGetProperty("LarguraMm", out var pLarg) ? pLarg.GetDouble() : 50.0;
            var alt = root.TryGetProperty("AlturaMm", out var pAlt) ? pAlt.GetDouble() : 30.0;

            return (true, nome, $"{larg:0.#} × {alt:0.#} mm");
        }
        catch
        {
            return (false, "Padrão do Sistema", "50 × 30 mm");
        }
    }

    public static ModeloEtiquetaDesign? CarregarEtiquetaProduto(int produtoId)
    {
        try
        {
            var caminho = ObterCaminhoArquivo(produtoId);
            if (!File.Exists(caminho)) return null;
            return ModeloEtiquetaDesign.Carregar(caminho);
        }
        catch
        {
            return null;
        }
    }

    public static bool SalvarEtiquetaProduto(int produtoId, ModeloEtiquetaDesign modelo)
    {
        try
        {
            if (!Directory.Exists(PastaEtiquetas))
            {
                Directory.CreateDirectory(PastaEtiquetas);
            }

            var caminho = ObterCaminhoArquivo(produtoId);
            modelo.Salvar(caminho);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool RemoverEtiquetaProduto(int produtoId)
    {
        try
        {
            var caminho = ObterCaminhoArquivo(produtoId);
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }
}
