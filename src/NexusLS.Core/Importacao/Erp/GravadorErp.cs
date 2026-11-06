using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>
/// Um documento a gravar no Firestore do ERP. <see cref="Caminho"/> é sempre <c>empresas/{empresa}/...</c>;
/// <see cref="Mascara"/> lista os campos que o Nexus é dono (o resto do documento do ERP não é tocado);
/// <see cref="Criar"/> indica documento novo (a máscara não se aplica).
/// </summary>
public sealed record DocumentoGravacaoErp(string Caminho, IReadOnlyDictionary<string, object?> Campos, IReadOnlyList<string> Mascara, bool Criar);

/// <summary>Para onde as gravações no formato do ERP iriam. No programa AINDA NÃO há implementação real; os testes usam um destino falso.</summary>
public interface IDestinoErp
{
    Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default);
}

/// <summary>
/// Monta os documentos no formato que o ERP PDV Life espera (ver docs/FIREBASE_MESMO_BANCO_DO_ERP.md). Só monta:
/// não envia nada. Preços em REAIS (número), grade como mapa tamanho para quantidade, datas como <see cref="DateTime"/> UTC.
/// </summary>
public static class MontadorErp
{
    public const string Origem = "nexus-ls";

    /// <summary>Campos do produto de que o Nexus é dono quando atualiza um produto que já existe no ERP.</summary>
    public static readonly string[] CamposProduto =
    {
        "nome", "categoria", "referencia", "codigoBarras", "precoVarejo", "precoAtacado", "ativo", "tipoEstoque",
        "empresaId", "origem", "nexusGuid", "atualizadoEm"
    };

    /// <summary>
    /// <c>empresas/{empresa}/produtos/{id}</c>. O id é o do ERP quando o produto veio de lá (<paramref name="idNoErp"/>),
    /// senão o Guid do Nexus. O estoque (<c>gradeTamanhos</c>, <c>estoqueTotal</c>) só entra com
    /// <paramref name="incluirEstoque"/>: com os dois sistemas gravando o saldo, quem grava por último sobrescreve o outro.
    /// </summary>
    public static DocumentoGravacaoErp Produto(string empresaId, Produto p, IEnumerable<ProdutoTamanho> tamanhos, DateTime agoraUtc,
        string? idNoErp = null, bool novo = false, bool incluirEstoque = false)
    {
        var id = string.IsNullOrWhiteSpace(idNoErp) ? p.Guid.ToString("D").ToLowerInvariant() : idNoErp.Trim();
        var campos = new Dictionary<string, object?>
        {
            ["nome"] = p.Nome,
            ["categoria"] = p.Categoria ?? "",
            ["referencia"] = p.Referencia ?? "",
            ["codigoBarras"] = p.CodigoBarras ?? "",
            ["precoVarejo"] = p.PrecoVendaCentavos / 100.0,
            ["precoAtacado"] = p.PrecoAtacadoCentavos > 0 ? p.PrecoAtacadoCentavos / 100.0 : p.PrecoVendaCentavos * 0.6 / 100.0,
            ["ativo"] = p.Ativo,
            ["tipoEstoque"] = "loja",
            ["empresaId"] = empresaId,
            ["origem"] = Origem,
            ["nexusGuid"] = p.Guid.ToString("D").ToLowerInvariant(),
            ["atualizadoEm"] = agoraUtc
        };
        var mascara = CamposProduto.ToList();
        if (incluirEstoque)
        {
            var grade = new Dictionary<string, object?>();
            foreach (var t in tamanhos.Where(t => !t.EhSemTamanho)) grade[t.Tamanho] = t.Estoque;
            campos["gradeTamanhos"] = grade;
            campos["estoqueTotal"] = p.VenderSemBaixa ? 0 : tamanhos.Sum(t => t.Estoque);
            mascara.AddRange(new[] { "gradeTamanhos", "estoqueTotal" });
        }
        if (novo)
        {
            campos["criadoEm"] = agoraUtc;
            campos["ativoNoCatalogo"] = true;
            campos["destaque"] = false;
            campos["cores"] = new[] { "Padrão" };
            campos["descricao"] = "";
        }
        return new DocumentoGravacaoErp($"empresas/{empresaId}/produtos/{id}", campos, mascara, novo);
    }

    /// <summary>
    /// <c>empresas/{empresa}</c> campo <c>vitrine</c>: a vitrine do editor (o JSON do editor, que já segue o formato do
    /// <c>vitrineConfig.js</c> do ERP) vai como mapa no campo <c>vitrine</c>, mais o carimbo <c>atualizadoEm</c>. Só estes dois campos entram na máscara.
    /// </summary>
    public static DocumentoGravacaoErp Vitrine(string empresaId, string vitrineJson, DateTime agoraUtc)
    {
        object? mapa;
        try { mapa = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(vitrineJson); }
        catch (System.Text.Json.JsonException) { throw new RegraDeNegocioException("A vitrine é inválida e não pode ser montada."); }
        return new DocumentoGravacaoErp($"empresas/{empresaId}",
            new Dictionary<string, object?> { ["vitrine"] = mapa, ["atualizadoEm"] = agoraUtc },
            new[] { "vitrine", "atualizadoEm" }, false);
    }
}

/// <summary>
/// A única porta de saída para gravar no ERP. Recusa tudo enquanto <see cref="ConfiguracaoErp.GravacaoAtivada"/> for falso
/// (o padrão) e recusa documentos fora de <c>empresas/{empresa}/...</c>.
/// </summary>
public sealed class GravadorErp
{
    private readonly ConfiguracaoErp _config;
    private readonly IDestinoErp _destino;

    public GravadorErp(ConfiguracaoErp config, IDestinoErp destino)
    {
        _config = config;
        _destino = destino;
    }

    public Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default)
    {
        if (!_config.GravacaoAtivada)
            throw new RegraDeNegocioException("A gravação no ERP está desligada (o Nexus só lê o ERP). Veja docs/FIREBASE_MESMO_BANCO_DO_ERP.md antes de ligar.");
        var prefixo = $"empresas/{_config.EmpresaId}";
        foreach (var d in documentos)
            if (d.Caminho != prefixo && !d.Caminho.StartsWith(prefixo + "/", StringComparison.Ordinal))
                throw new RegraDeNegocioException($"Documento fora da empresa configurada: {d.Caminho}.");
        return documentos.Count == 0 ? Task.CompletedTask : _destino.GravarAsync(documentos, ct);
    }
}
