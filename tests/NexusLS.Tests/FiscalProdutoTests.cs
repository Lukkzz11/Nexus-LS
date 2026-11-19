using NexusLS.Core.Fiscal;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class FiscalProdutoTests
{
    [Fact]
    public async Task Cadastro_grava_e_edicao_sem_fiscal_preserva()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        var fiscal = new DadosFiscaisProduto("0301100", "0 - Nacional", "102", "", "07", "", "07", "", "par", "000", "000001");
        var p = await servico.CadastrarAsync(new DadosProduto("Meia", null, 1500, 500, 1, Fiscal: fiscal));

        using (var db = banco.CriarContexto())
        {
            var g = db.Produtos.Single();
            Assert.Equal("0301100", g.Cest);
            Assert.Equal("PAR", g.UnidadeComercial);
            Assert.Equal("000", g.CstIbsCbs);
            Assert.Equal("000001", g.ClassTribIbsCbs);
        }

        await servico.EditarAsync(p.Id, new DadosProduto("Meia", null, 1600, 500, 1)); // sem Fiscal: não mexe
        using (var db = banco.CriarContexto()) Assert.Equal("PAR", db.Produtos.Single().UnidadeComercial);
    }

    [Theory]
    [InlineData("123", null)]          // CEST curto
    [InlineData(null, "99")]           // origem fora de 0..8
    public void Valores_invalidos_sao_recusados(string? cest, string? origem)
    {
        var p = new Produto();
        Assert.Throws<RegraDeNegocioException>(() => RegrasFiscaisProduto.Aplicar(p, new DadosFiscaisProduto(Cest: cest, Origem: origem)));
    }

    [Fact]
    public void Ibs_e_cbs_exigem_cst_e_classificacao_juntos() =>
        Assert.Throws<RegraDeNegocioException>(() => RegrasFiscaisProduto.Aplicar(new Produto(), new DadosFiscaisProduto(CstIbsCbs: "000")));

    [Theory]
    [InlineData("18", 1800)]
    [InlineData("18,5", 1850)]
    [InlineData("", 0)]
    public void Aliquota_vira_centesimos(string texto, int esperado) => Assert.Equal(esperado, RegrasFiscaisProduto.Aliquota(texto, "ICMS"));

    [Fact]
    public async Task Xml_usa_os_dados_fiscais_do_produto()
    {
        using var banco = new BancoDeTeste();
        using (var db = banco.CriarContexto())
        {
            db.ConfiguracoesLoja.Add(new ConfiguracaoLoja { Id = 1, NomeLoja = "Loja", Documento = "11222333000181", InscricaoEstadual = "032141840", RazaoSocial = "Loja LTDA", Endereco = "Rua A", Bairro = "Centro", Cep = "55000000", CidadeUf = "Caruaru - PE" });
            var p = new Produto
            {
                Nome = "Meia", Ncm = "61159900", Cest = "2806400", OrigemMercadoria = 1, CstIcms = "500", UnidadeComercial = "PAR",
                CstPis = "01", AliquotaPisBp = 165, CstCofins = "01", AliquotaCofinsBp = 760, CstIbsCbs = "000", ClassTribIbsCbs = "000001"
            };
            db.Produtos.Add(p);
            db.SaveChanges();
            var v = new Venda { DataHora = DateTime.Now, FormaPagamento = FormaPagamento.Pix, SubtotalCentavos = 10000, TotalCentavos = 10000 };
            v.Itens.Add(new ItemVenda { ProdutoId = p.Id, NomeProduto = "Meia", Quantidade = 1, PrecoUnitarioCentavos = 10000, TotalCentavos = 10000 });
            db.Vendas.Add(v);
            db.SaveChanges();
        }
        var r = await new ConferenciaNfce(banco.Fabrica).ConferirUltimaVendaAsync(new ConfiguracaoSeguranca { FiscalRegime = "SimplesNacional", FiscalCscId = "1" });
        var xml = r.Xml!;
        Assert.Contains("<CEST>2806400</CEST>", xml);
        Assert.Contains("<uCom>PAR</uCom>", xml);
        Assert.Contains("<CSOSN>500</CSOSN>", xml);
        Assert.Contains("<orig>1</orig>", xml);
        Assert.Contains("<pPIS>1.65</pPIS>", xml);
        Assert.Contains("<vCOFINS>7.60</vCOFINS>", xml);
        Assert.Contains("<cClassTrib>000001</cClassTrib>", xml);
        Assert.Contains(r.Avisos, a => a.Contains("IBS/CBS"));
    }
}
