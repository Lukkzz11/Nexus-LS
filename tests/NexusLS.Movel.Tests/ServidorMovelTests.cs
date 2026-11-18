using System.Net;
using System.Text.Json;
using NexusLS.Tests.Infra;

namespace NexusLS.Movel.Tests;

public class ServidorMovelTests
{
    private const string Pin = "123456";

    private static Task<ServidorEmExecucao> Iniciar(BancoDeTeste banco) =>
        ServidorMovel.IniciarAsync(banco.Fabrica, Pin, porta: 0, url: "http://127.0.0.1:0");

    private static HttpClient CriarCliente(ServidorEmExecucao servidor, string? pin = Pin)
    {
        var cliente = new HttpClient
        {
            BaseAddress = new Uri(servidor.Endereco),
            Timeout = TimeSpan.FromSeconds(15)
        };

        if (pin is not null) cliente.DefaultRequestHeaders.Add("X-Pin", pin);
        return cliente;
    }

    [Fact]
    public async Task Sem_pin_a_api_responde_401()
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor, pin: null);

        var resposta = await cliente.GetAsync("/api/saude");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Pin_errado_responde_401()
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor, pin: "000000");

        var resposta = await cliente.GetAsync("/api/produto/1019");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Com_pin_a_saude_traz_o_nome_da_loja()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.DefinirLoja(banco, "LOJA TESTE MOVEL");

        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var json = await cliente.GetStringAsync("/api/saude");
        using var documento = JsonDocument.Parse(json);

        Assert.True(documento.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("LOJA TESTE MOVEL", documento.RootElement.GetProperty("loja").GetString());
    }

    [Fact]
    public async Task Produto_por_codigo_de_tamanho_devolve_o_tamanho_marcado()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "CAMISA GOLA CARECA", "1019", 5990, 3990, null,
            ("P", 1, 24), ("M", 2, 20), ("G", 3, 20));

        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var json = await cliente.GetStringAsync("/api/produto/10192");
        using var documento = JsonDocument.Parse(json);

        Assert.True(documento.RootElement.GetProperty("encontrado").GetBoolean());

        var produto = documento.RootElement.GetProperty("produto");
        Assert.Equal("CAMISA GOLA CARECA", produto.GetProperty("nome").GetString());
        Assert.Equal("M", produto.GetProperty("tamanhoSelecionado").GetString());
        Assert.Equal(5990, produto.GetProperty("precoVarejoCentavos").GetInt64());
    }

    [Fact]
    public async Task Produto_inexistente_devolve_sugestoes()
    {
        using var banco = new BancoDeTeste();
        DadosDeTeste.SemearProduto(banco, "BERMUDA CARGO", "1402", 7000, 0, null, ("M", 2, 5));

        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var json = await cliente.GetStringAsync("/api/produto/000000");
        using var documento = JsonDocument.Parse(json);

        Assert.False(documento.RootElement.GetProperty("encontrado").GetBoolean());
    }

    [Fact]
    public async Task Raiz_serve_a_tela_do_celular_sem_pin()
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor, pin: null);

        var html = await cliente.GetStringAsync("/");

        Assert.Contains("Nexus Móvel", html);
        Assert.Contains("manifest.webmanifest", html);
    }

    [Fact]
    public async Task Arquivos_da_tela_vem_embutidos_no_executavel()
    {
        using var banco = new BancoDeTeste();
        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor, pin: null);

        foreach (var caminho in new[] { "/estilo.css", "/app.js", "/vendor/zxing.min.js", "/manifest.webmanifest", "/icone-192.png" })
        {
            var resposta = await cliente.GetAsync(caminho);
            Assert.True(resposta.IsSuccessStatusCode, $"{caminho} respondeu {(int)resposta.StatusCode}");
            Assert.True(resposta.Content.Headers.ContentLength > 0, $"{caminho} veio vazio");
        }
    }

    [Fact]
    public async Task Foto_de_produto_sem_foto_responde_404()
    {
        using var banco = new BancoDeTeste();
        var id = DadosDeTeste.SemearProduto(banco, "SEM FOTO", "4001", 1000, 0, null, ("M", 2, 1));

        await using var servidor = await Iniciar(banco);
        using var cliente = CriarCliente(servidor);

        var resposta = await cliente.GetAsync($"/api/foto/{id}");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }
}
