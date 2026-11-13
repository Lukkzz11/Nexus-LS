using NexusLS.Core.Pagamentos;
using NexusLS.Core.Services;
using NexusLS.Core.Models;

namespace NexusLS.Tests;

public class PixEstornoTests
{
    private sealed class Repo : IRepositorioPix
    {
        public Dictionary<string, CobrancaPix> D { get; } = new();
        public Task<CobrancaPix?> PorChaveVendaAsync(string c, CancellationToken ct = default) => Task.FromResult(D.GetValueOrDefault(c));
        public Task<CobrancaPix?> PorIdExternoAsync(string g, string id, CancellationToken ct = default) => Task.FromResult(D.Values.FirstOrDefault(x => x.IdExterno == id));
        public Task SalvarAsync(CobrancaPix c, CancellationToken ct = default) { D[c.ChaveVenda] = c; return Task.CompletedTask; }
    }
    private sealed class Gw : IGatewayPix, IEstornoPix
    {
        public int Estornos; public bool Falha;
        public string Nome => "MercadoPago";
        public Task<CobrancaPix> CriarAsync(long v, string d, string k, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StatusPix> ConsultarAsync(string id, CancellationToken ct = default) => Task.FromResult(StatusPix.Aprovado);
        public Task EstornarAsync(string id, string k, CancellationToken ct = default) { if (Falha) throw new HttpRequestException("recusado"); Estornos++; return Task.CompletedTask; }
    }

    private static (ServicoPix, Repo, Gw) Montar(StatusPix status)
    {
        var repo = new Repo(); var gw = new Gw();
        repo.D["v"] = new CobrancaPix("MercadoPago", "99", "v", 1000, "x", null, status, null);
        return (new ServicoPix(new IGatewayPix[] { gw }, repo, () => TarifaGateway.Padrao), repo, gw);
    }

    [Fact]
    public async Task Estorna_pix_pago_e_marca_estornado()
    {
        var (s, r, g) = Montar(StatusPix.Aprovado);
        await s.EstornarAsync("v");
        Assert.Equal(1, g.Estornos);
        Assert.Equal(StatusPix.Estornado, r.D["v"].Status);
    }

    [Theory]
    [InlineData(StatusPix.Pendente)]
    [InlineData(StatusPix.Estornado)]
    [InlineData(StatusPix.Expirado)]
    public async Task Nao_estorna_o_que_nao_foi_pago(StatusPix status)
    {
        var (s, _, g) = Montar(status);
        await Assert.ThrowsAsync<InvalidOperationException>(() => s.EstornarAsync("v"));
        Assert.Equal(0, g.Estornos);
    }

    [Fact]
    public async Task Gateway_recusando_mantem_pago()
    {
        var (s, r, g) = Montar(StatusPix.Aprovado);
        g.Falha = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => s.EstornarAsync("v"));
        Assert.Equal(StatusPix.Aprovado, r.D["v"].Status);
    }

    [Theory]
    [InlineData("refunded", StatusPix.Estornado)]
    [InlineData("charged_back", StatusPix.Estornado)]
    [InlineData("approved", StatusPix.Aprovado)]
    public void Traduz_estorno_do_mercado_pago(string s, StatusPix esperado) => Assert.Equal(esperado, MercadoPagoGateway.Traduzir(s));

    [Fact]
    public void Conferencia_aponta_pago_sem_venda_e_pendente_antigo()
    {
        var agora = new DateTime(2026, 11, 13, 12, 0, 0);
        CobrancaPixRegistro R(StatusPix s, long v, int? venda, int horas) => new() { Status = s, ValorCentavos = v, VendaId = venda, CriadaEm = agora.AddHours(-horas), IdExterno = "1", Gateway = "MercadoPago" };
        var r = ConferenciaPix.Resumir(new[] { R(StatusPix.Aprovado, 1000, 1, 5), R(StatusPix.Aprovado, 500, null, 5), R(StatusPix.Estornado, 300, 2, 5), R(StatusPix.Pendente, 200, null, 3), R(StatusPix.Pendente, 200, null, 1) }, agora);
        Assert.Equal(2, r.Aprovados);
        Assert.Equal(1500, r.AprovadosCentavos);
        Assert.Single(r.PagosSemVenda);
        Assert.Single(r.PendentesAntigos);
        Assert.Equal(1, r.Estornados);
        Assert.Contains("10,00", ConferenciaPix.Csv(new[] { R(StatusPix.Aprovado, 1000, 1, 5) }));
    }
}

public class MesclaProdutosTests : IDisposable
{
    private readonly NexusLS.Tests.Infra.BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    [Fact]
    public void Chave_ignora_acento_caixa_e_espacos() =>
        Assert.Equal(MesclaProdutos.Chave("Camiseta  Básica"), MesclaProdutos.Chave(" camiseta basica "));

    [Fact]
    public async Task Mescla_soma_estoque_desativa_repetido_e_mantem_total()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var a = await produtos.CadastrarAsync(new DadosProduto("Boné Preto", null, 5000, 0, 0), 3);
        var b = await produtos.CadastrarAsync(new DadosProduto("bone  preto", null, 5000, 0, 0), 4);
        await produtos.CadastrarAsync(new DadosProduto("Outro", null, 100, 0, 0), 1);
        var mescla = new MesclaProdutos(_banco.Fabrica);

        var grupos = await mescla.ListarAsync();
        Assert.Single(grupos);
        Assert.Equal(b.Id, grupos[0].Produtos[0].Id);   // maior estoque fica

        await mescla.MesclarAsync(b.Id, a.Id);
        var fica = await produtos.ObterAsync(b.Id);
        var sai = await produtos.ObterAsync(a.Id);
        Assert.Equal(7, fica!.EstoqueAtual);
        Assert.Equal(0, sai!.EstoqueAtual);
        Assert.False(sai.Ativo);
        Assert.Empty(await mescla.ListarAsync());
    }
}

public class DescreverContaTests
{
    [Fact]
    public void Conta_de_teste_e_real_sao_distinguidas()
    {
        Assert.Contains("conta de TESTE", NexusLS.Core.Pagamentos.MercadoPagoGateway.DescreverConta("{\"nickname\":\"TESTUSER123\"}"));
        Assert.Contains("conta REAL", NexusLS.Core.Pagamentos.MercadoPagoGateway.DescreverConta("{\"nickname\":\"LUCAS99\"}"));
        Assert.Equal("", NexusLS.Core.Pagamentos.MercadoPagoGateway.DescreverConta("nao e json"));
        Assert.Contains("conta de TESTE", NexusLS.Core.Pagamentos.MercadoPagoGateway.DescreverConta("{\"nickname\":\"LUCAS99\"}", "TEST-abc"));
    }
}
