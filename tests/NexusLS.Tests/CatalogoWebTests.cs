using System.Text.Json.Nodes;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Vitrine;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>A ponte de dados do editor/catálogo em JSX: estoque no formato do catálogo, imagens locais e publicação.</summary>
public sealed class CatalogoWebTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-vitrine-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private sealed class EnviadorFalso : IEnviadorFoto
    {
        public List<string> Nomes { get; } = new();
        public Task<string> EnviarAsync(string nome, byte[] jpeg, CancellationToken ct = default)
        {
            Nomes.Add(nome);
            return Task.FromResult("https://publico/" + nome);
        }
    }

    private sealed class PublicadorFalso : IPublicadorVitrineCompleta, IPublicadorAjustesProduto
    {
        public List<(string Id, bool Visivel, bool Destaque, long Promo)> Ajustes { get; } = new();
        public Task<bool> PublicarAjusteAsync(string id, bool visivel, bool destaque, long promo, CancellationToken ct = default)
        {
            Ajustes.Add((id, visivel, destaque, promo));
            return Task.FromResult(true);
        }
        public string? Json { get; private set; }
        public Exception? Erro { get; set; }
        public Task PublicarCompletaAsync(string json, CancellationToken ct = default)
        {
            if (Erro is not null) throw Erro;
            Json = json;
            return Task.CompletedTask;
        }
    }

    private ServicoCatalogoWeb Servico(EnviadorFalso? env = null, PublicadorFalso? pub = null) =>
        new(new ProdutoService(_banco.Fabrica), new ConfiguracaoService(_banco.Fabrica),
            new VitrineCompletaStore(Path.Combine(_pasta, "vitrine.json")), Path.Combine(_pasta, "img"), env, pub, null, new AjustesProdutoStore(Path.Combine(_pasta, "aj.json")));

    private const string PngPixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public async Task Carregar_devolve_estoque_em_reais_com_grade_e_sem_a_linha_sem_tamanho()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camisa", "7891", 5990, 2000, 1), 0);
        await produtos.AdicionarTamanhoAsync(p.Id, "M");

        var dados = await Servico().CarregarAsync();

        var item = Assert.Single(dados.Produtos)!.AsObject();
        Assert.Equal("Camisa", item["nome"]!.GetValue<string>());
        Assert.Equal(59.9, item["precoVarejo"]!.GetValue<double>(), 2);
        Assert.True(item["gradeTamanhos"]!.AsObject().ContainsKey("M"));
        Assert.False(item["gradeTamanhos"]!.AsObject().ContainsKey(Tamanhos.SemTamanho));
        Assert.Null(dados.VitrineJson);
    }

    [Fact]
    public void Salvar_imagem_guarda_no_disco_e_so_apaga_pelo_nome()
    {
        var s = Servico();
        var (url, path) = s.SalvarImagem(PngPixel);

        Assert.StartsWith("https://vitrine.nexus.local/", url);
        var arquivo = Path.Combine(_pasta, "img", Path.GetFileName(path));
        Assert.True(File.Exists(arquivo));

        s.ApagarImagem("../../" + path); // caminho vindo da página: só o nome conta
        Assert.False(File.Exists(arquivo));
    }

    [Fact]
    public void Salvar_imagem_recusa_o_que_nao_e_imagem()
    {
        Assert.Throws<RegraDeNegocioException>(() => Servico().SalvarImagem("data:text/plain;base64,AAAA"));
        Assert.Throws<RegraDeNegocioException>(() => Servico().SalvarImagem("lixo"));
    }

    [Fact]
    public async Task Publicar_sobe_as_imagens_locais_troca_os_enderecos_e_grava_no_firestore()
    {
        var env = new EnviadorFalso();
        var pub = new PublicadorFalso();
        var s = Servico(env, pub);
        var (url, _) = s.SalvarImagem(PngPixel);
        var vitrine = new JsonObject { ["banners"] = new JsonArray(new JsonObject { ["image"] = url }), ["loja"] = new JsonObject { ["logoUrl"] = url } };

        var (json, _, _) = await s.PublicarAsync(vitrine.ToJsonString());

        var resultado = JsonNode.Parse(json)!;
        var publica = resultado["banners"]![0]!["image"]!.GetValue<string>();
        Assert.StartsWith("https://publico/vitrine/", publica);
        Assert.Equal(publica, resultado["loja"]!["logoUrl"]!.GetValue<string>());
        Assert.Single(env.Nomes); // a mesma imagem usada duas vezes sobe uma vez só
        Assert.Equal(json, pub.Json);
    }

    [Fact]
    public async Task Publicar_com_falha_mantem_o_rascunho_local_com_as_imagens_locais()
    {
        var pub = new PublicadorFalso { Erro = new InvalidOperationException("sem rede") };
        var s = Servico(new EnviadorFalso(), pub);
        var (url, _) = s.SalvarImagem(PngPixel);
        var original = new JsonObject { ["loja"] = new JsonObject { ["logoUrl"] = url } }.ToJsonString();

        await Assert.ThrowsAsync<InvalidOperationException>(() => s.PublicarAsync(original));

        var dados = await s.CarregarAsync();
        Assert.Equal(url, JsonNode.Parse(dados.VitrineJson!)!["loja"]!["logoUrl"]!.GetValue<string>());
    }

    [Fact]
    public async Task Publicar_recusa_json_invalido_e_imagem_que_sumiu()
    {
        var s = Servico(new EnviadorFalso(), new PublicadorFalso());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.PublicarAsync("[1,2]"));
        var sumida = new JsonObject { ["x"] = "https://vitrine.nexus.local/nao-existe.jpg" }.ToJsonString();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.PublicarAsync(sumida));
    }

    [Fact]
    public async Task Ajustes_do_produto_aparecem_no_carregar_e_voltam_ao_padrao_sem_deixar_rastro()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camisa", "7891", 5990, 2000, 1), 0);
        var id = p.Guid.ToString("D");
        var s = Servico();

        await s.SalvarAjusteProdutoAsync(id, visivel: false, destaque: true, promocaoCentavos: 4990);
        var item = (await s.CarregarAsync()).Produtos[0]!.AsObject();
        Assert.False(item["ativoNoCatalogo"]!.GetValue<bool>());
        Assert.True(item["destaque"]!.GetValue<bool>());
        Assert.Equal(49.9, item["precoPromocional"]!.GetValue<double>(), 2);

        await s.SalvarAjusteProdutoAsync(id, true, false, 0);
        item = (await s.CarregarAsync()).Produtos[0]!.AsObject();
        Assert.True(item["ativoNoCatalogo"]!.GetValue<bool>());
        Assert.False(File.ReadAllText(Path.Combine(_pasta, "aj.json")).Contains(id));
    }

    [Fact]
    public async Task Promocao_precisa_ser_menor_que_o_varejo_e_produto_precisa_existir()
    {
        var p = await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "7891", 5990, 2000, 1), 0);
        var s = Servico();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAjusteProdutoAsync(p.Guid.ToString("D"), true, false, 5990));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAjusteProdutoAsync(p.Guid.ToString("D"), true, false, -1));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAjusteProdutoAsync(Guid.NewGuid().ToString("D"), true, false, 0));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.SalvarAjusteProdutoAsync("nao-e-guid", true, false, 0));
    }

    [Fact]
    public async Task Publicar_envia_os_ajustes_dos_produtos()
    {
        var p = await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "7891", 5990, 2000, 1), 0);
        var pub = new PublicadorFalso();
        var s = Servico(new EnviadorFalso(), pub);
        await s.SalvarAjusteProdutoAsync(p.Guid.ToString("D"), true, true, 4990);

        var (_, _, enviados) = await s.PublicarAsync("{}");

        Assert.Equal(1, enviados);
        Assert.Equal((p.Guid.ToString("D"), true, true, 4990L), pub.Ajustes.Single());
    }
}
