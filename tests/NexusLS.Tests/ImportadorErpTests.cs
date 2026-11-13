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
    public async Task Sobra_do_ERP_vira_a_marca_Sobra_e_continua_estoque_normal()
    {
        ProdutoNoErp("p1", "Retalho", "2026-10-30T10:00:00Z", ",\"sobra\":true");
        ProdutoNoErp("p2", "Camiseta", "2026-10-30T10:00:00Z");

        await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        var produtos = await db.Produtos.ToListAsync();
        Assert.True(produtos.Single(x => x.Nome == "Retalho").Sobra);
        Assert.False(produtos.Single(x => x.Nome == "Camiseta").Sobra);
        Assert.Equal(8, produtos.Single(x => x.Nome == "Retalho").EstoqueAtual);

        // reimportar sem o campo não desmarca; com false, desmarca
        _agora = _agora.AddHours(1);
        ProdutoNoErp("p1", "Retalho", "2026-10-31T10:00:00Z");
        await Importador().ImportarAsync();
        await using (var db2 = _banco.CriarContexto()) Assert.True((await db2.Produtos.SingleAsync(x => x.Nome == "Retalho")).Sobra);
        _agora = _agora.AddHours(1);
        ProdutoNoErp("p1", "Retalho", "2026-11-01T10:00:00Z", ",\"sobra\":false");
        await Importador().ImportarAsync();
        await using var db3 = _banco.CriarContexto();
        Assert.False((await db3.Produtos.SingleAsync(x => x.Nome == "Retalho")).Sobra);
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
    public async Task Importacao_de_produto_registra_movimentacao_por_tamanho_e_zera_sem_tamanho_antigo()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");
        var imp = Importador();
        await imp.ImportarAsync();
        int id;
        await using (var db0 = _banco.CriarContexto())
        {
            var p0 = await db0.Produtos.Include(x => x.Tamanhos).SingleAsync();
            id = p0.Id;
            var movs0 = await db0.Movimentacoes.Where(m => m.ProdutoId == id).ToListAsync();
            Assert.Equal(8, movs0.Sum(m => m.Quantidade));
            Assert.All(movs0, m => { Assert.Equal(TipoMovimentacao.Entrada, m.Tipo); Assert.False(string.IsNullOrEmpty(m.Tamanho)); Assert.False(string.IsNullOrEmpty(m.Motivo)); });
            p0.Tamanhos.Single(t => t.EhSemTamanho).Estoque = 4; // estoque antigo sem tamanho
            p0.EstoqueAtual = 12;
            await db0.SaveChangesAsync();
        }
        _agora = _agora.AddHours(1);
        _fonte.Por("produtos", "p1", """{"nome":"Camiseta","precoVarejo":70,"gradeTamanhos":{"P":1,"M":9},"atualizadoEm":"2026-10-31T12:30:00Z"}""");

        await imp.ImportarAsync();

        await using var db = _banco.CriarContexto();
        var p = await db.Produtos.Include(x => x.Tamanhos).SingleAsync();
        Assert.Equal(10, p.EstoqueAtual);
        Assert.Equal(0, p.Tamanhos.Single(t => t.EhSemTamanho).Estoque);
        Assert.Equal(p.EstoqueAtual, p.Tamanhos.Sum(t => t.Estoque));
        var movs = await db.Movimentacoes.Where(m => m.ProdutoId == id).ToListAsync();
        Assert.Equal(p.EstoqueAtual, movs.Sum(m => m.Quantidade) + 4); // as movimentações explicam o saldo (o 4 foi editado direto no banco do teste)
        Assert.Contains(movs, m => m.Tipo == TipoMovimentacao.Correcao && m.Tamanho == Tamanhos.SemTamanho && m.Quantidade == -4);
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

    private void VendaPrazoNoErp()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");
        _fonte.Por("clientes", "c1", """{"nome":"Ana","atualizadoEm":"2026-10-30T09:00:00Z"}""");
        _fonte.Por("vendas", "v1", """
            {"dataHoraVenda":"2026-10-30T14:00:00Z","itens":[{"id":"p1","nome":"Camiseta","quantidade":1,"precoUnitario":100,"subtotal":100}],
             "total":100,"formaPagamento":"prazo","cliente":{"id":"c1","nome":"Ana"},"criadoEm":"2026-10-30T14:00:00Z"}
            """);
    }

    private static string Titulo(string pagamentos, string atualizado = "2026-10-30T15:00:00Z") => $$"""
        {"origemVendaId":"v1","cliente":{"id":"c1"},"valorOriginal":100,"status":"pago_parcial","criadoEm":"2026-10-30T14:00:00Z","atualizadoEm":"{{atualizado}}",
         "historicoPagamentos":[{{pagamentos}}]}
        """;

    [Fact]
    public async Task Pagamentos_de_divida_viram_pagamentos_do_cliente_e_o_estornado_fica_de_fora()
    {
        VendaPrazoNoErp();
        _fonte.Por("contas_receber", "t1", Titulo("""
            {"id":"g1:t1","data":"2026-10-30","hora":"16:30","valor":30,"forma":"pix","operador":"ana"},
            {"id":"g2:t1","data":"2026-10-30","hora":"17:00","valor":20,"forma":"dinheiro","estornado":true}
            """));

        await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        var pg = await db.PagamentosCliente.SingleAsync();
        Assert.Equal(3000, pg.ValorCentavos);
        Assert.Equal(FormaPagamento.Pix, pg.Forma);
        Assert.Equal(new DateTime(2026, 10, 30, 16, 30, 0), pg.DataHora);
        var conta = await new NexusLS.Core.Services.ClienteService(_banco.Fabrica).ContaAsync(pg.ClienteId);
        Assert.Equal(7000, conta.SaldoDevedorCentavos); // 100 de venda a prazo - 30 pagos
    }

    [Fact]
    public async Task Estorno_feito_depois_no_ERP_remove_o_pagamento_importado()
    {
        VendaPrazoNoErp();
        string Pg(string extra) => "{\"id\":\"g1:t1\",\"data\":\"2026-10-30\",\"valor\":30,\"forma\":\"pix\"" + extra + "}";
        _fonte.Por("contas_receber", "t1", Titulo(Pg("")));
        var imp = Importador();
        await imp.ImportarAsync();
        _agora = _agora.AddHours(2);
        _fonte.Por("contas_receber", "t1", Titulo(Pg(",\"estornado\":true"), "2026-10-31T13:00:00Z"));

        var r = await imp.ImportarAsync();

        Assert.Equal(0, await _banco.CriarContexto().PagamentosCliente.CountAsync());
        Assert.Contains(r.Avisos, a => a.Contains("estornado"));
    }

    [Fact]
    public async Task Titulo_sem_venda_de_origem_fica_de_fora_com_aviso()
    {
        _fonte.Por("clientes", "c1", """{"nome":"Ana","atualizadoEm":"2026-10-30T09:00:00Z"}""");
        _fonte.Por("contas_receber", "t9", """{"origemVendaId":"","cliente":{"id":"c1"},"valorOriginal":50,"criadoEm":"2026-10-30T14:00:00Z","historicoPagamentos":[{"id":"x","valor":10,"forma":"pix"}]}""");

        var r = await Importador().ImportarAsync();

        Assert.Equal(0, await _banco.CriarContexto().PagamentosCliente.CountAsync());
        Assert.Contains(r.Avisos, a => a.Contains("t9"));
    }

    [Fact]
    public async Task Movimentos_do_caixa_e_gastos_do_erp_viram_movimentos_do_turno_e_lancamentos_sem_duplicar()
    {
        _fonte.Por("caixa_turnos", "t1", """{"abertoEm":"2026-10-30T11:00:00Z","trocoInicial":100,"operador":"ana","status":"fechado","fechadoEm":"2026-10-30T20:00:00Z","criadoEm":"2026-10-30T11:00:00Z"}""");
        _fonte.Por("transacoes_financeiras", "x1", """{"tipo":"saida","natureza":"retirada","interno":true,"origem":"caixa","afetaGaveta":true,"turnoId":"t1","valor":200,"descricao":"Levou ao banco","data":"2026-10-30","hora":"15:30","status":"concluido","criadoEm":"2026-10-30T15:30:00Z"}""");
        _fonte.Por("transacoes_financeiras", "x2", """{"tipo":"saida","natureza":"retirada","interno":true,"origem":"caixa","afetaGaveta":true,"turnoId":"sumiu","valor":50,"data":"2026-10-30","status":"concluido","criadoEm":"2026-10-30T16:00:00Z"}""");
        _fonte.Por("transacoes_financeiras", "x3", """{"tipo":"saida","categoria":"Aluguel","descricao":"Aluguel da loja","valor":1500,"formaPagamento":"pix","data":"2026-10-05","status":"concluido","fornecedorOuCliente":"Imobiliária","criadoEm":"2026-10-05T10:00:00Z"}""");
        _fonte.Por("transacoes_financeiras", "x4", """{"tipo":"entrada","origem":"caixa","natureza":"entrada_avulsa","afetaGaveta":false,"valor":80,"descricao":"Conserto","formaPagamento":"pix","data":"2026-10-06","status":"cancelado","criadoEm":"2026-10-06T10:00:00Z"}""");

        var r1 = await Importador().ImportarAsync();
        await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        var mov = await db.MovimentosCaixa.SingleAsync();
        Assert.Equal(TipoMovimentoCaixa.Sangria, mov.Tipo);
        Assert.Equal(20000, mov.ValorCentavos);
        Assert.Equal((await db.TurnosCaixa.SingleAsync()).Id, mov.TurnoId);
        Assert.Contains(r1.Avisos, a => a.Contains("x2"));          // sem turno importado: fora, com aviso

        var lancs = await db.Lancamentos.OrderBy(l => l.Id).ToListAsync();
        Assert.Equal(2, lancs.Count);
        Assert.Equal(TipoLancamento.Despesa, lancs[0].Tipo);
        Assert.Equal(150000, lancs[0].ValorCentavos);
        Assert.Equal(FormaPagamento.Pix, lancs[0].FormaPagamento);
        Assert.NotNull(lancs[0].PagoEm);
        Assert.Equal("Imobiliária", lancs[0].Contraparte);
        Assert.True(lancs[1].Cancelado);
        Assert.Equal(TipoLancamento.Receita, lancs[1].Tipo);
    }

    [Fact]
    public async Task Fabrica_do_erp_vira_materia_ordens_com_grade_e_perdas_ligadas_e_importar_de_novo_nao_duplica()
    {
        _fonte.Por("materia_prima", "m1", """{"nome":"Malha PV","tipo":"Tecido","unidade":"kg","estoqueAtual":145.5,"estoqueMinimo":30,"custoUnitario":42,"fornecedor":"Malharia","atualizadoEm":"2026-10-30T09:00:00Z"}""");
        _fonte.Por("ordens_producao", "o1", """
            {"codigoOP":"OP-2026-084","modelo":"Camiseta Silk","referencia":"CAM-001","status":"costura","previsaoTermino":"2026-11-15",
             "gradePlanejada":{"P":30,"M":60},"custoEstimadoPeca":22.5,"responsavel":"Raimundo","criadoEm":"2026-10-30T10:00:00Z"}
            """);
        _fonte.Por("ordens_producao", "o2", """{"codigoOP":"OP-2026-084","modelo":"Bermuda","status":"concluido","gradePlanejada":{"38":10},"criadoEm":"2026-10-30T11:00:00Z"}""");
        _fonte.Por("perdas_producao", "pe1", """{"opCodigo":"OP-2026-084","motivo":"defeito_tecido","materialNome":"Malha PV","quantidade":2.5,"unidade":"kg","custoEstimadoPerda":105,"criadoEm":"2026-10-30T12:00:00Z"}""");

        await Importador().ImportarAsync();
        await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        var materia = await db.MateriasPrimas.SingleAsync();
        Assert.Equal(145.5m, materia.EstoqueAtual);
        Assert.Equal(4200, materia.CustoUnitarioCentavos);

        var ordens = await db.OrdensProducao.Include(o => o.Grade).OrderBy(o => o.Id).ToListAsync();
        Assert.Equal(2, ordens.Count);
        Assert.Equal(StatusOrdemProducao.Costura, ordens[0].Status);
        Assert.Equal(90, ordens[0].TotalPlanejado);
        Assert.Equal(StatusOrdemProducao.Concluida, ordens[1].Status);
        Assert.NotEqual(ordens[0].Codigo, ordens[1].Codigo);        // código repetido no ERP ganha sufixo

        var perda = await db.PerdasProducao.SingleAsync();
        Assert.Equal(MotivoPerda.DefeitoNoTecido, perda.Motivo);
        Assert.Equal(ordens[0].Id, perda.OrdemProducaoId);
        Assert.Equal(materia.Id, perda.MateriaPrimaId);
        Assert.False(perda.BaixouEstoque);
        Assert.Equal(10500, perda.CustoEstimadoCentavos);
        Assert.Equal(145.5m, (await db.MateriasPrimas.SingleAsync()).EstoqueAtual);   // o saldo não baixa de novo
    }

    [Fact]
    public async Task Pedido_do_catalogo_vira_pedido_com_itens_cliente_e_produto_ligados()
    {
        ProdutoNoErp("p1", "Camiseta", "2026-10-30T10:00:00Z");
        _fonte.Por("clientes", "c1", """{"nome":"Ana","telefone":"8199","atualizadoEm":"2026-10-30T09:00:00Z"}""");
        _fonte.Por("pedidos", "o1", """
            {"numeroPedido":"PED-0001","origem":"catalogo","status":"novo","cliente":{"id":"c1","nome":"Ana","telefone":"8199"},
             "itens":[{"id":"p1","nome":"Camiseta","tamanho":"M","quantidade":2,"precoUnitario":50}],"total":100,"observacoes":"entregar sexta",
             "criadoEm":"2026-10-30T15:00:00Z"}
            """);

        await Importador().ImportarAsync();

        await using var db = _banco.CriarContexto();
        var ped = await db.Pedidos.Include(p => p.Itens).Include(p => p.Historico).SingleAsync();
        Assert.Equal("PED-0001", ped.Numero);
        Assert.Equal(OrigemPedido.Catalogo, ped.Origem);
        Assert.Equal(StatusPedido.Novo, ped.Status);
        Assert.Equal(10000, ped.TotalCentavos);
        Assert.Equal((await db.Clientes.SingleAsync()).Id, ped.ClienteId);
        var item = Assert.Single(ped.Itens);
        Assert.Equal((await db.Produtos.SingleAsync()).Id, item.ProdutoId);
        Assert.Equal(5000, item.PrecoUnitarioCentavos);
        Assert.Single(ped.Historico);
    }

    [Fact]
    public async Task Mudanca_de_etapa_no_ERP_atualiza_o_mesmo_pedido_e_registra_no_historico()
    {
        string Pedido(string status, string extra) => "{\"numeroPedido\":\"PED-7\",\"origem\":\"whatsapp\",\"status\":\"" + status + "\",\"cliente\":{\"nome\":\"Bia\"},\"itens\":[{\"nome\":\"Livre\",\"quantidade\":1,\"precoUnitario\":10}],\"total\":10,\"criadoEm\":\"2026-10-30T15:00:00Z\"" + extra + "}";
        _fonte.Por("pedidos", "o1", Pedido("novo", ""));
        var imp = Importador();
        await imp.ImportarAsync();
        _agora = _agora.AddHours(2);
        _fonte.Por("pedidos", "o1", Pedido("separado", ",\"atualizadoEm\":\"2026-10-31T13:00:00Z\""));

        await imp.ImportarAsync();

        await using var db = _banco.CriarContexto();
        var ped = await db.Pedidos.Include(p => p.Historico).SingleAsync(); // o mesmo pedido, não dois
        Assert.Equal(StatusPedido.Separado, ped.Status);
        Assert.Equal(2, ped.Historico.Count);
        Assert.Null((await db.Pedidos.Include(p => p.Itens).SingleAsync()).Itens.Single().ProdutoId); // item livre
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

public sealed class ImportadorErpSemDuplicarTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid");
    private readonly FonteErpFalsa _fonte = new();
    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Produto_que_ja_existe_aqui_e_ligado_ao_do_ERP_em_vez_de_duplicado()
    {
        await using (var db = _banco.CriarContexto())
        {
            db.Produtos.Add(new Produto { Nome = "Camiseta Surf", Referencia = "R-1", CodigoBarras = "789", PrecoVendaCentavos = 1000, Tamanhos = { new ProdutoTamanho { Tamanho = "M", Estoque = 1 } } });
            db.Produtos.Add(new Produto { Nome = "Boné", PrecoVendaCentavos = 2000, Tamanhos = { new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho, Estoque = 1 } } });
            db.Produtos.Add(new Produto { Nome = "Chinelo", Referencia = "A", Tamanhos = { new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho, Estoque = 0 } } });
            await db.SaveChangesAsync();
        }
        _fonte.Por("produtos", "p1", """{"nome":"CAMISETA SURF NOVA","codigoBarras":"789","precoVarejo":59.9,"precoAtacado":45,"gradeTamanhos":{"M":5},"atualizadoEm":"2026-10-30T10:00:00Z"}""");
        _fonte.Por("produtos", "p2", """{"nome":"boné","referencia":"B-2","precoVarejo":30,"precoAtacado":20,"estoqueTotal":4,"atualizadoEm":"2026-10-30T10:00:00Z"}""");
        _fonte.Por("produtos", "p3", """{"nome":"Chinelo","referencia":"B","precoVarejo":10,"estoqueTotal":1,"atualizadoEm":"2026-10-30T10:00:00Z"}""");

        await new ImportadorErp(_banco.Fabrica, _fonte, () => new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc)).ImportarAsync();

        await using var d = _banco.CriarContexto();
        Assert.Equal(4, await d.Produtos.CountAsync());                       // camiseta e boné ligados; chinelo com outra referência é outro produto
        var camiseta = await d.Produtos.SingleAsync(x => x.CodigoBarras == "789");
        Assert.Equal(5990, camiseta.PrecoVendaCentavos);
        Assert.Equal(4500, camiseta.PrecoAtacadoCentavos);
        Assert.Equal(2000, (await d.Produtos.SingleAsync(x => x.Nome == "boné")).PrecoAtacadoCentavos);
    }
}
