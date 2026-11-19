using System.Text.Json.Nodes;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Nuvem;

namespace NexusLS.Tests.Gestao;

public class PacoteLojaTests
{
    private static readonly DateTime AgoraUtc = new(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime Dia = new(2026, 11, 18);

    private static readonly Produto Camiseta = new()
    {
        Id = 1, Guid = Guid.Parse("11111111-1111-1111-1111-111111111111"), Nome = "Camiseta", Referencia = "CAM1", Categoria = "Roupas",
        PrecoCustoCentavos = 2000, PrecoVendaCentavos = 5000, EstoqueAtual = 7,
        Tamanhos = { new ProdutoTamanho { Tamanho = "M", Estoque = 4 }, new ProdutoTamanho { Tamanho = "G", Estoque = 3 } }
    };

    private static readonly Produto Bone = new()
    {
        Id = 2, Guid = Guid.Parse("22222222-2222-2222-2222-222222222222"), Nome = "Boné", PrecoVendaCentavos = 4000, EstoqueAtual = 2,
        Tamanhos = { new ProdutoTamanho { Tamanho = Tamanhos.SemTamanho, Estoque = 2 } }
    };

    private static Venda V(int id, int hora, long total, FormaPagamento forma, bool cancelada = false, long vale = 0, params ItemVenda[] itens) => new()
    {
        Id = id, DataHora = Dia.AddHours(hora), TotalCentavos = total, SubtotalCentavos = total, FormaPagamento = forma, Cancelada = cancelada,
        ValeTrocaCentavos = vale, Itens = itens.ToList()
    };

    private static ItemVenda I(Produto p, string tam, int qtd, long unit) => new()
    {
        ProdutoId = p.Id, NomeProduto = p.Nome, Tamanho = tam, Quantidade = qtd, PrecoUnitarioCentavos = unit, TotalCentavos = qtd * unit
    };

    private static ResumoDiaNuvem ResumoDeExemplo() => MontadorPacoteLoja.ResumoDoDia(Dia,
        new[]
        {
            V(1, 9, 10000, FormaPagamento.Dinheiro, itens: new[] { I(Camiseta, "M", 2, 5000) }),
            V(2, 10, 9000, FormaPagamento.Pix, vale: 1000, itens: new[] { I(Camiseta, "G", 1, 5000), I(Bone, Tamanhos.SemTamanho, 1, 4000) }),
            V(3, 11, 7000, FormaPagamento.CartaoCredito, cancelada: true, itens: new[] { I(Camiseta, "M", 1, 7000) }),
            V(4, 12, 500, FormaPagamento.Dinheiro, itens: new[] { I(Bone, Tamanhos.SemTamanho, 1, 500) }),
            new Venda { Id = 9, DataHora = Dia.AddDays(-1), TotalCentavos = 99999, FormaPagamento = FormaPagamento.Pix }   // outro dia
        },
        new[] { Camiseta, Bone },
        new[] { new Devolucao { DataHora = Dia.AddHours(14), ValorCentavos = 2500 }, new Devolucao { DataHora = Dia, ValorCentavos = 1, Cancelada = true } },
        AgoraUtc);

    [Fact]
    public void Resumo_do_dia_soma_so_o_dia_e_separa_canceladas_e_trocas()
    {
        var r = ResumoDeExemplo();
        Assert.Equal("2026-11-18", r.Id);
        Assert.Equal(19500, r.FaturamentoCentavos);
        Assert.Equal(3, r.NumeroDeVendas);
        Assert.Equal(5, r.Pecas);
        Assert.Equal((1, 7000L), (r.Canceladas, r.CanceladasCentavos));
        Assert.Equal((1, 2500L), (r.Trocas, r.TrocasCentavos));
        Assert.Equal(10500, r.PorFormaPagamento["Dinheiro"]);
        Assert.Equal(8000, r.PorFormaPagamento["Pix"]);
        Assert.Equal(1000, r.PorFormaPagamento["ValeTroca"]);
        Assert.Equal(r.FaturamentoCentavos, r.PorFormaPagamento.Values.Sum());
        Assert.False(r.PorFormaPagamento.ContainsKey("CartaoCredito")); // cancelada
    }

    [Fact]
    public void Resumo_do_dia_agrupa_por_produto_e_tamanho_com_guid_do_produto()
    {
        var r = ResumoDeExemplo();
        var camM = r.PorProduto.Single(i => i.Nome == "Camiseta" && i.Tamanho == "M");
        Assert.Equal((2, 10000L), (camM.Pecas, camM.ValorCentavos));
        Assert.Equal("11111111-1111-1111-1111-111111111111", camM.ProdutoGuid);
        Assert.Equal("CAM1", camM.Referencia);
        var bone = r.PorProduto.Single(i => i.Nome == "Boné");
        Assert.Equal("", bone.Tamanho);
        Assert.Equal(2, bone.Pecas);
    }

    [Fact]
    public void Resumo_do_dia_vazio_gera_documento_zerado()
    {
        var r = MontadorPacoteLoja.ResumoDoDia(Dia, Array.Empty<Venda>(), new[] { Camiseta }, null, AgoraUtc);
        Assert.Equal(0, r.FaturamentoCentavos);
        Assert.Empty(r.PorProduto);
        Assert.Empty(r.PorFormaPagamento);
    }

    [Fact]
    public void Dia_vai_e_volta_pelos_campos_do_firestore()
    {
        var original = ResumoDeExemplo();
        var campos = SerializadorPacoteLoja.ParaCampos(original);

        // Formato REST do Firestore: inteiro como texto, mapa e lista aninhados.
        Assert.Equal("19500", campos["faturamentoCentavos"]!["integerValue"]!.GetValue<string>());
        Assert.Equal("2026-11-18", campos["dia"]!["stringValue"]!.GetValue<string>());
        Assert.NotNull(campos["porFormaPagamento"]!["mapValue"]!["fields"]!["Pix"]);
        Assert.NotNull(campos["porProduto"]!["arrayValue"]!["values"]);
        Assert.Equal("2026-11-19T15:30:00.0000000Z", campos["atualizadoEm"]!["timestampValue"]!.GetValue<string>());

        var volta = SerializadorPacoteLoja.DiaDeCampos(Reler(campos));
        Assert.Equal(original.Id, volta.Id);
        Assert.Equal(original.FaturamentoCentavos, volta.FaturamentoCentavos);
        Assert.Equal(original.NumeroDeVendas, volta.NumeroDeVendas);
        Assert.Equal(original.Pecas, volta.Pecas);
        Assert.Equal(original.CanceladasCentavos, volta.CanceladasCentavos);
        Assert.Equal(original.TrocasCentavos, volta.TrocasCentavos);
        Assert.Equal(original.PorFormaPagamento.OrderBy(k => k.Key), volta.PorFormaPagamento.OrderBy(k => k.Key));
        Assert.Equal(original.PorProduto, volta.PorProduto);
        Assert.Equal(AgoraUtc, volta.AtualizadoEmUtc);
    }

    [Fact]
    public void Serializacao_e_deterministica_reenviar_o_mesmo_dia_gera_o_mesmo_documento()
    {
        var a = SerializadorPacoteLoja.ParaCampos(ResumoDeExemplo()).ToJsonString();
        var b = SerializadorPacoteLoja.ParaCampos(ResumoDeExemplo()).ToJsonString();
        Assert.Equal(a, b);
    }

    [Fact]
    public void Estoque_ignora_inativos_e_sem_baixa_e_vai_e_volta()
    {
        var lista = MontadorPacoteLoja.Estoque(new[]
        {
            Camiseta, Bone,
            new Produto { Id = 3, Nome = "Inativo", Ativo = false },
            new Produto { Id = 4, Nome = "Serviço", VenderSemBaixa = true }
        }, AgoraUtc);
        Assert.Equal(new[] { "Boné", "Camiseta" }, lista.Select(e => e.Nome).ToArray());

        var cam = lista.Single(e => e.Nome == "Camiseta");
        Assert.Equal(7, cam.Total);
        Assert.Equal(4, cam.PorTamanho["M"]);

        foreach (var e in lista)
        {
            var campos = SerializadorPacoteLoja.ParaCampos(e);
            var volta = SerializadorPacoteLoja.EstoqueDeCampos(Reler(campos));
            Assert.Equal(e.ProdutoGuid, volta.ProdutoGuid);
            Assert.Equal(e.Referencia, volta.Referencia);
            Assert.Equal(e.Categoria, volta.Categoria);
            Assert.Equal(e.Total, volta.Total);
            Assert.Equal(e.CustoCentavos, volta.CustoCentavos);
            Assert.Equal(e.PorTamanho.OrderBy(k => k.Key), volta.PorTamanho.OrderBy(k => k.Key));
        }
        // "Sem tamanho" (texto vazio) vira o campo "_sem": o Firestore não aceita nome de campo vazio.
        var boneCampos = SerializadorPacoteLoja.ParaCampos(lista.Single(e => e.Nome == "Boné"));
        Assert.NotNull(boneCampos["porTamanho"]!["mapValue"]!["fields"]!["_sem"]);
    }

    [Fact]
    public void Lancamento_leva_so_o_nome_dos_anexos_e_vai_e_volta()
    {
        var l = new LancamentoEmpresa
        {
            Guid = Guid.Parse("33333333-3333-3333-3333-333333333333"), Data = new DateTime(2026, 11, 10), Tipo = TipoMovimentoEmpresa.Saida,
            Categoria = "Frete", Descricao = "Frete do fornecedor", ValorCentavos = 12345, Forma = FormaPagamento.Pix, Observacao = "ok",
            Anexos = { new AnexoLancamento { Id = 1, NomeOriginal = "nota.pdf", CaminhoRelativo = "2026/11/segredo-nota.pdf", Sha256 = "abc" } }
        };
        var nuvem = MontadorPacoteLoja.Lancamento(l, AgoraUtc);
        var campos = SerializadorPacoteLoja.ParaCampos(nuvem);
        var json = campos.ToJsonString();
        Assert.Contains("nota.pdf", json);
        Assert.DoesNotContain("segredo", json);   // o caminho local não vai à nuvem
        Assert.DoesNotContain("abc", json);

        var volta = SerializadorPacoteLoja.LancamentoDeCampos(Reler(campos));
        Assert.Equal(nuvem.Id, volta.Id);
        Assert.Equal("Saida", volta.Tipo);
        Assert.Equal("Pix", volta.Forma);
        Assert.Equal(12345, volta.ValorCentavos);
        Assert.Equal(new DateTime(2026, 11, 10), volta.Data);
        Assert.Equal(new[] { "nota.pdf" }, volta.NomesDosAnexos);
    }

    [Fact]
    public void Lancamento_sem_forma_nem_anexo_volta_com_nulos_e_lista_vazia()
    {
        var l = new LancamentoEmpresa { Data = new DateTime(2026, 11, 10), Tipo = TipoMovimentoEmpresa.Entrada, Categoria = "Aporte", Descricao = "Aporte", ValorCentavos = 1 };
        var volta = SerializadorPacoteLoja.LancamentoDeCampos(Reler(SerializadorPacoteLoja.ParaCampos(MontadorPacoteLoja.Lancamento(l, AgoraUtc))));
        Assert.Null(volta.Forma);
        Assert.Null(volta.Observacao);
        Assert.Empty(volta.NomesDosAnexos);
    }

    [Fact]
    public void Loja_e_preco_vao_e_voltam()
    {
        var loja = MontadorPacoteLoja.Loja("loja-1", "Loja Centro", "1.2.3", AgoraUtc);
        Assert.Equal(loja, SerializadorPacoteLoja.LojaDeCampos(Reler(SerializadorPacoteLoja.ParaCampos(loja))));

        var preco = new PrecoLojaNuvem("11111111-1111-1111-1111-111111111111", "loja-2", 5500, 4500, "admin", AgoraUtc);
        Assert.Equal(preco, SerializadorPacoteLoja.PrecoDeCampos(Reler(SerializadorPacoteLoja.ParaCampos(preco))));

        var semAtacado = preco with { PrecoAtacadoCentavos = null };
        var campos = SerializadorPacoteLoja.ParaCampos(semAtacado);
        Assert.False(campos.ContainsKey("precoAtacadoCentavos"));
        Assert.Null(SerializadorPacoteLoja.PrecoDeCampos(Reler(campos)).PrecoAtacadoCentavos);
    }

    [Fact]
    public void Campo_com_tipo_errado_gera_erro_claro_e_campo_ausente_vira_padrao()
    {
        var campos = SerializadorPacoteLoja.ParaCampos(ResumoDeExemplo());
        campos["faturamentoCentavos"] = SerializadorPacoteLoja.Texto("abc");
        Assert.Throws<FormatException>(() => SerializadorPacoteLoja.DiaDeCampos(campos));

        var parcial = SerializadorPacoteLoja.ParaCampos(ResumoDeExemplo());
        parcial.Remove("trocas");
        parcial.Remove("porProduto");
        var lido = SerializadorPacoteLoja.DiaDeCampos(parcial);
        Assert.Equal(0, lido.Trocas);
        Assert.Empty(lido.PorProduto);
    }

    [Fact]
    public void Caminhos_ficam_so_sob_empresas_lojas_e_precos_e_recusam_ids_perigosos()
    {
        Assert.Equal("empresas/emp1/lojas/loja-1", CaminhosNuvem.Loja("emp1", "loja-1"));
        Assert.Equal("empresas/emp1/lojas/loja-1/dias/2026-11-18", CaminhosNuvem.Dia("emp1", "loja-1", Dia));
        Assert.Equal("empresas/emp1/lojas/loja-1/estoque/abc", CaminhosNuvem.Estoque("emp1", "loja-1", "abc"));
        Assert.Equal("empresas/emp1/lojas/loja-1/lancamentos/id-9", CaminhosNuvem.Lancamento("emp1", "loja-1", "id-9"));
        Assert.Equal("empresas/emp1/precos/abc/lojas/loja-2", CaminhosNuvem.Preco("emp1", "abc", "loja-2"));

        foreach (var ruim in new[] { "", " ", "a/b", "../x", "a b", "loja.1", new string('a', 65) })
            Assert.Throws<RegraDeNegocioException>(() => CaminhosNuvem.Loja("emp1", ruim));
        Assert.Throws<RegraDeNegocioException>(() => CaminhosNuvem.Loja("../outra", "loja-1"));
        Assert.Throws<RegraDeNegocioException>(() => MontadorPacoteLoja.Loja("loja/1", "x", "1", AgoraUtc));
    }

    [Fact]
    public void Pacote_nao_usa_colecoes_do_erp()
    {
        var caminhos = new[]
        {
            CaminhosNuvem.Loja("e", "l"), CaminhosNuvem.Dia("e", "l", Dia), CaminhosNuvem.Estoque("e", "l", "p"),
            CaminhosNuvem.Lancamento("e", "l", "i"), CaminhosNuvem.Preco("e", "p", "l")
        };
        Assert.All(caminhos, c => Assert.StartsWith("empresas/e/", c));
        Assert.All(caminhos, c => Assert.Matches(@"^empresas/e/(lojas|precos)/", c));
    }

    /// <summary>Passa por texto JSON, como seria no envio e no download.</summary>
    private static JsonObject Reler(JsonObject o) => (JsonObject)JsonNode.Parse(o.ToJsonString())!;
}
