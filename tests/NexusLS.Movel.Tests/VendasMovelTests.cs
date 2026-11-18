using System.Net;
using System.Text.Json;
using NexusLS.Core.Models;
using NexusLS.Tests.Infra;

namespace NexusLS.Movel.Tests;

/// <summary>Aba Vendas do celular: resumo e pesquisa, pelo servidor HTTP de verdade.</summary>
public class VendasMovelTests
{
    private const string Pin = "123456";

    private static Task<ServidorEmExecucao> Iniciar(BancoDeTeste banco) =>
        ServidorMovel.IniciarAsync(banco.Fabrica, Pin, porta: 0, url: "http://127.0.0.1:0");

    private static HttpClient CriarCliente(ServidorEmExecucao servidor, string? pin = Pin)
    {
        var cliente = new HttpClient { BaseAddress = new Uri(servidor.Endereco), Timeout = TimeSpan.FromSeconds(15) };
        if (pin is not null) cliente.DefaultRequestHeaders.Add("X-Pin", pin);
        return cliente;
    }

    private static void SemearVenda(BancoDeTeste banco, DateTime quando, FormaPagamento forma, bool cancelada,
        string clienteNome, params (int ProdutoId, string Nome, string Tamanho, int Qtd, long Unitario)[] itens)
    {
        using var db = banco.CriarContexto();
        var cliente = new Cliente { Nome = clienteNome };
        db.Clientes.Add(cliente);
        db.SaveChanges();

        var total = itens.Sum(i => i.Qtd * i.Unitario);
        var venda = new Venda
        {
            DataHora = quando,
            FormaPagamento = forma,
            SubtotalCentavos = total,
            TotalCentavos = total,
            Cancelada = cancelada,
            Operador = "Maria Vendedora",
            ClienteId = cliente.Id
        };

        foreach (var i in itens)
        {
            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = i.ProdutoId,
                NomeProduto = i.Nome,
                Tamanho = i.Tamanho,
                Quantidade = i.Qtd,
                PrecoUnitarioCentavos = i.Unitario,
                CustoUnitarioCentavos = 1234,
                TotalCentavos = i.Qtd * i.Unitario
            });
        }

        db.Vendas.Add(venda);
        db.SaveChanges();
    }

    private static (int Camisa, int Bermuda) SemearCenario(BancoDeTeste banco)
    {
        var camisa = DadosDeTeste.SemearProduto(banco, "CAMISA GOLA CARECA", "1019", 5000, 0, null, ("P", 1, 5), ("M", 2, 5));
        var bermuda = DadosDeTeste.SemearProduto(banco, "BERMUDA CARGO", "1402", 7000, 0, null, ("M", 1, 5));
        var agora = DateTime.Now;

        // Hoje: 2 camisas (M) no Pix + camisa P e bermuda no dinheiro = 3 vendas... aqui sao 2 vendas.
        SemearVenda(banco, agora, FormaPagamento.Pix, false, "CLIENTE SECRETO UM",
            (camisa, "CAMISA GOLA CARECA", "M", 2, 5000));
        SemearVenda(banco, agora, FormaPagamento.Dinheiro, false, "CLIENTE SECRETO DOIS",
            (camisa, "CAMISA GOLA CARECA", "P", 1, 5000), (bermuda, "BERMUDA CARGO", "M", 1, 7000));

        // Cancelada hoje: fica fora dos totais.
        SemearVenda(banco, agora, FormaPagamento.Pix, true, "CLIENTE SECRETO TRES",
            (bermuda, "BERMUDA CARGO", "M", 3, 7000));

        // Ontem (periodo anterior de "hoje"): 1 venda de 10000.
        SemearVenda(banco, agora.Date.AddDays(-1).AddHours(10), FormaPagamento.Dinheiro, false, "CLIENTE ANTIGO",
            (camisa, "CAMISA GOLA CARECA", "M", 2, 5000));

        return (camisa, bermuda);
    }

    [Theory]
    [InlineData("/api/vendas/resumo?periodo=hoje")]
    [InlineData("/api/vendas/busca?periodo=hoje&texto=1019")]
    public async Task Vendas_sem_pin_responde_401(string caminho)
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor, pin: null);

        var resposta = await cliente.GetAsync(caminho);

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Resumo_do_dia_traz_totais_formas_e_comparacao()
    {
        using var banco = new BancoDeTeste();
        SemearCenario(banco);
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var json = await cliente.GetStringAsync("/api/vendas/resumo?periodo=hoje");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        // Hoje: 10000 (Pix) + 12000 (dinheiro) = 22000, em 2 vendas e 4 pecas; a cancelada fica de fora.
        Assert.Equal(22000, r.GetProperty("totalCentavos").GetProperty("atual").GetInt64());
        Assert.Equal(10000, r.GetProperty("totalCentavos").GetProperty("anterior").GetInt64());
        Assert.Equal(120m, r.GetProperty("totalCentavos").GetProperty("variacaoPct").GetDecimal());
        Assert.Equal(2, r.GetProperty("vendas").GetProperty("atual").GetInt32());
        Assert.Equal(4, r.GetProperty("pecas").GetProperty("atual").GetInt32());
        Assert.Equal(11000, r.GetProperty("ticketMedioCentavos").GetProperty("atual").GetInt64());
        Assert.Equal(1, r.GetProperty("canceladas").GetInt32());

        var formas = r.GetProperty("porForma").EnumerateArray().ToList();
        Assert.Equal(2, formas.Count);
        Assert.Equal("Dinheiro", formas[0].GetProperty("forma").GetString());
        Assert.Equal(12000, formas[0].GetProperty("valorCentavos").GetInt64());

        var primeiro = r.GetProperty("maisVendidos").EnumerateArray().First();
        Assert.Equal("CAMISA GOLA CARECA", primeiro.GetProperty("nome").GetString());
        Assert.Equal(3, primeiro.GetProperty("pecas").GetInt32());
    }

    [Fact]
    public async Task Resumo_nao_expoe_cliente_vendedor_nem_custo()
    {
        using var banco = new BancoDeTeste();
        SemearCenario(banco);
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var resumo = await cliente.GetStringAsync("/api/vendas/resumo?periodo=mes");
        var busca = await cliente.GetStringAsync("/api/vendas/busca?periodo=mes&texto=camisa");

        foreach (var corpo in new[] { resumo, busca })
        {
            Assert.DoesNotContain("SECRETO", corpo);
            Assert.DoesNotContain("Maria", corpo);
            Assert.DoesNotContain("1234", corpo);
            Assert.DoesNotContain("custo", corpo, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("lucro", corpo, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task Busca_por_referencia_soma_unidades_e_valor_do_periodo()
    {
        using var banco = new BancoDeTeste();
        SemearCenario(banco);
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var json = await cliente.GetStringAsync("/api/vendas/busca?periodo=hoje&texto=1019");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;

        Assert.True(r.GetProperty("achou").GetBoolean());
        Assert.Equal(1, r.GetProperty("produtosEncontrados").GetInt32());
        Assert.Equal(3, r.GetProperty("unidades").GetInt32());
        Assert.Equal(15000, r.GetProperty("valorCentavos").GetInt64());

        var variacoes = r.GetProperty("variacoes").EnumerateArray().ToList();
        Assert.Equal("M", variacoes[0].GetProperty("tamanho").GetString());
        Assert.Equal(2, variacoes[0].GetProperty("unidades").GetInt32());
    }

    [Fact]
    public async Task Busca_por_nome_sem_acento_e_sem_resultado()
    {
        using var banco = new BancoDeTeste();
        SemearCenario(banco);
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        using var porNome = JsonDocument.Parse(await cliente.GetStringAsync("/api/vendas/busca?periodo=hoje&texto=bermuda"));
        Assert.Equal(1, porNome.RootElement.GetProperty("unidades").GetInt32());
        Assert.Equal(7000, porNome.RootElement.GetProperty("valorCentavos").GetInt64());

        using var nada = JsonDocument.Parse(await cliente.GetStringAsync("/api/vendas/busca?periodo=hoje&texto=naoexiste"));
        Assert.False(nada.RootElement.GetProperty("achou").GetBoolean());
        Assert.Equal(0, nada.RootElement.GetProperty("unidades").GetInt32());
    }

    [Theory]
    [InlineData("/api/vendas/resumo?periodo=ano")]
    [InlineData("/api/vendas/resumo")]
    [InlineData("/api/vendas/busca?periodo=ontem&texto=1019")]
    [InlineData("/api/vendas/busca?periodo=hoje")]
    [InlineData("/api/vendas/busca?periodo=hoje&texto=a")]
    public async Task Periodo_invalido_ou_busca_curta_responde_400(string caminho)
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var resposta = await cliente.GetAsync(caminho);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Contains("erro", await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Tela_de_vendas_vem_embutida()
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor, pin: null);

        var html = await cliente.GetStringAsync("/");
        var js = await cliente.GetStringAsync("/vendas.js");

        Assert.Contains("aba-vendas", html);
        Assert.DoesNotContain("innerHTML", js);
    }
}
