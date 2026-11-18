using NexusLS.Core.Fiscal;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class ConferenciaNfceTests
{
    [Fact]
    public async Task Confere_a_ultima_venda_gravada_e_bloqueia_se_cancelada()
    {
        using var banco = new BancoDeTeste();
        using (var db = banco.CriarContexto())
        {
            db.ConfiguracoesLoja.Add(new ConfiguracaoLoja { Id = 1, NomeLoja = "Loja", Documento = "11222333000181", InscricaoEstadual = "032141840", RazaoSocial = "Loja LTDA", Endereco = "Rua A", Bairro = "Centro", Cep = "55000000", CidadeUf = "Caruaru - PE" });
            var p = new Produto { Nome = "Camiseta", Ncm = "61091000" };
            db.Produtos.Add(p);
            db.SaveChanges();
            var v = new Venda { DataHora = DateTime.Now, FormaPagamento = FormaPagamento.Pix, SubtotalCentavos = 5000, TotalCentavos = 5000 };
            v.Itens.Add(new ItemVenda { ProdutoId = p.Id, NomeProduto = "Camiseta", Quantidade = 1, PrecoUnitarioCentavos = 5000, TotalCentavos = 5000 });
            db.Vendas.Add(v);
            db.SaveChanges();
        }
        var cfg = new ConfiguracaoSeguranca { FiscalRegime = "Mei", FiscalCscId = "1" };
        var servico = new ConferenciaNfce(banco.Fabrica);
        var r = await servico.ConferirUltimaVendaAsync(cfg);
        Assert.True(r.PodeGerarXml);
        Assert.Equal(1, r.Numero);
        Assert.Contains("SEM-VALIDADE-FISCAL", ConferenciaNfce.NomeArquivo(r));
        Assert.Contains(r.Pendencias, x => x.Contains("IBGE"));

        using (var db = banco.CriarContexto()) { db.Vendas.Single().Cancelada = true; db.SaveChanges(); }
        Assert.False((await servico.ConferirUltimaVendaAsync(cfg)).PodeGerarXml);
    }

    [Fact]
    public async Task Sem_vendas_nao_gera_xml()
    {
        using var banco = new BancoDeTeste();
        var r = await new ConferenciaNfce(banco.Fabrica).ConferirUltimaVendaAsync(new ConfiguracaoSeguranca());
        Assert.False(r.PodeGerarXml);
        Assert.NotNull(r.Bloqueio);
    }
}
