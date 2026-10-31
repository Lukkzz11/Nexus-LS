using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Models;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Fonte do ERP falsa: guarda documentos por coleção e responde como o Firestore (filtro por data, ordem, limite).</summary>
internal sealed class FonteErpFalsa : IFonteErp
{
    public Dictionary<string, Dictionary<string, JsonObject>> Docs { get; } = new();
    public List<(string Colecao, string Campo, DateTime? Desde, int Lidos)> Consultas { get; } = new();
    public int Leituras => Consultas.Sum(c => c.Lidos);

    public void Por(string colecao, string id, string json) =>
        (Docs.TryGetValue(colecao, out var d) ? d : Docs[colecao] = new())[id] = JsonNode.Parse(json)!.AsObject();

    public Task<IReadOnlyList<DocumentoErp>> LerAsync(string colecao, string campoData, DateTime? aPartirDe, int limite, CancellationToken ct = default)
    {
        IEnumerable<KeyValuePair<string, JsonObject>> q = Docs.TryGetValue(colecao, out var d) ? d : Enumerable.Empty<KeyValuePair<string, JsonObject>>();
        if (aPartirDe is { } desde) // como no Firestore: sem o campo, o documento não entra
            q = q.Where(p => Data(p.Value, campoData) is { } x && x >= desde).OrderBy(p => Data(p.Value, campoData)).Take(limite);
        var lista = q.Select(p => new DocumentoErp(p.Key, (JsonObject)JsonNode.Parse(p.Value.ToJsonString())!)).ToList();
        Consultas.Add((colecao, campoData, aPartirDe, lista.Count));
        return Task.FromResult<IReadOnlyList<DocumentoErp>>(lista);
    }

    private static DateTime? Data(JsonObject o, string campo) =>
        o[campo]?.GetValue<string>() is { } s ? DateTimeOffset.Parse(s, CultureInfo.InvariantCulture).UtcDateTime : null;
}

public sealed class ImportadorErpTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid");
    private readonly FonteErpFalsa _fonte = new();
    private DateTime _agora = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _banco.Dispose();

    private ImportadorErp Importador() => new(_banco.Fabrica, _fonte, () => _agora);

    private void ProdutoNoErp(string id, string nome, string atualizado, string extra = "") =>
        _fonte.Por("produtos", id, $$"""
            {"nome":"{{nome}}","categoria":"Camisetas","referencia":"R-{{id}}","precoVarejo":59.9,"precoAtacado":45,"ativo":true,
             "gradeTamanhos":{"P":3,"M":5},"estoqueTotal":8,"atualizadoEm":"{{atualizado}}" {{extra}}}
            """);

    [Fact]
    public async Task Primeira_importacao_traz_produtos_com_grade_clientes_turno_e_venda_sem_mexer_no_estoque()
    {
        ProdutoNoErp("p1", "Camiseta Surf", "2026-10-30T10:00:00Z");
        _fonte.Por("clientes", "c1", """{"nome":"Ana","documento":"123","telefone":"8199","limiteCredito":500,"atualizadoEm":"2026-10-30T09:00:00Z"}""");
        _fonte.Por("caixa_turnos", "t1", """{"abertoEm":"2026-10-30T11:00:00Z","trocoInicial":100,"operador":"ana","status":"fechado","fechadoEm":"2026-10-30T20:00:00Z","criadoEm":"2026-10-30T11:00:00Z"}""");
        _fonte.Por("vendas", "v1", """
            {"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"p1","nome":"Camiseta Surf","tamanho":"M","quantidade":2,"precoUnitario":59.9,"subtotal":119.8}],
             "subtotal":119.8,"desconto":0,"total":119.8,"formaPagamento":"prazo","infoPagamento":{"parcelas":1},"tipoVenda":"varejo",
             "cliente":{"id":"c1","nome":"Ana"},"operador":"ana","criadoEm":"2026-10-30T14:00:00Z"}
            """);

        var r = await Importador().ImportarAsync();

        Assert.Equal(4, r.Novos);
        await using var db = _banco.CriarContexto();
        var p = await db.Produtos.Include(x => x.Tamanhos).SingleAsync();
        Assert.Equal(5990, p.PrecoVendaCentavos);
        Assert.Equal(4500, p.PrecoAtacadoCentavos);
        Assert.Equal(8, p.EstoqueAtual);
        Assert.Equal(5, p.Tamanhos.Single(t => t.Tamanho == "M").Estoque); // a venda importada NÃO baixou o estoque
        Assert.Contains(p.Tamanhos, t => t.EhSemTamanho);

        var cliente = await db.Clientes.SingleAsync();
        Assert.Equal(50000, cliente.LimiteCreditoCentavos);

        var turno = await db.TurnosCaixa.SingleAsync();
        Assert.Equal(10000, turno.TrocoInicialCentavos);
        Assert.NotNull(turno.DataFechamento);

        var venda = await db.Vendas.Include(v => v.Itens).SingleAsync();
        Assert.Equal(11980, venda.TotalCentavos);
        Assert.Equal(FormaPagamento.APrazo, venda.FormaPagamento);
        Assert.Equal(cliente.Id, venda.ClienteId);
        Assert.Equal(p.Id, venda.Itens.Single().ProdutoId);
        Assert.Equal("M", venda.Itens.Single().Tamanho);
    }

    [Fact]
    public async Task Importar_de_novo_sem_mudanca_le_quase_nada_e_nao_duplica()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");
        ProdutoNoErp("p2", "Boné", "2026-10-30T10:01:00Z");
        var imp = Importador();
        await imp.ImportarAsync();
        _fonte.Consultas.Clear();
        _agora = _agora.AddMinutes(3);

        var r = await imp.ImportarAsync();

        Assert.Equal(0, r.Novos);
        Assert.Equal(0, r.Atualizados);
        Assert.Equal(2, await _banco.CriarContexto().Produtos.CountAsync());
        Assert.All(_fonte.Consultas, c => Assert.NotNull(c.Desde)); // nunca mais a coleção inteira
        Assert.Equal(0, _fonte.Leituras); // o cursor ficou em "agora - folga", depois dos documentos
    }

    [Fact]
    public async Task Primeira_leitura_nao_le_duas_vezes_a_mesma_colecao()
    {
        _fonte.Por("vendas", "v1", """{"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"x","nome":"X","quantidade":1,"precoUnitario":10,"subtotal":10}],"total":10,"formaPagamento":"pix","criadoEm":"2026-10-30T14:00:00Z"}""");

        await Importador().ImportarAsync();

        Assert.Equal(1, _fonte.Consultas.Count(c => c.Colecao == "vendas" && c.Desde is null)); // criadoEm lê tudo; atualizadoEm só marca o ponto
        Assert.Equal(1, _fonte.Leituras - _fonte.Consultas.Where(c => c.Colecao != "vendas").Sum(c => c.Lidos));
    }

    [Fact]
    public async Task Produto_alterado_no_ERP_atualiza_o_mesmo_registro()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");
        var imp = Importador();
        await imp.ImportarAsync();
        _agora = _agora.AddHours(1);
        _fonte.Por("produtos", "p1", """{"nome":"Camiseta Nova","precoVarejo":70,"gradeTamanhos":{"P":1,"M":9},"atualizadoEm":"2026-10-31T12:30:00Z"}""");

        var r = await imp.ImportarAsync();

        Assert.Equal(1, r.Atualizados);
        await using var db = _banco.CriarContexto();
        var p = await db.Produtos.Include(x => x.Tamanhos).SingleAsync();
        Assert.Equal("Camiseta Nova", p.Nome);
        Assert.Equal(7000, p.PrecoVendaCentavos);
        Assert.Equal(10, p.EstoqueAtual);
    }

    [Fact]
    public async Task Venda_cancelada_depois_no_ERP_vira_cancelada_aqui()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");
        string Venda(string extra) => $$"""
            {"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"p1","nome":"Camiseta","quantidade":1,"precoUnitario":10,"subtotal":10}],
             "total":10,"formaPagamento":"dinheiro","criadoEm":"2026-10-30T14:00:00Z" {{extra}}}
            """;
        _fonte.Por("vendas", "v1", Venda(""));
        var imp = Importador();
        await imp.ImportarAsync();
        _agora = _agora.AddHours(2);
        _fonte.Por("vendas", "v1", Venda(",\"status\":\"cancelada\",\"motivoCancelamento\":\"erro de preço\",\"canceladoPor\":\"ana\",\"estoqueRestaurado\":true,\"atualizadoEm\":\"2026-10-31T13:00:00Z\""));

        await imp.ImportarAsync();

        await using var db = _banco.CriarContexto();
        var v = await db.Vendas.SingleAsync(); // a mesma venda, não duas
        Assert.True(v.Cancelada);
        Assert.Equal("erro de preço", v.MotivoCancelamento);
        Assert.True(v.EstoqueDevolvido);
    }

    [Fact]
    public async Task Venda_de_produto_que_nao_existe_mais_cria_produto_inativo_para_o_historico()
    {
        _fonte.Por("vendas", "v1", """{"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"sumiu","nome":"Peça antiga","quantidade":2,"precoUnitario":10,"subtotal":20}],"total":20,"formaPagamento":"pix","criadoEm":"2026-10-30T14:00:00Z"}""");

        var r = await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        var p = await db.Produtos.SingleAsync();
        Assert.False(p.Ativo);
        Assert.Equal("Peça antiga", p.Nome);
        Assert.Equal(p.Id, (await db.ItensVenda.SingleAsync()).ProdutoId);
        Assert.Contains(r.Avisos, a => a.Contains("sumiu"));
    }

    [Fact]
    public async Task Codigo_de_barras_repetido_importa_sem_codigo_e_avisa()
    {
        ProdutoNoErp("p1", "A", "2026-10-30T10:00:00Z", ",\"codigoBarras\":\"789\"");
        ProdutoNoErp("p2", "B", "2026-10-30T10:01:00Z", ",\"codigoBarras\":\"789\"");

        var r = await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        Assert.Equal(2, await db.Produtos.CountAsync());
        Assert.Equal(1, await db.Produtos.CountAsync(p => p.CodigoBarras == "789"));
        Assert.Contains(r.Avisos, a => a.Contains("789"));
    }

    [Fact]
    public async Task Forma_de_pagamento_desconhecida_ignora_a_venda_e_avisa()
    {
        _fonte.Por("vendas", "v1", """{"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"x","nome":"X","quantidade":1,"precoUnitario":10,"subtotal":10}],"total":10,"formaPagamento":"misto","criadoEm":"2026-10-30T14:00:00Z"}""");

        var r = await Importador().ImportarAsync();

        Assert.Equal(0, await _banco.CriarContexto().Vendas.CountAsync());
        Assert.Equal(1, r.Colecoes.Single(c => c.Colecao == "vendas").Ignorados);
    }

    [Fact]
    public async Task O_que_foi_importado_entra_na_fila_de_envio_da_nuvem()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");

        await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        Assert.Equal(1, await db.FilaSincronizacao.CountAsync(f => f.Entidade == NomesEntidade.Produto));
    }

    // ---- leitura REST ----

    private sealed class ManipuladorFalso : HttpMessageHandler
    {
        public string? Url, Corpo, Autorizacao;
        public string Resposta = "[]";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            Url = req.RequestUri!.ToString();
            Corpo = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
            Autorizacao = req.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Resposta, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class TokenFalso : IProvedorToken
    {
        public Task<string> ObterAsync(CancellationToken ct = default) => Task.FromResult("tok");
        public void Invalidar() { }
    }

    [Fact]
    public async Task Leitura_REST_pede_so_o_que_mudou_e_decodifica_os_valores_do_Firestore()
    {
        var http = new ManipuladorFalso
        {
            Resposta = """
            [{"document":{"name":"projects/p/databases/(default)/documents/empresas/e/produtos/abc","fields":{
               "nome":{"stringValue":"Camiseta"},"precoVarejo":{"doubleValue":59.9},"estoqueTotal":{"integerValue":"8"},"ativo":{"booleanValue":true},
               "atualizadoEm":{"timestampValue":"2026-10-30T10:00:00.5Z"},
               "gradeTamanhos":{"mapValue":{"fields":{"M":{"integerValue":"5"}}}},
               "cores":{"arrayValue":{"values":[{"stringValue":"azul"}]}}}}},
             {"readTime":"2026-10-31T00:00:00Z"}]
            """
        };
        var fonte = new FirestoreErpFonte(new HttpClient(http), new ConfiguracaoErp { ProjectId = "erp-projeto", ApiKey = "k", EmpresaId = "e" }, new TokenFalso());

        var docs = await fonte.LerAsync("produtos", "atualizadoEm", new DateTime(2026, 10, 30, 0, 0, 0, DateTimeKind.Utc), 300);

        var d = Assert.Single(docs);
        Assert.Equal("abc", d.Id);
        Assert.Equal(5L, (long)d.Campos["gradeTamanhos"]!["M"]!);
        Assert.Equal(8, (long)d.Campos["estoqueTotal"]!);
        Assert.Equal("azul", (string)d.Campos["cores"]![0]!);
        Assert.Contains("/documents/empresas/e:runQuery", http.Url);
        Assert.Equal("tok", http.Autorizacao);
        var consulta = JsonNode.Parse(http.Corpo!)!["structuredQuery"]!;
        Assert.Equal("GREATER_THAN_OR_EQUAL", (string)consulta["where"]!["fieldFilter"]!["op"]!);
        Assert.Equal(300, (int)consulta["limit"]!);
        Assert.DoesNotContain("\"update\"", http.Corpo); // só consulta, nunca grava
    }

    [Fact]
    public async Task Primeira_leitura_REST_nao_manda_filtro_nem_limite()
    {
        var http = new ManipuladorFalso();
        var fonte = new FirestoreErpFonte(new HttpClient(http), new ConfiguracaoErp { ProjectId = "erp-projeto", ApiKey = "k", EmpresaId = "e" }, new TokenFalso());

        await fonte.LerAsync("clientes", "atualizadoEm", null, 300);

        var consulta = JsonNode.Parse(http.Corpo!)!["structuredQuery"]!.AsObject();
        Assert.False(consulta.ContainsKey("where"));
        Assert.False(consulta.ContainsKey("limit"));
    }
}
