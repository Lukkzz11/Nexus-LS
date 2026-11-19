using System.Globalization;
using System.Text;
using NexusLS.Core.Services;

namespace NexusLS.Core.Gaveta;

public enum CategoriaGaveta
{
    Cupons,
    Relatorios,
    NotasFiscais,
    Comprovantes,
    Outros
}

/// <summary>Um arquivo da gaveta, como a tela mostra.</summary>
public sealed record ArquivoGaveta(string Caminho, string Nome, CategoriaGaveta Categoria, long Bytes, DateTime Data)
{
    public string Extensao => Path.GetExtension(Nome).ToLowerInvariant();
}

/// <summary>
/// A "gaveta" da Gestão: uma pasta no computador (dentro da pasta de anexos, por isso entra no backup completo) onde ficam
/// os PDFs e documentos da loja: cupons, relatórios, notas fiscais e comprovantes. Organiza em subpastas por categoria e por
/// mês, nunca sobrescreve um arquivo (nome repetido ganha número) e recusa nome que escape da pasta.
/// </summary>
public class GavetaService
{
    public const string NomePasta = "gaveta";
    public const long TamanhoMaximoBytes = 50L * 1024 * 1024;

    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".xml", ".jpg", ".jpeg", ".png", ".txt", ".csv", ".doc", ".docx", ".xls", ".xlsx", ".zip"
    };

    private readonly string _raiz;
    private readonly Func<DateTime> _agora;

    /// <param name="pastaAnexos">A pasta "anexos" da instalação; a gaveta fica em "anexos\gaveta".</param>
    public GavetaService(string pastaAnexos, Func<DateTime>? agora = null)
    {
        _raiz = Path.Combine(Path.GetFullPath(pastaAnexos), NomePasta);
        _agora = agora ?? (() => DateTime.Now);
    }

    public string Pasta => _raiz;

    public static string Rotulo(CategoriaGaveta c) => c switch
    {
        CategoriaGaveta.Cupons => "Cupons",
        CategoriaGaveta.Relatorios => "Relatórios",
        CategoriaGaveta.NotasFiscais => "Notas fiscais",
        CategoriaGaveta.Comprovantes => "Comprovantes",
        _ => "Outros"
    };

    private static string PastaDa(CategoriaGaveta c) => c.ToString();

    /// <summary>Guarda os bytes na categoria (subpasta do mês). Devolve o caminho final.</summary>
    public string Salvar(CategoriaGaveta categoria, string nomeArquivo, byte[] conteudo)
    {
        if (conteudo.LongLength > TamanhoMaximoBytes)
            throw new RegraDeNegocioException("O arquivo passa de 50 MB e não cabe na gaveta.");
        var nome = NomeSeguro(nomeArquivo);
        var pasta = Path.Combine(_raiz, PastaDa(categoria), _agora().ToString("yyyy-MM", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(pasta);
        var destino = ProximoLivre(pasta, nome);
        var tmp = destino + ".tmp";
        File.WriteAllBytes(tmp, conteudo);
        File.Move(tmp, destino);
        return destino;
    }

    /// <summary>Copia um arquivo que o usuário escolheu para dentro da gaveta.</summary>
    public string Importar(CategoriaGaveta categoria, string caminhoOrigem)
    {
        if (!File.Exists(caminhoOrigem)) throw new RegraDeNegocioException("Arquivo não encontrado.");
        if (new FileInfo(caminhoOrigem).Length > TamanhoMaximoBytes) throw new RegraDeNegocioException("O arquivo passa de 50 MB e não cabe na gaveta.");
        return Salvar(categoria, Path.GetFileName(caminhoOrigem), File.ReadAllBytes(caminhoOrigem));
    }

    /// <summary>Lista os arquivos (opcionalmente de uma categoria e filtrados por parte do nome), do mais novo para o mais antigo.</summary>
    public IReadOnlyList<ArquivoGaveta> Listar(CategoriaGaveta? categoria = null, string? busca = null)
    {
        if (!Directory.Exists(_raiz)) return Array.Empty<ArquivoGaveta>();
        var lista = new List<ArquivoGaveta>();
        foreach (var cat in Enum.GetValues<CategoriaGaveta>())
        {
            if (categoria is not null && categoria != cat) continue;
            var pasta = Path.Combine(_raiz, PastaDa(cat));
            if (!Directory.Exists(pasta)) continue;
            foreach (var f in Directory.EnumerateFiles(pasta, "*", SearchOption.AllDirectories))
            {
                if (f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(f);
                lista.Add(new ArquivoGaveta(f, info.Name, cat, info.Length, info.LastWriteTime));
            }
        }
        var termo = (busca ?? "").Trim();
        return lista.Where(a => termo.Length == 0 || a.Nome.Contains(termo, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(a => a.Data).ThenBy(a => a.Nome).ToList();
    }

    /// <summary>Apaga um arquivo da gaveta. Só aceita caminho que esteja mesmo dentro dela.</summary>
    public void Excluir(string caminho)
    {
        var completo = Path.GetFullPath(caminho);
        var raiz = _raiz.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!completo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
            throw new RegraDeNegocioException("Esse arquivo não está na gaveta.");
        if (File.Exists(completo)) File.Delete(completo);
    }

    private static string NomeSeguro(string nome)
    {
        var baseNome = Path.GetFileName((nome ?? "").Trim());
        var ext = Path.GetExtension(baseNome);
        if (!Extensoes.Contains(ext)) throw new RegraDeNegocioException("Tipo de arquivo não permitido na gaveta (use PDF, imagem, planilha, documento ou XML).");
        var semExt = Path.GetFileNameWithoutExtension(baseNome);
        var sb = new StringBuilder();
        foreach (var c in semExt)
            sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
        var limpo = sb.ToString().Trim().TrimEnd('.');
        if (limpo.Length == 0) limpo = "documento";
        if (limpo.Length > 80) limpo = limpo[..80];
        return limpo + ext.ToLowerInvariant();
    }

    private static string ProximoLivre(string pasta, string nome)
    {
        var caminho = Path.Combine(pasta, nome);
        var semExt = Path.GetFileNameWithoutExtension(nome);
        var ext = Path.GetExtension(nome);
        for (var i = 2; File.Exists(caminho); i++) caminho = Path.Combine(pasta, $"{semExt}_{i}{ext}");
        return caminho;
    }
}
