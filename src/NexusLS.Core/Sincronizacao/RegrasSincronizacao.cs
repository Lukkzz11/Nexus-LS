using System.Security.Cryptography;
using System.Text;
using NexusLS.Core.Models;

namespace NexusLS.Core.Sincronizacao;

/// <summary>Um tamanho como vai para o catálogo (nome e quanto há em estoque).</summary>
public record TamanhoPublicavel(string Tamanho, int Estoque);

/// <summary>
/// O produto exatamente como será publicado: só o que a vitrine precisa. Custo, preço de atacado, estoque mínimo
/// e qualquer dado de venda ou de cliente ficam FORA, por decisão de segurança (a lista só cresce com o seu OK).
/// </summary>
public record ProdutoPublicavel(
    Guid Guid,
    string Nome,
    string? Categoria,
    string? Referencia,
    string? CodigoBarras,
    long PrecoVendaCentavos,
    bool SemControleDeEstoque,
    bool Ativo,
    IReadOnlyList<TamanhoPublicavel> Tamanhos)
{
    public int EstoqueTotal => SemControleDeEstoque ? 0 : Tamanhos.Sum(t => t.Estoque);
}

/// <summary>Um produto que não pode ser enviado, e por quê. Nunca é enviado: fica na lista para o usuário corrigir.</summary>
public record ProblemaProduto(Guid Guid, string Nome, string Motivo);

/// <summary>O que já se sabe do produto no Firestore (o que foi confirmado na última sincronização).</summary>
public record EstadoEnviado(string Hash, bool Ativo);

/// <summary>Um produto a enviar, com o hash do que será enviado (guardado depois que o servidor confirmar).</summary>
public record ItemPlano(ProdutoPublicavel Produto, string Hash);

/// <summary>
/// O que a sincronização vai fazer, calculado antes de enviar qualquer coisa (é o que a tela mostra para o
/// usuário confirmar): novos, alterados, desativados, inválidos e quantos ficam como estão.
/// </summary>
public record PlanoSincronizacao(
    IReadOnlyList<ItemPlano> Novos,
    IReadOnlyList<ItemPlano> Alterados,
    IReadOnlyList<ItemPlano> Desativar,
    IReadOnlyList<ProblemaProduto> Invalidos,
    int Inalterados)
{
    public bool TemQueEnviar => Novos.Count + Alterados.Count + Desativar.Count > 0;
    public int TotalAEnviar => Novos.Count + Alterados.Count + Desativar.Count;
}

/// <summary>
/// Regras puras da sincronização (sem banco, sem internet): montar o que se publica, calcular o hash, validar
/// e descobrir o que mudou. Por serem puras, são testadas em milissegundos e valem igual para qualquer destino.
/// </summary>
public static class RegrasSincronizacao
{
    /// <summary>Versão do formato do hash. Se um dia a lista de campos publicados mudar, subir este número força o reenvio de todos.</summary>
    public const string VersaoDoFormato = "v1";

    public static ProdutoPublicavel Montar(Produto p) => new(
        p.Guid,
        p.Nome,
        Vazio(p.Categoria),
        Vazio(p.Referencia),
        Vazio(p.CodigoBarras),
        p.PrecoVendaCentavos,
        p.VenderSemBaixa,
        p.Ativo,
        p.Tamanhos
            .OrderBy(t => t.Tamanho, StringComparer.Ordinal)
            .Select(t => new TamanhoPublicavel(t.Tamanho, t.Estoque))
            .ToList());

    /// <summary>
    /// Hash (SHA-256) do conteúdo publicável. Mesmo conteúdo = mesmo hash, em qualquer computador e a qualquer hora;
    /// qualquer campo publicado que mude (inclusive o estoque de um tamanho) muda o hash.
    /// Não usa data nem relógio: um relógio errado não engana a sincronização.
    /// </summary>
    public static string Hash(ProdutoPublicavel p)
    {
        var tamanhos = string.Join(";", p.Tamanhos
            .OrderBy(t => t.Tamanho, StringComparer.Ordinal)
            .Select(t => $"{t.Tamanho}={t.Estoque}"));
        var texto = string.Join("|",
            VersaoDoFormato,
            p.Guid.ToString("D").ToUpperInvariant(),
            p.Nome,
            p.Categoria ?? "",
            p.Referencia ?? "",
            p.CodigoBarras ?? "",
            p.PrecoVendaCentavos,
            p.SemControleDeEstoque ? 1 : 0,
            p.Ativo ? 1 : 0,
            tamanhos);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    /// <summary>Os motivos pelos quais o produto não pode ser enviado (lista vazia = pode).</summary>
    public static IReadOnlyList<string> Validar(ProdutoPublicavel p)
    {
        var erros = new List<string>();
        if (p.Guid == Guid.Empty) erros.Add("sem identificador global");
        if (string.IsNullOrWhiteSpace(p.Nome)) erros.Add("sem nome");
        if (p.PrecoVendaCentavos < 0) erros.Add("preço de venda negativo");

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in p.Tamanhos)
        {
            if (string.IsNullOrWhiteSpace(t.Tamanho)) erros.Add("tamanho sem nome");
            else if (!vistos.Add(t.Tamanho)) erros.Add($"tamanho {t.Tamanho} repetido");
            if (t.Estoque < 0) erros.Add($"estoque negativo no tamanho {t.Tamanho}");
        }
        return erros;
    }

    /// <summary>
    /// Compara o que há agora com o que foi enviado da última vez e diz o que fazer com cada produto.
    /// Produto ativo: novo, alterado (hash diferente ou estava desativado) ou inalterado.
    /// Produto desativado que está publicado como ativo: desativar (nada é apagado no remoto).
    /// Produto desativado que nunca foi publicado: ignorado, nem entra na conta.
    /// Produto inválido ou com código de barras repetido: vai para a lista de inválidos e NÃO atrapalha os outros.
    /// </summary>
    public static PlanoSincronizacao Planejar(
        IEnumerable<ProdutoPublicavel> atuais, IReadOnlyDictionary<Guid, EstadoEnviado> enviados)
    {
        var novos = new List<ItemPlano>();
        var alterados = new List<ItemPlano>();
        var desativar = new List<ItemPlano>();
        var invalidos = new List<ProblemaProduto>();
        var inalterados = 0;
        var codigosVistos = new Dictionary<string, string>(StringComparer.Ordinal); // código de barras -> nome de quem usou primeiro

        foreach (var p in atuais)
        {
            enviados.TryGetValue(p.Guid, out var estado);

            if (!p.Ativo)
            {
                if (estado is { Ativo: true } && p.Guid != Guid.Empty) desativar.Add(new ItemPlano(p, Hash(p)));
                continue;
            }

            var erros = Validar(p).ToList();
            if (p.CodigoBarras is { } codigo && !codigosVistos.TryAdd(codigo, p.Nome))
                erros.Add($"código de barras {codigo} repetido com \"{codigosVistos[codigo]}\"");
            if (erros.Count > 0)
            {
                invalidos.Add(new ProblemaProduto(p.Guid, p.Nome, string.Join("; ", erros)));
                continue;
            }

            var hash = Hash(p);
            if (estado is null) novos.Add(new ItemPlano(p, hash));
            else if (!estado.Ativo || estado.Hash != hash) alterados.Add(new ItemPlano(p, hash));
            else inalterados++;
        }

        return new PlanoSincronizacao(novos, alterados, desativar, invalidos, inalterados);
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
