using System.Net;
using System.Text.Json.Nodes;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Vitrine;

namespace NexusLS.Tests;

public sealed class VitrineTests : IDisposable
{
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "vitrine-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        if (File.Exists(_arquivo)) File.Delete(_arquivo);
    }

    [Fact]
    public void Normalizar_arruma_cores_textos_e_numeros()
    {
        var c = new ConfiguracaoVitrine
        {
            Cores = new() { ["fundo"] = " FFF ", ["botao"] = "#1E40AF", ["titulo"] = "", ["inexistente"] = "zzz" },
            TextoAnuncio = "  Frete \t grátis\n hoje ",
            Whatsapp = "+55 (81) 99999-0000",
            Instagram = "@lojaboa",
            Modelo = "nao-existe"
        };
        c.Normalizar();
        Assert.Equal("#ffffff", c.Cores["fundo"]);
        Assert.Equal("#1e40af", c.Cores["botao"]);
        Assert.False(c.Cores.ContainsKey("titulo"));
        Assert.False(c.Cores.ContainsKey("inexistente"));
        Assert.Equal("Frete grátis hoje", c.TextoAnuncio);
        Assert.Equal("5581999990000", c.Whatsapp);
        Assert.Equal("lojaboa", c.Instagram);
        Assert.Equal("padrao", c.Modelo);
    }

    [Theory]
    [InlineData("cor", "A cor \"Fundo geral do catálogo\" é inválida")]
    [InlineData("whats", "WhatsApp deve ter")]
    [InlineData("email", "falta o @")]
    [InlineData("atacado", "entre 1 e 1000")]
    [InlineData("novidades", "de 1 a 24")]
    public void Normalizar_recusa_dados_invalidos_dizendo_o_que_corrigir(string caso, string trecho)
    {
        var c = new ConfiguracaoVitrine();
        switch (caso)
        {
            case "cor": c.Cores["fundo"] = "azul"; break;
            case "whats": c.Whatsapp = "123"; break;
            case "email": c.Email = "loja.com"; break;
            case "atacado": c.AtacadoMinPecas = 0; break;
            case "novidades": c.NovidadesQtd = 99; break;
        }
        var ex = Assert.Throws<RegraDeNegocioException>(c.Normalizar);
        Assert.Contains(trecho, ex.Message);
    }

    [Fact]
    public void Contraste_avisa_so_quando_as_duas_cores_foram_escolhidas_e_a_leitura_e_ruim()
    {
        Assert.Equal(21, ConfiguracaoVitrine.Contraste("#000000", "#ffffff"), 1);
        var c = new ConfiguracaoVitrine { Cores = new() { ["titulo"] = "#cccccc", ["fundo"] = "#ffffff", ["botao"] = "#1e40af" } };
        var avisos = c.AvisosDeContraste();
        Assert.Single(avisos);
        Assert.Contains("Título sobre o fundo", avisos[0]);
        Assert.Empty(new ConfiguracaoVitrine { Cores = new() { ["titulo"] = "#cccccc" } }.AvisosDeContraste());
    }

    [Fact]
    public void Store_grava_le_e_perdoa_arquivo_estragado()
    {
        var store = new VitrineStore(_arquivo);
        Assert.Equal("padrao", store.Carregar().Modelo);                 // sem arquivo: padrão
        var c = new ConfiguracaoVitrine { Modelo = "elegante", LojaNome = "Life Surf" };
        c.Cores["botao"] = "#abc";
        store.Salvar(c);
        var lido = store.Carregar();
        Assert.Equal("elegante", lido.Modelo);
        Assert.Equal("#aabbcc", lido.Cores["botao"]);
        Assert.Equal(2, lido.Banners.Count);

        File.WriteAllText(_arquivo, "{ isto nao e json");
        Assert.Equal("padrao", store.Carregar().Modelo);
    }

    [Fact]
    public void Documento_tem_os_campos_da_vitrine_e_omite_texto_vazio()
    {
        var c = new ConfiguracaoVitrine { Modelo = "minimalista", LojaNome = "Life", AtacadoMinPecas = 12 };
        c.Cores["fundo"] = "#ffffff";
        c.Normalizar();
        var f = VitrineJson.Campos(c, new DateTime(2026, 11, 2, 10, 0, 0));
        Assert.Equal("minimalista", f["modelo"]!["stringValue"]!.GetValue<string>());
        Assert.Equal("12", f["atacado"]!["mapValue"]!["fields"]!["minPecas"]!["integerValue"]!.GetValue<string>());
        var loja = f["loja"]!["mapValue"]!["fields"]!.AsObject();
        Assert.True(loja.ContainsKey("nome"));
        Assert.False(loja.ContainsKey("telefone"));
        Assert.Equal(2, f["banners"]!["arrayValue"]!["values"]!.AsArray().Count);
        Assert.Equal("nexus-ls", f["origem"]!["stringValue"]!.GetValue<string>());
    }

    [Fact]
    public async Task Publicar_faz_patch_em_meta_vitrine_e_traduz_erros()
    {
        var servidor = new ServidorFalso().Responde(HttpStatusCode.OK, "{}");
        var pub = new PublicadorVitrineRest(new HttpClient(servidor), "loja-1a2b3", new TokenFixo());
        await pub.PublicarAsync(new ConfiguracaoVitrine { LojaNome = "Life" });
        var p = Assert.Single(servidor.Pedidos);
        Assert.Equal("PATCH", p.Metodo.Method);
        Assert.Contains("/projects/loja-1a2b3/databases/(default)/documents/meta/vitrine", p.Url);
        Assert.Equal("Bearer token-1", p.Autorizacao);
        Assert.Equal("Life", JsonNode.Parse(p.Corpo)!["fields"]!["loja"]!["mapValue"]!["fields"]!["nome"]!["stringValue"]!.GetValue<string>());

        var negado = new PublicadorVitrineRest(new HttpClient(new ServidorFalso().Responde(HttpStatusCode.Forbidden)), "loja-1a2b3", new TokenFixo());
        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => negado.PublicarAsync(new ConfiguracaoVitrine()));
        Assert.Contains("Sem permissão", ex.Message);

        var semRede = new PublicadorVitrineRest(new HttpClient(new ServidorFalso().Falha(new HttpRequestException("x"))), "loja-1a2b3", new TokenFixo());
        Assert.Contains("Sem conexão", (await Assert.ThrowsAsync<FalhaDestinoException>(() => semRede.PublicarAsync(new ConfiguracaoVitrine()))).Message);
    }

    [Fact]
    public async Task Publicar_nao_envia_configuracao_invalida()
    {
        var servidor = new ServidorFalso();
        var pub = new PublicadorVitrineRest(new HttpClient(servidor), "loja-1a2b3", new TokenFixo());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => pub.PublicarAsync(new ConfiguracaoVitrine { AtacadoMinPecas = 0 }));
        Assert.Empty(servidor.Pedidos);
    }
}
