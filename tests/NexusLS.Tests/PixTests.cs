using NexusLS.Core.Pagamentos;

namespace NexusLS.Tests;

public class PixTests
{
    private sealed class Memoria : IRepositorioPix
    {
        public Dictionary<string, CobrancaPix> D { get; } = new();
        public Task<CobrancaPix?> PorChaveVendaAsync(string c, CancellationToken ct = default) => Task.FromResult(D.GetValueOrDefault(c));
        public Task SalvarAsync(CobrancaPix c, CancellationToken ct = default) { D[c.ChaveVenda] = c; return Task.CompletedTask; }
    }

    private sealed class Falso : IGatewayPix
    {
        public Falso(string nome) { Nome = nome; }
        public string Nome { get; }
        public int Criacoes;
        public StatusPix Status = StatusPix.Pendente;
        public bool SemInternet;
        public Task<CobrancaPix> CriarAsync(long v, string d, string chave, CancellationToken ct = default)
        {
            Criacoes++;
            return Task.FromResult(new CobrancaPix(Nome, "id" + Criacoes, chave, v, "000201...", null, StatusPix.Pendente, null));
        }
        public Task<StatusPix> ConsultarAsync(string id, CancellationToken ct = default) =>
            SemInternet ? throw new HttpRequestException("sem rede") : Task.FromResult(Status);
    }

    [Theory]
    [InlineData(1000, "MercadoPago")]
    [InlineData(7575, "MercadoPago")]    // R$ 75,75: ainda Mercado Pago
    [InlineData(7576, "SyncPay")]        // R$ 75,76: SyncPay passa a ser mais barato
    [InlineData(15000, "SyncPay")]       // R$ 150,00
    [InlineData(15001, "MercadoPago")]   // acima de R$ 150: Mercado Pago
    [InlineData(50000, "MercadoPago")]
    public void Escolhe_o_gateway_de_menor_custo_nas_faixas_do_documento(long valor, string esperado) =>
        Assert.Equal(esperado, RoteadorPix.Escolher(valor, TarifaGateway.Padrao).Gateway);

    [Fact]
    public void Gateway_desligado_nao_e_usado_e_sem_nenhum_ativo_da_erro()
    {
        var soSync = TarifaGateway.Padrao.Select(t => t.Gateway == "MercadoPago" ? t with { Ativo = false } : t).ToList();
        Assert.Equal("SyncPay", RoteadorPix.Escolher(1000, soSync).Gateway);
        Assert.Throws<InvalidOperationException>(() => RoteadorPix.Escolher(1000, soSync.Select(t => t with { Ativo = false })));
    }

    [Fact]
    public void Empate_vai_para_a_menor_prioridade()
    {
        var iguais = new[] { new TarifaGateway("A", true, 50, 0, 0, 2), new TarifaGateway("B", true, 50, 0, 0, 1) };
        Assert.Equal("B", RoteadorPix.Escolher(1000, iguais).Gateway);
    }

    [Fact]
    public async Task Mesma_venda_nao_gera_cobranca_duplicada_e_so_aprovado_conta_como_pago()
    {
        var mp = new Falso("MercadoPago"); var sp = new Falso("SyncPay");
        var servico = new ServicoPix(new IGatewayPix[] { mp, sp }, new Memoria());

        var a = await servico.IniciarAsync("venda-1", 5000, "Venda 1");
        var b = await servico.IniciarAsync("venda-1", 5000, "Venda 1");
        Assert.Equal(1, mp.Criacoes);
        Assert.Equal(a.IdExterno, b.IdExterno);
        Assert.Equal(StatusPix.Pendente, b.Status);   // QR gerado não é venda paga

        mp.Status = StatusPix.Aprovado;
        Assert.Equal(StatusPix.Aprovado, (await servico.AtualizarAsync("venda-1"))!.Status);
    }

    [Fact]
    public async Task Sem_internet_continua_pendente_e_expirada_gera_nova_cobranca()
    {
        var mp = new Falso("MercadoPago");
        var servico = new ServicoPix(new IGatewayPix[] { mp }, new Memoria());
        await servico.IniciarAsync("v", 5000, "x");

        mp.SemInternet = true;
        Assert.Equal(StatusPix.Pendente, (await servico.AtualizarAsync("v"))!.Status);
        Assert.Equal(1, mp.Criacoes);

        mp.SemInternet = false; mp.Status = StatusPix.Expirado;
        var nova = await servico.IniciarAsync("v", 5000, "x");
        Assert.Equal(2, mp.Criacoes);
        Assert.Equal(StatusPix.Pendente, nova.Status);
    }

    [Theory]
    [InlineData("approved", StatusPix.Aprovado)]
    [InlineData("pending", StatusPix.Pendente)]
    [InlineData("in_process", StatusPix.Pendente)]
    [InlineData("cancelled", StatusPix.Cancelado)]
    [InlineData("expired", StatusPix.Expirado)]
    [InlineData("qualquer_coisa", StatusPix.Pendente)]
    public void Mercado_pago_so_aprova_com_approved(string status, StatusPix esperado) =>
        Assert.Equal(esperado, MercadoPagoGateway.Traduzir(status));
}
