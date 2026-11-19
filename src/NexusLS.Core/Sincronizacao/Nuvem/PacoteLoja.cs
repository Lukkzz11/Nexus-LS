using System.Globalization;
using System.Text.Json.Nodes;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Sincronizacao.Nuvem;

// Pacote que cada loja envia à nuvem (PROPOSTA_GESTAO.md, seção 7). Aqui só há classes e conversão para/de "campos do Firestore"
// (o formato da API REST: {"campo": {"integerValue": "12"}}). NÃO há chamada de rede e nada aqui toca em coleções do ERP Life.

/// <summary>Documento da loja: empresas/{empresaId}/lojas/{lojaId}.</summary>
public sealed record LojaNuvem(string LojaId, string Nome, DateTime AtualizadoEmUtc, string VersaoNexus);

/// <summary>Uma linha do resumo do dia: o que foi vendido de um produto num tamanho.</summary>
public sealed record ItemDiaNuvem(string ProdutoGuid, string Nome, string? Referencia, string Tamanho, int Pecas, long ValorCentavos);

/// <summary>Documento do dia: empresas/{empresaId}/lojas/{lojaId}/dias/{AAAA-MM-DD}. Reenviar o mesmo dia substitui o documento.</summary>
public sealed record ResumoDiaNuvem(
    DateTime Dia, long FaturamentoCentavos, int NumeroDeVendas, int Pecas,
    int Canceladas, long CanceladasCentavos, int Trocas, long TrocasCentavos,
    IReadOnlyDictionary<string, long> PorFormaPagamento, IReadOnlyList<ItemDiaNuvem> PorProduto, DateTime AtualizadoEmUtc)
{
    /// <summary>AAAA-MM-DD, o id do documento.</summary>
    public string Id => Dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Documento do estoque: empresas/{empresaId}/lojas/{lojaId}/estoque/{produtoGuid}: foto do momento do envio.</summary>
public sealed record EstoqueProdutoNuvem(string ProdutoGuid, string Nome, string? Referencia, string? Categoria, long CustoCentavos,
    long PrecoVarejoCentavos, IReadOnlyDictionary<string, int> PorTamanho, int Total, DateTime AtualizadoEmUtc);

/// <summary>Documento do lançamento: empresas/{empresaId}/lojas/{lojaId}/lancamentos/{guid}. Do anexo vai só o NOME (o arquivo fica na loja).</summary>
public sealed record LancamentoNuvem(string Id, DateTime Data, string Tipo, string Categoria, string Descricao, long ValorCentavos,
    string? Forma, string? Observacao, IReadOnlyList<string> NomesDosAnexos, DateTime AtualizadoEmUtc);

/// <summary>Documento do preço: empresas/{empresaId}/precos/{produtoGuid}/lojas/{lojaId}. Definido só pelo administrador.</summary>
public sealed record PrecoLojaNuvem(string ProdutoGuid, string LojaId, long PrecoVarejoCentavos, long? PrecoAtacadoCentavos,
    string DefinidoPor, DateTime AtualizadoEmUtc);

/// <summary>Monta os documentos do pacote a partir do banco local (funções puras).</summary>
public static class MontadorPacoteLoja
{
    private static string Tam(string t) => t == Tamanhos.SemTamanho ? "" : t;

    public static LojaNuvem Loja(string lojaId, string nome, string versaoNexus, DateTime agoraUtc) =>
        new(CaminhosNuvem.ValidarId(lojaId, "lojaId"), (nome ?? "").Trim(), agoraUtc, versaoNexus ?? "");

    /// <summary>
    /// Resumo de UM dia. Vendas canceladas só entram na contagem de canceladas. A venda enviada a Pedidos continua contando
    /// no faturamento. Valores por forma seguem o relatório financeiro (parte paga com vale-troca em "ValeTroca").
    /// </summary>
    public static ResumoDiaNuvem ResumoDoDia(DateTime dia, IEnumerable<Venda> vendasComItens, IEnumerable<Produto> produtos,
        IEnumerable<Devolucao>? devolucoes, DateTime agoraUtc)
    {
        var d = dia.Date;
        var todas = vendasComItens.Where(v => v.DataHora.Date == d).ToList();
        var ativas = todas.Where(v => !v.Cancelada).ToList();
        var canceladas = todas.Where(v => v.Cancelada).ToList();
        var trocas = (devolucoes ?? Array.Empty<Devolucao>()).Where(x => !x.Cancelada && x.DataHora.Date == d).ToList();
        var cad = produtos.ToDictionary(p => p.Id);

        var fin = RelatorioFinanceiro.Gerar(ativas, Array.Empty<LancamentoEmpresa>(), null, new DesempenhoVendas.Intervalo(d, d));
        var formas = fin.PorFormaPagamento.Where(f => f.Forma is not null && f.VendasCentavos != 0)
            .ToDictionary(f => f.Forma!.Value.ToString(), f => f.VendasCentavos);

        var itens = new List<(int ProdutoId, string Nome, string Tam, int Qtd, long Valor)>();
        foreach (var v in ativas)
        {
            var lista = v.Itens.Where(i => i.Quantidade > 0).ToList();
            var bruto = lista.Sum(i => i.TotalCentavos);
            if (lista.Count == 0 || bruto <= 0) continue;
            foreach (var (item, valor) in DesempenhoVendas.ValoresLiquidos(v, lista, bruto))
                itens.Add((item.ProdutoId, item.NomeProduto, Tam(item.Tamanho), item.Quantidade, valor));
        }
        var porProduto = itens.GroupBy(i => (i.ProdutoId, i.Tam)).Select(g =>
        {
            cad.TryGetValue(g.Key.ProdutoId, out var p);
            return new ItemDiaNuvem(p?.Guid.ToString("D") ?? "", g.First().Nome, p?.Referencia, g.Key.Tam, g.Sum(x => x.Qtd), g.Sum(x => x.Valor));
        }).OrderBy(i => i.Nome, StringComparer.Ordinal).ThenBy(i => i.Tamanho, StringComparer.Ordinal).ToList();

        return new ResumoDiaNuvem(d, fin.VendasCentavos, ativas.Count, ativas.Sum(v => v.Itens.Where(i => i.Quantidade > 0).Sum(i => i.Quantidade)),
            canceladas.Count, canceladas.Sum(v => v.TotalCentavos), trocas.Count, trocas.Sum(t => t.ValorCentavos),
            formas, porProduto, agoraUtc);
    }

    public static List<EstoqueProdutoNuvem> Estoque(IEnumerable<Produto> produtos, DateTime agoraUtc) => produtos
        .Where(p => p.Ativo && !p.VenderSemBaixa)
        .Select(p => new EstoqueProdutoNuvem(p.Guid.ToString("D"), p.Nome, p.Referencia, p.Categoria, p.PrecoCustoCentavos, p.PrecoVendaCentavos,
            p.Tamanhos.GroupBy(t => Tam(t.Tamanho)).ToDictionary(g => g.Key, g => g.Sum(t => t.Estoque)),
            p.EstoqueAtual, agoraUtc))
        .OrderBy(e => e.Nome, StringComparer.Ordinal).ToList();

    public static LancamentoNuvem Lancamento(LancamentoEmpresa l, DateTime agoraUtc) =>
        new(l.Guid.ToString("D"), l.Data.Date, l.Tipo.ToString(), l.Categoria, l.Descricao, l.ValorCentavos, l.Forma?.ToString(), l.Observacao,
            l.Anexos.OrderBy(a => a.Id).Select(a => a.NomeOriginal).ToList(), agoraUtc);
}

/// <summary>Caminhos dos documentos. Só gera caminhos dentro de empresas/{empresaId}/lojas e empresas/{empresaId}/precos.</summary>
public static class CaminhosNuvem
{
    /// <summary>Id seguro de documento: letras, números, "-" e "_" (sem "/", "." nem espaços), de 1 a 64 caracteres.</summary>
    public static string ValidarId(string? id, string campo)
    {
        var v = (id ?? "").Trim();
        if (v.Length is 0 or > 64 || !v.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new RegraDeNegocioException($"Identificação inválida ({campo}): use só letras, números, - e _.");
        return v;
    }

    public static string Loja(string empresaId, string lojaId) => $"empresas/{ValidarId(empresaId, "empresaId")}/lojas/{ValidarId(lojaId, "lojaId")}";
    public static string Dia(string empresaId, string lojaId, DateTime dia) => $"{Loja(empresaId, lojaId)}/dias/{dia:yyyy-MM-dd}";
    public static string Estoque(string empresaId, string lojaId, string produtoGuid) => $"{Loja(empresaId, lojaId)}/estoque/{ValidarId(produtoGuid, "produto")}";
    public static string Lancamento(string empresaId, string lojaId, string id) => $"{Loja(empresaId, lojaId)}/lancamentos/{ValidarId(id, "lançamento")}";
    public static string Preco(string empresaId, string produtoGuid, string lojaId) =>
        $"empresas/{ValidarId(empresaId, "empresaId")}/precos/{ValidarId(produtoGuid, "produto")}/lojas/{ValidarId(lojaId, "lojaId")}";
}

/// <summary>
/// Conversão dos documentos para/de "campos do Firestore" (formato REST: cada campo é um objeto com o tipo). Determinística:
/// o mesmo dado gera sempre o mesmo JSON, então reenviar substitui sem criar duplicado. Ao ler, campo ausente vira o valor
/// padrão e campo de tipo errado gera <see cref="FormatException"/>.
/// </summary>
public static class SerializadorPacoteLoja
{
    // ---------------------------------------------------------------- loja
    public static JsonObject ParaCampos(LojaNuvem l) => new()
    {
        ["lojaId"] = Texto(l.LojaId), ["nome"] = Texto(l.Nome), ["atualizadoEm"] = Tempo(l.AtualizadoEmUtc), ["versaoNexus"] = Texto(l.VersaoNexus)
    };

    public static LojaNuvem LojaDeCampos(JsonObject c) =>
        new(LerTexto(c, "lojaId"), LerTexto(c, "nome"), LerTempo(c, "atualizadoEm"), LerTexto(c, "versaoNexus"));

    // ---------------------------------------------------------------- dia
    public static JsonObject ParaCampos(ResumoDiaNuvem d)
    {
        var formas = new JsonObject();
        foreach (var f in d.PorFormaPagamento.OrderBy(k => k.Key, StringComparer.Ordinal)) formas[f.Key] = Inteiro(f.Value);
        var itens = d.PorProduto.Select(i => (JsonNode)Mapa(new JsonObject
        {
            ["produtoGuid"] = Texto(i.ProdutoGuid), ["nome"] = Texto(i.Nome), ["referencia"] = Texto(i.Referencia ?? ""),
            ["tamanho"] = Texto(i.Tamanho), ["pecas"] = Inteiro(i.Pecas), ["valorCentavos"] = Inteiro(i.ValorCentavos)
        })).ToArray();
        return new JsonObject
        {
            ["dia"] = Texto(d.Id), ["faturamentoCentavos"] = Inteiro(d.FaturamentoCentavos), ["numeroDeVendas"] = Inteiro(d.NumeroDeVendas),
            ["pecas"] = Inteiro(d.Pecas), ["canceladas"] = Inteiro(d.Canceladas), ["canceladasCentavos"] = Inteiro(d.CanceladasCentavos),
            ["trocas"] = Inteiro(d.Trocas), ["trocasCentavos"] = Inteiro(d.TrocasCentavos),
            ["porFormaPagamento"] = Mapa(formas), ["porProduto"] = Lista(itens), ["atualizadoEm"] = Tempo(d.AtualizadoEmUtc)
        };
    }

    public static ResumoDiaNuvem DiaDeCampos(JsonObject c)
    {
        var dia = DateTime.ParseExact(LerTexto(c, "dia"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var formas = LerMapa(c, "porFormaPagamento")?.ToDictionary(k => k.Key, k => ValorInteiro(k.Value!, k.Key)) ?? new Dictionary<string, long>();
        var itens = LerLista(c, "porProduto").Select(n =>
        {
            var m = ConteudoMapa(n, "porProduto");
            return new ItemDiaNuvem(LerTexto(m, "produtoGuid"), LerTexto(m, "nome"), NuloSeVazio(LerTexto(m, "referencia")), LerTexto(m, "tamanho"),
                (int)LerInteiro(m, "pecas"), LerInteiro(m, "valorCentavos"));
        }).ToList();
        return new ResumoDiaNuvem(dia, LerInteiro(c, "faturamentoCentavos"), (int)LerInteiro(c, "numeroDeVendas"), (int)LerInteiro(c, "pecas"),
            (int)LerInteiro(c, "canceladas"), LerInteiro(c, "canceladasCentavos"), (int)LerInteiro(c, "trocas"), LerInteiro(c, "trocasCentavos"),
            formas, itens, LerTempo(c, "atualizadoEm"));
    }

    // ---------------------------------------------------------------- estoque
    public static JsonObject ParaCampos(EstoqueProdutoNuvem e)
    {
        var tam = new JsonObject();
        foreach (var t in e.PorTamanho.OrderBy(k => k.Key, StringComparer.Ordinal)) tam[ChaveTamanho(t.Key)] = Inteiro(t.Value);
        return new JsonObject
        {
            ["produtoGuid"] = Texto(e.ProdutoGuid), ["nome"] = Texto(e.Nome), ["referencia"] = Texto(e.Referencia ?? ""), ["categoria"] = Texto(e.Categoria ?? ""),
            ["custoCentavos"] = Inteiro(e.CustoCentavos), ["precoVarejoCentavos"] = Inteiro(e.PrecoVarejoCentavos),
            ["porTamanho"] = Mapa(tam), ["total"] = Inteiro(e.Total), ["atualizadoEm"] = Tempo(e.AtualizadoEmUtc)
        };
    }

    public static EstoqueProdutoNuvem EstoqueDeCampos(JsonObject c) =>
        new(LerTexto(c, "produtoGuid"), LerTexto(c, "nome"), NuloSeVazio(LerTexto(c, "referencia")), NuloSeVazio(LerTexto(c, "categoria")),
            LerInteiro(c, "custoCentavos"), LerInteiro(c, "precoVarejoCentavos"),
            LerMapa(c, "porTamanho")?.ToDictionary(k => DeChaveTamanho(k.Key), k => (int)ValorInteiro(k.Value!, k.Key)) ?? new Dictionary<string, int>(),
            (int)LerInteiro(c, "total"), LerTempo(c, "atualizadoEm"));

    // O Firestore não aceita nome de campo vazio: o "sem tamanho" (texto vazio) vai como "_sem".
    private static string ChaveTamanho(string t) => t.Length == 0 ? "_sem" : t;
    private static string DeChaveTamanho(string k) => k == "_sem" ? "" : k;

    // ---------------------------------------------------------------- lançamento
    public static JsonObject ParaCampos(LancamentoNuvem l) => new()
    {
        ["id"] = Texto(l.Id), ["data"] = Texto(l.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ["tipo"] = Texto(l.Tipo),
        ["categoria"] = Texto(l.Categoria), ["descricao"] = Texto(l.Descricao), ["valorCentavos"] = Inteiro(l.ValorCentavos),
        ["forma"] = Texto(l.Forma ?? ""), ["observacao"] = Texto(l.Observacao ?? ""),
        ["anexos"] = Lista(l.NomesDosAnexos.Select(n => (JsonNode)Texto(n)).ToArray()), ["atualizadoEm"] = Tempo(l.AtualizadoEmUtc)
    };

    public static LancamentoNuvem LancamentoDeCampos(JsonObject c) =>
        new(LerTexto(c, "id"), DateTime.ParseExact(LerTexto(c, "data"), "yyyy-MM-dd", CultureInfo.InvariantCulture), LerTexto(c, "tipo"),
            LerTexto(c, "categoria"), LerTexto(c, "descricao"), LerInteiro(c, "valorCentavos"), NuloSeVazio(LerTexto(c, "forma")),
            NuloSeVazio(LerTexto(c, "observacao")),
            LerLista(c, "anexos").Select(n => TextoDe(n, "anexos")).ToList(), LerTempo(c, "atualizadoEm"));

    // ---------------------------------------------------------------- preço por loja
    public static JsonObject ParaCampos(PrecoLojaNuvem p)
    {
        var o = new JsonObject
        {
            ["produtoGuid"] = Texto(p.ProdutoGuid), ["lojaId"] = Texto(p.LojaId), ["precoVarejoCentavos"] = Inteiro(p.PrecoVarejoCentavos),
            ["definidoPor"] = Texto(p.DefinidoPor), ["atualizadoEm"] = Tempo(p.AtualizadoEmUtc)
        };
        if (p.PrecoAtacadoCentavos is long a) o["precoAtacadoCentavos"] = Inteiro(a);
        return o;
    }

    public static PrecoLojaNuvem PrecoDeCampos(JsonObject c) =>
        new(LerTexto(c, "produtoGuid"), LerTexto(c, "lojaId"), LerInteiro(c, "precoVarejoCentavos"),
            c.ContainsKey("precoAtacadoCentavos") ? LerInteiro(c, "precoAtacadoCentavos") : null,
            LerTexto(c, "definidoPor"), LerTempo(c, "atualizadoEm"));

    // ---------------------------------------------------------------- valores do Firestore
    public static JsonObject Texto(string valor) => new() { ["stringValue"] = valor };
    public static JsonObject Inteiro(long valor) => new() { ["integerValue"] = valor.ToString(CultureInfo.InvariantCulture) }; // a API pede o inteiro como texto
    public static JsonObject Tempo(DateTime utc) => new()
    {
        ["timestampValue"] = DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture)
    };
    public static JsonObject Mapa(JsonObject campos) => new() { ["mapValue"] = new JsonObject { ["fields"] = campos } };
    public static JsonObject Lista(JsonNode[] itens) => new() { ["arrayValue"] = new JsonObject { ["values"] = new JsonArray(itens) } };

    private static string LerTexto(JsonObject c, string campo) =>
        c[campo] is null ? "" : TextoDe(c[campo]!, campo);

    private static string TextoDe(JsonNode n, string campo) =>
        (n as JsonObject)?["stringValue"]?.GetValue<string>() ?? throw new FormatException($"Campo '{campo}' não é texto.");

    private static long LerInteiro(JsonObject c, string campo) =>
        c[campo] is null ? 0 : ValorInteiro(c[campo]!, campo);

    private static long ValorInteiro(JsonNode n, string campo)
    {
        var o = n as JsonObject;
        if (o?["integerValue"] is JsonNode i && long.TryParse(i.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        throw new FormatException($"Campo '{campo}' não é inteiro.");
    }

    private static DateTime LerTempo(JsonObject c, string campo)
    {
        if (c[campo] is null) return DateTime.MinValue;
        var s = (c[campo] as JsonObject)?["timestampValue"]?.GetValue<string>() ?? throw new FormatException($"Campo '{campo}' não é data e hora.");
        return DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    }

    private static JsonObject? LerMapa(JsonObject c, string campo) =>
        c[campo] is null ? null : ConteudoMapa(c[campo]!, campo);

    private static JsonObject ConteudoMapa(JsonNode n, string campo) =>
        (n as JsonObject)?["mapValue"]?["fields"] as JsonObject ?? ((n as JsonObject)?["mapValue"] is JsonObject ? new JsonObject() : throw new FormatException($"Campo '{campo}' não é mapa."));

    private static List<JsonNode> LerLista(JsonObject c, string campo)
    {
        if (c[campo] is null) return new List<JsonNode>();
        var a = (c[campo] as JsonObject)?["arrayValue"] as JsonObject ?? throw new FormatException($"Campo '{campo}' não é lista.");
        return a["values"] is JsonArray v ? v.Where(x => x is not null).Select(x => x!).ToList() : new List<JsonNode>();
    }

    private static string? NuloSeVazio(string s) => s.Length == 0 ? null : s;
}
