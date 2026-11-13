using NexusLS.Core.Pagamentos;

namespace NexusLS.Tests;

public class PixTests
{
    private sealed class Memoria : IRepositorioPix
    {
        public Dictionary<string, CobrancaPix> D { get; } = new();
        public Task<CobrancaPix?> PorChaveVendaAsync(string c, CancellationToken ct = default) => Task.FromResult(D.GetValueOrDefault(c));
        public Task<CobrancaPix?> PorIdExternoAsync(string g, string id, CancellationToken ct = default) =>
            Task.FromResult(D.Values.FirstOrDefault(x => x.Gateway == g && x.IdExterno == id));
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

public class PixPersistenciaTests : IDisposable
{
    private readonly NexusLS.Tests.Infra.BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private sealed class ProtetorFalso : NexusLS.Core.Sincronizacao.Firebase.IProtetorSegredo
    {
        public string Proteger(string t) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(t));
        public string? Desproteger(string t) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t));
    }

    [Fact]
    public async Task Cobranca_e_gravada_atualizada_sem_duplicar_e_ligada_a_venda()
    {
        var repo = new RepositorioPixSqlite(_banco.Fabrica);
        var c = new CobrancaPix("MercadoPago", "123", "venda-a", 5000, "0002...", "QUJD", StatusPix.Pendente, null);
        await repo.SalvarAsync(c);
        await repo.SalvarAsync(c with { Status = StatusPix.Aprovado });
        await repo.VincularVendaAsync("venda-a", 7);

        var lida = await repo.PorChaveVendaAsync("venda-a");
        Assert.Equal(StatusPix.Aprovado, lida!.Status);
        var historico = await repo.HistoricoAsync();
        Assert.Single(historico);
        Assert.Equal(7, historico[0].VendaId);
        Assert.Null(await repo.PorChaveVendaAsync("outra"));
    }

    [Fact]
    public void Token_nunca_fica_em_texto_claro_no_arquivo_e_vazio_mantem_o_anterior()
    {
        var caminho = Path.Combine(Path.GetTempPath(), "pag-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var store = new ConfiguracaoPagamentosStore(caminho, new ProtetorFalso());
            var config = store.Ler();
            config.MercadoPagoAtivo = true;
            store.DefinirToken(config, "APP_USR-segredo");
            store.Salvar(config);

            Assert.DoesNotContain("APP_USR-segredo", File.ReadAllText(caminho));
            Assert.Equal("APP_USR-segredo", store.Token());

            var outra = store.Ler();
            store.DefinirToken(outra, "   ");   // em branco: mantém
            Assert.Equal("APP_USR-segredo", store.Token());
            Assert.True(outra.TarifaMercadoPago().Ativo);
        }
        finally { if (File.Exists(caminho)) File.Delete(caminho); }
    }

    [Fact]
    public void Sem_token_o_mercado_pago_fica_fora_do_roteamento()
    {
        var c = new ConfiguracaoPagamentos { MercadoPagoAtivo = true };
        Assert.False(c.TarifaMercadoPago().Ativo);
    }
}

public class PixMotivoTests
{
    [Fact]
    public void Motivo_vem_do_corpo_do_mercado_pago_ou_da_dica_do_codigo()
    {
        Assert.Contains("invalid access token", MercadoPagoGateway.MotivoDaRecusa("{\"message\":\"invalid access token\",\"status\":401}", System.Net.HttpStatusCode.Unauthorized));
        Assert.Contains("Payer email forbidden", MercadoPagoGateway.MotivoDaRecusa("{\"message\":\"x\",\"cause\":[{\"description\":\"Payer email forbidden\"}]}", System.Net.HttpStatusCode.BadRequest));
        Assert.Contains("Access Token", MercadoPagoGateway.MotivoDaRecusa("", System.Net.HttpStatusCode.Unauthorized));
    }
}

public class MercadoPagoGatewayHttpTests
{
    private sealed class Falso : HttpMessageHandler
    {
        public HttpRequestMessage? Pedido; public string? Corpo;
        public System.Net.HttpStatusCode Codigo = System.Net.HttpStatusCode.Created; public string Resposta = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Pedido = r; Corpo = r.Content is null ? null : await r.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(Codigo) { Content = new StringContent(Resposta) };
        }
    }

    private static MercadoPagoGateway Gateway(Falso f) => new(new HttpClient(f), () => "TOKEN-X", () => "cliente@teste.com");

    [Fact]
    public async Task Cria_cobranca_com_o_corpo_certo_e_le_qr_code_e_status()
    {
        var f = new Falso { Resposta = "{\"id\":123456,\"status\":\"pending\",\"date_of_expiration\":\"2026-11-10T20:00:00.000-03:00\",\"point_of_interaction\":{\"transaction_data\":{\"qr_code\":\"0002012658\",\"qr_code_base64\":\"QUJD\"}}}" };
        var c = await Gateway(f).CriarAsync(3000, "Venda", "pdv-abc");

        Assert.Equal(HttpMethod.Post, f.Pedido!.Method);
        Assert.Equal("https://api.mercadopago.com/v1/payments", f.Pedido.RequestUri!.ToString());
        Assert.Equal("Bearer", f.Pedido.Headers.Authorization!.Scheme);
        Assert.Equal("TOKEN-X", f.Pedido.Headers.Authorization.Parameter);
        Assert.Equal("pdv-abc", f.Pedido.Headers.GetValues("X-Idempotency-Key").Single());
        using var corpo = System.Text.Json.JsonDocument.Parse(f.Corpo!);
        Assert.Equal(30m, corpo.RootElement.GetProperty("transaction_amount").GetDecimal());
        Assert.Equal("pix", corpo.RootElement.GetProperty("payment_method_id").GetString());
        Assert.Equal("cliente@teste.com", corpo.RootElement.GetProperty("payer").GetProperty("email").GetString());

        Assert.Equal("123456", c.IdExterno);
        Assert.Equal("0002012658", c.CopiaECola);
        Assert.Equal("QUJD", c.QrCodeBase64);
        Assert.Equal(StatusPix.Pendente, c.Status);
    }

    [Fact]
    public async Task Recusa_mostra_o_motivo_sem_vazar_o_token()
    {
        var f = new Falso { Codigo = System.Net.HttpStatusCode.Unauthorized, Resposta = "{\"message\":\"Unauthorized use of live credentials\",\"error\":\"unauthorized\",\"status\":401}" };
        var e = await Assert.ThrowsAsync<HttpRequestException>(() => Gateway(f).CriarAsync(3000, "Venda", "k"));
        Assert.Contains("401", e.Message);
        Assert.Contains("PRODUÇÃO", e.Message);
        Assert.DoesNotContain("TOKEN-X", e.Message);
    }

    [Fact]
    public async Task Consulta_aprovado_e_testar_conexao()
    {
        var f = new Falso { Codigo = System.Net.HttpStatusCode.OK, Resposta = "{\"id\":1,\"status\":\"approved\"}" };
        Assert.Equal(StatusPix.Aprovado, await Gateway(f).ConsultarAsync("1"));
        Assert.Equal("https://api.mercadopago.com/v1/payments/1", f.Pedido!.RequestUri!.ToString());
        Assert.True((await Gateway(f).TestarAsync()).Ok);
    }
}
