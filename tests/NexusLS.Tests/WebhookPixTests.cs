using System.Net;
using System.Net.Sockets;
using NexusLS.Core.Pagamentos;

namespace NexusLS.Tests;

public class WebhookPixTests
{
    private sealed class Repo : IRepositorioPix
    {
        public Dictionary<string, CobrancaPix> D { get; } = new();
        public Task<CobrancaPix?> PorChaveVendaAsync(string c, CancellationToken ct = default) => Task.FromResult(D.GetValueOrDefault(c));
        public Task<CobrancaPix?> PorIdExternoAsync(string g, string id, CancellationToken ct = default) =>
            Task.FromResult(D.Values.FirstOrDefault(x => x.Gateway == g && x.IdExterno == id));
        public Task SalvarAsync(CobrancaPix c, CancellationToken ct = default) { D[c.ChaveVenda] = c; return Task.CompletedTask; }
    }

    private sealed class Gateway : IGatewayPix
    {
        public string Nome => "MercadoPago";
        public StatusPix Status = StatusPix.Pendente;
        public Task<CobrancaPix> CriarAsync(long v, string d, string chave, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<StatusPix> ConsultarAsync(string id, CancellationToken ct = default) => Task.FromResult(Status);
    }

    private static (ServicoPix Pix, Repo Repo, Gateway Gw) Montar()
    {
        var repo = new Repo();
        var gw = new Gateway();
        repo.D["v1"] = new CobrancaPix("MercadoPago", "123456", "v1", 3500, "000201", null, StatusPix.Pendente, null);
        return (new ServicoPix(new[] { gw }, repo), repo, gw);
    }

    private static int PortaLivre()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var p = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return p;
    }

    [Theory]
    [InlineData("{\"type\":\"payment\",\"data\":{\"id\":\"123456\"}}", "123456")]
    [InlineData("{\"type\":\"payment\",\"data\":{\"id\":123456}}", "123456")]
    [InlineData("{\"type\":\"merchant_order\",\"data\":{\"id\":\"1\"}}", null)]
    [InlineData("{\"data\":{\"id\":\"abc; drop\"}}", null)]
    [InlineData("lixo que nao e json", null)]
    [InlineData("", null)]
    public void ExtrairId_so_aceita_aviso_de_pagamento_com_id_numerico(string corpo, string? esperado) =>
        Assert.Equal(esperado, ReceptorWebhookPix.ExtrairId(corpo));

    [Fact]
    public void ExtrairId_usa_o_id_da_url_quando_nao_ha_corpo() =>
        Assert.Equal("987", ReceptorWebhookPix.ExtrairId("", "987"));

    [Fact]
    public async Task Aviso_confirma_consultando_o_gateway_e_dispara_o_evento()
    {
        var (pix, repo, gw) = Montar();
        CobrancaPix? avisada = null;
        pix.CobrancaAtualizada += c => avisada = c;
        gw.Status = StatusPix.Aprovado;

        var r = await pix.ConfirmarPorAvisoAsync("MercadoPago", "123456");

        Assert.Equal(StatusPix.Aprovado, r!.Status);
        Assert.Equal(StatusPix.Aprovado, repo.D["v1"].Status);
        Assert.Equal("v1", avisada!.ChaveVenda);
    }

    [Fact]
    public async Task Aviso_de_cobranca_desconhecida_e_ignorado()
    {
        var (pix, _, _) = Montar();
        Assert.Null(await pix.ConfirmarPorAvisoAsync("MercadoPago", "999"));
    }

    [Fact]
    public async Task Aviso_com_gateway_ainda_pendente_nao_marca_como_pago()
    {
        var (pix, repo, _) = Montar();   // o gateway diz pendente, mesmo que o aviso afirme o contrário
        var r = await pix.ConfirmarPorAvisoAsync("MercadoPago", "123456");
        Assert.Equal(StatusPix.Pendente, r!.Status);
        Assert.Equal(StatusPix.Pendente, repo.D["v1"].Status);
    }

    [Fact]
    public async Task Receptor_http_responde_200_ao_POST_e_atualiza_a_cobranca()
    {
        var (pix, repo, gw) = Montar();
        gw.Status = StatusPix.Aprovado;
        var porta = PortaLivre();
        using var receptor = new ReceptorWebhookPix(pix, porta);
        Assert.True(receptor.Iniciar());
        using var http = new HttpClient();
        var url = $"http://127.0.0.1:{porta}{ReceptorWebhookPix.Rota}";

        var r = await http.PostAsync(url, new StringContent("{\"type\":\"payment\",\"data\":{\"id\":\"123456\"}}"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        for (var i = 0; i < 50 && repo.D["v1"].Status != StatusPix.Aprovado; i++) await Task.Delay(50);
        Assert.Equal(StatusPix.Aprovado, repo.D["v1"].Status);

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(url)).StatusCode);                         // teste de alcance
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"http://127.0.0.1:{porta}/outra")).StatusCode);
    }

    [Fact]
    public void Receptor_nao_estoura_se_a_porta_esta_ocupada()
    {
        var (pix, _, _) = Montar();
        var porta = PortaLivre();
        using var primeiro = new ReceptorWebhookPix(pix, porta);
        Assert.True(primeiro.Iniciar());
        using var segundo = new ReceptorWebhookPix(pix, porta);
        Assert.False(segundo.Iniciar());
    }
}
