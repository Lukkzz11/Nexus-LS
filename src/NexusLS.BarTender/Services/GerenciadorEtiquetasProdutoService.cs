using System.IO;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Models;

namespace NexusLS.BarTender.Services;

/// <summary>
/// Etiqueta exclusiva de cada produto. Fica na tabela EtiquetasProduto do banco do Nexus (assim entra no backup do Nexus).
/// Se o banco ainda não tem a tabela (Nexus antigo, ainda não atualizado), cai para arquivo JSON em
/// %LOCALAPPDATA%\NexusLS\EtiquetasProdutos, como era antes. Os arquivos antigos são movidos para o banco
/// na primeira leitura em que ele aceita e ficam como "produto_N.json.migrado" (nada é apagado).
/// </summary>
public static class GerenciadorEtiquetasProdutoService
{
    public sealed record InfoEtiqueta(string Nome, double LarguraMm, double AlturaMm);

    private static readonly string PastaEtiquetas = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLS",
        "EtiquetasProdutos");

    private static readonly object Trava = new();
    private static Dictionary<int, InfoEtiqueta>? _cache;

    /// <summary>Banco em uso. Sem banco (ou sem a tabela), vale só o arquivo.</summary>
    public static BancoDadosService? Banco { get; set; }

    private static string ObterCaminhoArquivo(int produtoId) =>
        Path.Combine(PastaEtiquetas, $"produto_{produtoId}.json");

    /// <summary>Esquece o que foi lido: a próxima consulta lê de novo (use ao trocar de banco ou recarregar).</summary>
    public static void Invalidar()
    {
        lock (Trava) _cache = null;
    }

    // ---------------------------------------------------------------- consulta

    public static bool TemEtiquetaPersonalizada(int produtoId) => Indice().ContainsKey(produtoId);

    public static (bool TemPersonalizada, string NomeModelo, string Dimensoes) ObterInfoEtiqueta(int produtoId)
    {
        if (!Indice().TryGetValue(produtoId, out var info))
            return (false, "Padrão do Sistema", "50 × 30 mm");

        return (true, info.Nome, $"{info.LarguraMm:0.#} × {info.AlturaMm:0.#} mm");
    }

    public static ModeloEtiquetaDesign? CarregarEtiquetaProduto(int produtoId)
    {
        try
        {
            var json = LerJsonDoBanco(produtoId);
            if (json is not null)
                return JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, ModeloEtiquetaDesign.JsonOpts);

            var caminho = ObterCaminhoArquivo(produtoId);
            return File.Exists(caminho) ? ModeloEtiquetaDesign.Carregar(caminho) : null;
        }
        catch (Exception ex)
        {
            App.Log($"CarregarEtiquetaProduto({produtoId}) falhou: {ex.Message}");
            return null;
        }
    }

    // ------------------------------------------------------------------ gravação

    public static bool SalvarEtiquetaProduto(int produtoId, ModeloEtiquetaDesign modelo)
    {
        try
        {
            if (GravarNoBanco(produtoId, modelo))
            {
                Invalidar();
                return true;
            }

            // Banco sem a tabela (Nexus antigo): guarda em arquivo, como antes.
            Directory.CreateDirectory(PastaEtiquetas);
            modelo.Salvar(ObterCaminhoArquivo(produtoId));
            Invalidar();
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"SalvarEtiquetaProduto({produtoId}) falhou: {ex.Message}");
            return false;
        }
    }

    public static bool RemoverEtiquetaProduto(int produtoId)
    {
        var removeu = false;
        try
        {
            var banco = Banco;
            if (banco is not null && banco.BancoExiste())
            {
                try
                {
                    using var db = banco.CriarContexto();
                    var linha = db.EtiquetasProduto.FirstOrDefault(e => e.ProdutoId == produtoId);
                    if (linha is not null)
                    {
                        db.EtiquetasProduto.Remove(linha);
                        db.SaveChanges();
                        removeu = true;
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"RemoverEtiquetaProduto({produtoId}) no banco falhou: {ex.Message}");
                }
            }

            var caminho = ObterCaminhoArquivo(produtoId);
            if (File.Exists(caminho))
            {
                File.Delete(caminho);
                removeu = true;
            }
        }
        catch (Exception ex)
        {
            App.Log($"RemoverEtiquetaProduto({produtoId}) falhou: {ex.Message}");
        }

        Invalidar();
        return removeu;
    }

    // ------------------------------------------------------------------ detalhes

    private static Dictionary<int, InfoEtiqueta> Indice()
    {
        lock (Trava)
        {
            if (_cache is not null) return _cache;

            var indice = new Dictionary<int, InfoEtiqueta>();
            var bancoLido = false;

            try
            {
                var banco = Banco;
                if (banco is not null && banco.BancoExiste())
                {
                    using var db = banco.CriarContexto();
                    foreach (var e in db.EtiquetasProduto.AsNoTracking().Select(e => new { e.ProdutoId, e.Nome, e.LarguraMm, e.AlturaMm }))
                        indice[e.ProdutoId] = new InfoEtiqueta(e.Nome, e.LarguraMm, e.AlturaMm);
                    bancoLido = true;
                }
            }
            catch (Exception ex)
            {
                // Normalmente: o banco ainda não tem a tabela (Nexus não atualizado).
                App.Log($"EtiquetasProduto: banco indisponível, usando arquivos ({ex.Message})");
            }

            foreach (var arquivo in ArquivosAntigos())
            {
                if (!int.TryParse(Path.GetFileNameWithoutExtension(arquivo)["produto_".Length..], out var id)) continue;
                if (indice.ContainsKey(id)) continue; // o banco manda

                if (bancoLido && MigrarArquivoParaBanco(id, arquivo, out var info))
                    indice[id] = info;
                else if (LerInfoDoArquivo(arquivo) is { } doArquivo)
                    indice[id] = doArquivo;
            }

            return _cache = indice;
        }
    }

    private static IEnumerable<string> ArquivosAntigos()
    {
        try
        {
            return Directory.Exists(PastaEtiquetas)
                ? Directory.EnumerateFiles(PastaEtiquetas, "produto_*.json").ToList()
                : Array.Empty<string>();
        }
        catch (Exception ex)
        {
            App.Log($"EtiquetasProduto: não foi possível listar a pasta de arquivos ({ex.Message})");
            return Array.Empty<string>();
        }
    }

    private static string? LerJsonDoBanco(int produtoId)
    {
        var banco = Banco;
        if (banco is null || !banco.BancoExiste()) return null;

        try
        {
            using var db = banco.CriarContexto();
            return db.EtiquetasProduto.AsNoTracking()
                .Where(e => e.ProdutoId == produtoId)
                .Select(e => e.Json)
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            App.Log($"LerJsonDoBanco({produtoId}): {ex.Message}");
            return null;
        }
    }

    /// <summary>Grava ou atualiza a linha do produto. Devolve false se o banco não está disponível para isso.</summary>
    private static bool GravarNoBanco(int produtoId, ModeloEtiquetaDesign modelo)
    {
        var banco = Banco;
        if (banco is null || !banco.BancoExiste()) return false;

        try
        {
            var json = JsonSerializer.Serialize(modelo, ModeloEtiquetaDesign.JsonOpts);
            using var db = banco.CriarContexto();
            var linha = db.EtiquetasProduto.FirstOrDefault(e => e.ProdutoId == produtoId);
            if (linha is null)
            {
                linha = new EtiquetaProduto { ProdutoId = produtoId };
                db.EtiquetasProduto.Add(linha);
            }

            linha.Nome = string.IsNullOrWhiteSpace(modelo.Nome) ? "Personalizada" : modelo.Nome;
            linha.LarguraMm = modelo.LarguraMm;
            linha.AlturaMm = modelo.AlturaMm;
            linha.Json = json;
            linha.AtualizadoEm = DateTime.UtcNow;
            db.SaveChanges();

            // Se sobrou arquivo antigo deste produto, ele não vale mais: guarda como cópia.
            var antigo = ObterCaminhoArquivo(produtoId);
            if (File.Exists(antigo)) File.Move(antigo, antigo + ".migrado", overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"GravarNoBanco({produtoId}) falhou, usando arquivo: {ex.Message}");
            return false;
        }
    }

    private static bool MigrarArquivoParaBanco(int produtoId, string arquivo, out InfoEtiqueta info)
    {
        info = new InfoEtiqueta("Personalizada", 50, 30);
        try
        {
            var json = File.ReadAllText(arquivo);
            var modelo = JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, ModeloEtiquetaDesign.JsonOpts);
            if (modelo is null) return false;

            if (!GravarNoBanco(produtoId, modelo)) return false;

            info = new InfoEtiqueta(string.IsNullOrWhiteSpace(modelo.Nome) ? "Personalizada" : modelo.Nome, modelo.LarguraMm, modelo.AlturaMm);
            App.Log($"Etiqueta do produto {produtoId} movida do arquivo para o banco.");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"MigrarArquivoParaBanco({produtoId}) falhou: {ex.Message}");
            return false;
        }
    }

    private static InfoEtiqueta? LerInfoDoArquivo(string arquivo)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(arquivo));
            var root = doc.RootElement;
            var nome = root.TryGetProperty("Nome", out var pNome) ? pNome.GetString() ?? "Personalizada" : "Personalizada";
            var larg = root.TryGetProperty("LarguraMm", out var pLarg) ? pLarg.GetDouble() : 50.0;
            var alt = root.TryGetProperty("AlturaMm", out var pAlt) ? pAlt.GetDouble() : 30.0;
            return new InfoEtiqueta(nome, larg, alt);
        }
        catch (Exception ex)
        {
            App.Log($"LerInfoDoArquivo({Path.GetFileName(arquivo)}): {ex.Message}");
            return null;
        }
    }
}
