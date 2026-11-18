using System.Xml.Linq;
using NexusLS.Core.Fiscal;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

public class MontadorNfceTests
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    private static EmitenteNfce Emitente(string? ibge = "2604106") => new("11.222.333/0001-81", "0321418-40", "Loja Teste LTDA", "Loja Teste",
        "Rua das Flores", "100", "Centro", "55000-000", "Caruaru", "PE", ibge);

    private static ConfigNfce Config(string? regime = "SimplesNacional", string ambiente = PreparoFiscal.Homologacao) =>
        new(regime, ambiente, 1, "1", "61091000", null);

    private static Venda UmaVenda(int id = 1, FormaPagamento forma = FormaPagamento.Pix, long desconto = 0, long acrescimo = 0, params (string nome, int qtd, long preco)[] itens)
    {
        if (itens.Length == 0) itens = new[] { ("Camiseta", 1, 5000L) };
        var v = new Venda { Id = id, DataHora = new DateTime(2026, 11, 18, 10, 30, 0), FormaPagamento = forma, DescontoCentavos = desconto, AcrescimoCentavos = acrescimo };
        int p = 1;
        foreach (var (nome, qtd, preco) in itens)
            v.Itens.Add(new ItemVenda { ProdutoId = p++, NomeProduto = nome, Quantidade = qtd, PrecoUnitarioCentavos = preco, TotalCentavos = qtd * preco });
        v.SubtotalCentavos = v.Itens.Sum(i => i.TotalCentavos);
        v.TotalCentavos = v.SubtotalCentavos - desconto + acrescimo;
        return v;
    }

    private static ResultadoNfce Montar(Venda v, EmitenteNfce? e = null, ConfigNfce? c = null, IEnumerable<int>? ids = null) =>
        MontadorNfce.Montar(v, e ?? Emitente(), c ?? Config(), new Dictionary<int, Produto>(), ids ?? new[] { v.Id });

    [Fact]
    public void Digito_verificador_com_vetores_conhecidos()
    {
        Assert.Equal(2, ChaveAcessoNfce.CalcularDigito(new string('1', 43)));                 // soma 229, resto 9, DV 2
        Assert.Equal(0, ChaveAcessoNfce.CalcularDigito(new string('0', 43)));                 // resto 0 -> DV 0
        Assert.Equal(0, ChaveAcessoNfce.CalcularDigito(new string('0', 42) + "6"));           // soma 12, resto 1 -> DV 0
        Assert.Equal(3, ChaveAcessoNfce.CalcularDigito("3508059999909091027055001000000001518005127"));
    }

    [Fact]
    public void Chave_valida_so_com_44_digitos_e_dv_certo()
    {
        var chave = ChaveAcessoNfce.Montar(26, new DateTime(2026, 11, 18), "11222333000181", 65, 1, 7, 1, 12345);
        Assert.Equal(44, chave.Length);
        Assert.StartsWith("262611" + "11222333000181" + "65" + "001" + "000000007" + "1" + "00012345", chave);
        Assert.True(ChaveAcessoNfce.Valida(chave));
        var errada = chave[..43] + ((chave[43] - '0' + 1) % 10);
        Assert.False(ChaveAcessoNfce.Valida(errada));
        Assert.False(ChaveAcessoNfce.Valida("123"));
        Assert.False(ChaveAcessoNfce.Valida(null));
    }

    [Fact]
    public void Base_com_tamanho_errado_e_recusada() =>
        Assert.Throws<ArgumentException>(() => ChaveAcessoNfce.CalcularDigito("123"));

    [Fact]
    public void Monta_nota_com_um_item_sem_assinatura()
    {
        var r = Montar(UmaVenda());
        Assert.True(r.PodeGerarXml);
        Assert.Empty(r.Pendencias);                                  // só o IBGE informado; cadastros completos
        Assert.True(ChaveAcessoNfce.Valida(r.Chave));
        var doc = XDocument.Parse(r.Xml!);
        Assert.Contains("SEM VALIDADE FISCAL", r.Xml);
        Assert.Contains("NÃO ASSINADO", r.Xml);
        Assert.Empty(doc.Descendants(Ns + "Signature"));
        var inf = doc.Descendants(Ns + "infNFe").Single();
        Assert.Equal("4.00", inf.Attribute("versao")!.Value);
        Assert.Equal("NFe" + r.Chave, inf.Attribute("Id")!.Value);
        Assert.Equal("65", inf.Element(Ns + "ide")!.Element(Ns + "mod")!.Value);
        Assert.Equal("26", inf.Element(Ns + "ide")!.Element(Ns + "cUF")!.Value);
        Assert.Equal("11222333000181", inf.Element(Ns + "emit")!.Element(Ns + "CNPJ")!.Value);
        Assert.Equal("1", inf.Element(Ns + "emit")!.Element(Ns + "CRT")!.Value);
        var det = inf.Elements(Ns + "det").Single();
        Assert.Equal("61091000", det.Element(Ns + "prod")!.Element(Ns + "NCM")!.Value);
        Assert.Equal("5102", det.Element(Ns + "prod")!.Element(Ns + "CFOP")!.Value);
        Assert.Equal("1.0000", det.Element(Ns + "prod")!.Element(Ns + "qCom")!.Value);
        Assert.Equal("50.00", det.Element(Ns + "prod")!.Element(Ns + "vProd")!.Value);
        Assert.Equal("50.00", inf.Element(Ns + "total")!.Element(Ns + "ICMSTot")!.Element(Ns + "vNF")!.Value);
    }

    [Fact]
    public void Monta_varios_itens_com_desconto_e_acrescimo_rateados_que_fecham()
    {
        var v = UmaVenda(2, FormaPagamento.CartaoCredito, desconto: 1000, acrescimo: 700, ("A", 2, 3333), ("B", 1, 5001), ("C", 3, 1999));
        var r = Montar(v);
        var doc = XDocument.Parse(r.Xml!);
        var prods = doc.Descendants(Ns + "prod").ToList();
        Assert.Equal(3, prods.Count);
        decimal Soma(string n) => prods.Sum(p => p.Element(Ns + n) is { } e ? decimal.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture) : 0m);
        Assert.Equal(10.00m, Soma("vDesc"));
        Assert.Equal(7.00m, Soma("vOutro"));
        var tot = doc.Descendants(Ns + "ICMSTot").Single();
        Assert.Equal("10.00", tot.Element(Ns + "vDesc")!.Value);
        Assert.Equal("7.00", tot.Element(Ns + "vOutro")!.Value);
        Assert.Equal(((v.TotalCentavos) / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), tot.Element(Ns + "vNF")!.Value);
        Assert.Equal(new[] { "1", "2", "3" }, doc.Descendants(Ns + "det").Select(d => d.Attribute("nItem")!.Value));
    }

    [Fact]
    public void Ratear_sempre_fecha_a_soma()
    {
        var r = MontadorNfce.Ratear(100, new long[] { 333, 333, 334 });
        Assert.Equal(100, r.Sum());
        Assert.Equal(new long[3], MontadorNfce.Ratear(0, new long[] { 1, 2, 3 }));
    }

    [Theory]
    [InlineData(FormaPagamento.Dinheiro, "01")]
    [InlineData(FormaPagamento.Cheque, "02")]
    [InlineData(FormaPagamento.CartaoCredito, "03")]
    [InlineData(FormaPagamento.CartaoDebito, "04")]
    [InlineData(FormaPagamento.APrazo, "05")]
    [InlineData(FormaPagamento.Pix, "17")]
    [InlineData(FormaPagamento.ValeTroca, "99")]
    public void Formas_de_pagamento_seguem_a_tabela_tPag(FormaPagamento forma, string esperado) =>
        Assert.Equal(esperado, MontadorNfce.CodigoPagamento(forma));

    [Fact]
    public void Pagamento_em_dinheiro_leva_troco_e_cartao_leva_tpIntegra()
    {
        var din = UmaVenda(1, FormaPagamento.Dinheiro);
        din.ValorRecebidoCentavos = 10000; din.TrocoCentavos = 5000;
        var pag = XDocument.Parse(Montar(din).Xml!).Descendants(Ns + "pag").Single();
        Assert.Equal("100.00", pag.Element(Ns + "detPag")!.Element(Ns + "vPag")!.Value);
        Assert.Equal("50.00", pag.Element(Ns + "vTroco")!.Value);

        var card = XDocument.Parse(Montar(UmaVenda(1, FormaPagamento.CartaoDebito)).Xml!).Descendants(Ns + "detPag").Single();
        Assert.Equal("04", card.Element(Ns + "tPag")!.Value);
        Assert.Equal("2", card.Descendants(Ns + "tpIntegra").Single().Value);
    }

    [Fact]
    public void Vale_troca_vira_pagamento_99_separado()
    {
        var v = UmaVenda(1, FormaPagamento.Pix);
        v.ValeTrocaCentavos = 2000;
        var dets = XDocument.Parse(Montar(v).Xml!).Descendants(Ns + "detPag").ToList();
        Assert.Equal(2, dets.Count);
        Assert.Equal("30.00", dets[0].Element(Ns + "vPag")!.Value);
        Assert.Equal("99", dets[1].Element(Ns + "tPag")!.Value);
        Assert.Equal("20.00", dets[1].Element(Ns + "vPag")!.Value);
    }

    [Fact]
    public void Escapa_caracteres_especiais_e_remove_controle()
    {
        var v = UmaVenda(1, FormaPagamento.Pix, 0, 0, ("Camisa <Surf> & \"Cia\" 'x'\u0001", 1, 1000));
        var r = Montar(v, c: Config(ambiente: PreparoFiscal.Producao));
        Assert.Contains("&lt;Surf&gt; &amp;", r.Xml);
        var xProd = XDocument.Parse(r.Xml!).Descendants(Ns + "xProd").Single().Value;
        Assert.Equal("Camisa <Surf> & \"Cia\" 'x'", xProd);
    }

    [Fact]
    public void Homologacao_troca_o_nome_do_primeiro_item_pelo_texto_da_sefaz()
    {
        var xml = XDocument.Parse(Montar(UmaVenda()).Xml!);
        Assert.Equal(MontadorNfce.TextoHomologacao, xml.Descendants(Ns + "xProd").First().Value);
        Assert.Equal("2", xml.Descendants(Ns + "tpAmb").Single().Value);
    }

    [Fact]
    public void Numeracao_e_sequencial_sem_pular_e_ignora_canceladas()
    {
        Assert.Equal(1, MontadorNfce.NumeroSequencial(new[] { 5, 9, 12 }, 5));
        Assert.Equal(2, MontadorNfce.NumeroSequencial(new[] { 12, 5, 9 }, 9));      // venda 7 cancelada nem entra na lista
        Assert.Equal(3, MontadorNfce.NumeroSequencial(new[] { 5, 9, 12 }, 12));
        var r = Montar(UmaVenda(12), ids: new[] { 5, 9, 12 });
        Assert.Equal(3, r.Numero);
        Assert.Equal("3", XDocument.Parse(r.Xml!).Descendants(Ns + "nNF").Single().Value);
    }

    [Fact]
    public void Venda_cancelada_ou_sem_itens_ou_nula_e_bloqueada()
    {
        var c = UmaVenda(); c.Cancelada = true;
        var r = Montar(c);
        Assert.False(r.PodeGerarXml);
        Assert.Contains("cancelada", r.Bloqueio);
        Assert.False(Montar(new Venda { Id = 3 }).PodeGerarXml);
        Assert.Contains("não tem itens", Montar(new Venda { Id = 3 }).Bloqueio);
        Assert.False(MontadorNfce.Montar(null, Emitente(), Config(), new Dictionary<int, Produto>(), Array.Empty<int>()).PodeGerarXml);
    }

    [Fact]
    public void Lista_o_que_falta_em_portugues()
    {
        var emit = new EmitenteNfce("123", "", null, "Loja", null, null, null, null, null, null, null);
        var cfg = new ConfigNfce(null, PreparoFiscal.Homologacao, 1, null, null, null);
        var r = MontadorNfce.Montar(UmaVenda(), emit, cfg, new Dictionary<int, Produto>(), new[] { 1 });
        Assert.True(r.PodeGerarXml);
        Assert.False(r.CadastrosCompletos);
        var t = string.Join("\n", r.Pendencias);
        Assert.Contains("CNPJ da loja inválido", t);
        Assert.Contains("Inscrição estadual", t);
        Assert.Contains("UF da loja", t);
        Assert.Contains("NCM ausente", t);
        Assert.Contains("Regime tributário não escolhido", t);
        Assert.Contains("ID do CSC", t);
        Assert.Contains("IBGE", t);
        Assert.Null(r.Chave);
    }

    [Fact]
    public void Codigo_ibge_do_municipio_nunca_e_inventado()
    {
        var r = Montar(UmaVenda(), e: Emitente(ibge: null));
        Assert.Contains(r.Pendencias, p => p.Contains("IBGE") && p.Contains("Caruaru"));
        Assert.Empty(XDocument.Parse(r.Xml!).Descendants(Ns + "cMun"));
        Assert.Empty(XDocument.Parse(r.Xml!).Descendants(Ns + "cMunFG"));
    }

    [Fact]
    public void Ncm_e_cfop_do_produto_valem_mais_que_o_padrao()
    {
        var v = UmaVenda();
        var prod = new Dictionary<int, Produto> { [1] = new Produto { Id = 1, Ncm = "62052000", Cfop = "5405" } };
        var r = MontadorNfce.Montar(v, Emitente(), Config(), prod, new[] { 1 });
        var p = XDocument.Parse(r.Xml!).Descendants(Ns + "prod").Single();
        Assert.Equal("62052000", p.Element(Ns + "NCM")!.Value);
        Assert.Equal("5405", p.Element(Ns + "CFOP")!.Value);
    }

    [Theory]
    [InlineData("Caruaru - PE", "Caruaru", "PE")]
    [InlineData("Recife/pe", "Recife", "PE")]
    [InlineData("Natal", "Natal", null)]
    [InlineData("", null, null)]
    public void Separa_cidade_e_uf(string texto, string? cidade, string? uf) =>
        Assert.Equal((cidade, uf), EmitenteNfce.SepararCidadeUf(texto));

    [Fact]
    public void Gtin_valido_confere_o_digito()
    {
        Assert.True(MontadorNfce.GtinValido("7891000100103"));
        Assert.False(MontadorNfce.GtinValido("7891000100104"));
        Assert.False(MontadorNfce.GtinValido("INT-000001"));
    }
}
