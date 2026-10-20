using System.Net;
using System.Security.Cryptography;
using System.Text;
using NexusLS.Core.Atualizacao;

namespace NexusLS.Tests;

public sealed class AtualizadorTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-atualizador-" + Guid.NewGuid().ToString("N"));

    public AtualizadorTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    // ---------- versões ----------

    [Theory]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("V2.0", 2, 0, 0)]
    [InlineData("3", 3, 0, 0)]
    [InlineData("1.4.2+abc123", 1, 4, 2)]
    [InlineData("1.4.2-beta", 1, 4, 2)]
    [InlineData("1.4.2.0", 1, 4, 2)]
    public void Le_varios_formatos_de_versao(string texto, int maior, int menor, int correcao)
    {
        Assert.True(VersaoPrograma.TryParse(texto, out var v));
        Assert.Equal(new VersaoPrograma(maior, menor, correcao), v);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("versão nova")]
    public void Texto_que_nao_e_versao_e_recusado(string texto)
    {
        Assert.False(VersaoPrograma.TryParse(texto, out _));
        Assert.Throws<FormatException>(() => VersaoPrograma.Parse(texto));
    }

    [Fact]
    public void Compara_por_numero_e_nao_por_texto()
    {
        Assert.True(VersaoPrograma.Parse("1.10.0") > VersaoPrograma.Parse("1.9.9"));
        Assert.True(VersaoPrograma.Parse("2.0.0") > VersaoPrograma.Parse("1.99.99"));
        Assert.True(VersaoPrograma.Parse("1.0.1") > VersaoPrograma.Parse("1.0.0"));
        Assert.Equal(VersaoPrograma.Parse("v1.2"), VersaoPrograma.Parse("1.2.0"));
        Assert.Equal("1.2.0", VersaoPrograma.Parse("v1.2").ToString());
    }

    // ---------- fonte do GitHub (respostas simuladas) ----------

    private sealed class ManipuladorFalso : HttpMessageHandler
    {
        public Dictionary<string, (HttpStatusCode Codigo, byte[] Corpo)> Respostas { get; } = new();
        public List<string> Pedidos { get; } = new();

        public void Json(string url, string json, HttpStatusCode codigo = HttpStatusCode.OK) => Respostas[url] = (codigo, Encoding.UTF8.GetBytes(json));
        public void Bytes(string url, byte[] bytes) => Respostas[url] = (HttpStatusCode.OK, bytes);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Pedidos.Add(url);
            if (!Respostas.TryGetValue(url, out var r)) throw new HttpRequestException("sem rota: " + url);
            return Task.FromResult(new HttpResponseMessage(r.Codigo) { Content = new ByteArrayContent(r.Corpo) });
        }
    }

    private const string Api = "https://api.github.com/repos/Loja/Nexus-Releases/releases/latest";
    private const string UrlExe = "https://github.com/Loja/Nexus-Releases/releases/download/v1.3.0/NexusLS.exe";
    private const string UrlHash = "https://github.com/Loja/Nexus-Releases/releases/download/v1.3.0/NexusLS.exe.sha256";

    private static string Release(string tag, string assets, bool draft = false, bool previa = false) =>
        $$"""{ "tag_name": "{{tag}}", "draft": {{draft.ToString().ToLower()}}, "prerelease": {{previa.ToString().ToLower()}}, "body": "- Contas e saldos\n- Atualizador", "published_at": "2026-10-20T17:00:00Z", "assets": [ {{assets}} ] }""";

    private static string AssetExe(string url = UrlExe, long tamanho = 1234, string? digest = null) =>
        $$"""{ "name": "NexusLS.exe", "size": {{tamanho}}, "browser_download_url": "{{url}}"{{(digest is null ? "" : $", \"digest\": \"{digest}\"")}} }""";

    private const string AssetHash = $$"""{ "name": "NexusLS.exe.sha256", "size": 70, "browser_download_url": "{{UrlHash}}" }""";

    private static FonteGitHub Fonte(ManipuladorFalso falso) => new(new HttpClient(falso), "Loja/Nexus-Releases");

    [Fact]
    public async Task Fonte_le_versao_notas_tamanho_e_o_hash_do_proprio_github()
    {
        var falso = new ManipuladorFalso();
        falso.Json(Api, Release("v1.3.0", AssetExe(tamanho: 5000, digest: "sha256:ABCDEF0123")));

        var info = await Fonte(falso).UltimaAsync();

        Assert.NotNull(info);
        Assert.Equal(new VersaoPrograma(1, 3, 0), info!.Versao);
        Assert.Equal(5000, info.Tamanho);
        Assert.Equal("abcdef0123", info.Sha256);
        Assert.Contains("Atualizador", info.Notas);
        Assert.Single(falso.Pedidos); // não precisou baixar o arquivo de hash
    }

    [Fact]
    public async Task Fonte_baixa_o_arquivo_de_hash_quando_o_github_nao_informa()
    {
        var falso = new ManipuladorFalso();
        falso.Json(Api, Release("v1.3.0", AssetExe() + "," + AssetHash));
        falso.Bytes(UrlHash, Encoding.UTF8.GetBytes("ABCDEF0123  NexusLS.exe\n"));

        var info = await Fonte(falso).UltimaAsync();

        Assert.Equal("abcdef0123", info!.Sha256);
    }

    [Fact]
    public async Task Fonte_sem_nenhuma_versao_publicada_devolve_nulo()
    {
        var falso = new ManipuladorFalso();
        falso.Json(Api, "{}", HttpStatusCode.NotFound);

        Assert.Null(await Fonte(falso).UltimaAsync());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Rascunho_e_versao_de_teste_nao_sao_oferecidos(bool rascunho, bool previa)
    {
        var falso = new ManipuladorFalso();
        falso.Json(Api, Release("v9.0.0", AssetExe(digest: "sha256:aa"), rascunho, previa));

        Assert.Null(await Fonte(falso).UltimaAsync());
    }

    [Fact]
    public async Task Release_sem_o_arquivo_do_programa_ou_com_tag_estranha_e_ignorado()
    {
        var falso = new ManipuladorFalso();
        falso.Json(Api, Release("v1.3.0", "{ \"name\": \"outro.zip\", \"size\": 1, \"browser_download_url\": \"" + UrlExe + "\" }"));
        Assert.Null(await Fonte(falso).UltimaAsync());

        falso.Json(Api, Release("lancamento-de-natal", AssetExe(digest: "sha256:aa")));
        Assert.Null(await Fonte(falso).UltimaAsync());
    }

    [Theory]
    [InlineData("http://github.com/Loja/Nexus-Releases/releases/download/v1.3.0/NexusLS.exe")]   // sem HTTPS
    [InlineData("https://site-malicioso.example/NexusLS.exe")]                                      // fora do GitHub
    [InlineData("https://github.com.site-malicioso.example/NexusLS.exe")]                           // domínio que só parece
    public async Task Endereco_fora_do_github_ou_sem_https_e_recusado(string url)
    {
        var falso = new ManipuladorFalso();
        falso.Json(Api, Release("v1.3.0", AssetExe(url, digest: "sha256:aa")));

        await Assert.ThrowsAsync<InvalidDataException>(() => Fonte(falso).UltimaAsync());
    }

    [Fact]
    public void Enderecos_do_github_aceitos()
    {
        Assert.True(FonteGitHub.EnderecoDoGitHub(new Uri("https://github.com/a/b/releases/download/v1/x.exe")));
        Assert.True(FonteGitHub.EnderecoDoGitHub(new Uri("https://objects.githubusercontent.com/abc")));
        Assert.True(FonteGitHub.EnderecoDoGitHub(new Uri("https://release-assets.githubusercontent.com/abc")));
        Assert.False(FonteGitHub.EnderecoDoGitHub(new Uri("https://evilgithub.com/x")));
    }

    // ---------- verificar ----------

    private sealed class FonteFalsa : IFonteAtualizacao
    {
        public InfoVersao? Ultima { get; set; }
        public Exception? Erro { get; set; }
        public byte[] Conteudo { get; set; } = Array.Empty<byte>();

        public Task<InfoVersao?> UltimaAsync(CancellationToken ct = default) =>
            Erro is not null ? throw Erro : Task.FromResult(Ultima);

        public async Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default)
        {
            // entrega em 4 pedaços, como uma conexão de verdade
            var passo = Math.Max(1, Conteudo.Length / 4);
            long total = 0;
            for (var i = 0; i < Conteudo.Length; i += passo)
            {
                var n = Math.Min(passo, Conteudo.Length - i);
                await destino.WriteAsync(Conteudo.AsMemory(i, n), ct);
                total += n;
                progresso?.Report(total);
            }
        }
    }

    private static byte[] Programa(int tamanho = 4096, byte semente = 7)
    {
        var bytes = new byte[tamanho];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * semente);
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static InfoVersao Info(string versao, byte[] conteudo, string? sha = "auto", long? tamanho = null) =>
        new(VersaoPrograma.Parse(versao), "notas", new Uri(UrlExe), tamanho ?? conteudo.Length, sha == "auto" ? Sha(conteudo) : sha, null);

    private AtualizadorService Servico(FonteFalsa fonte, string atual = "1.0.0") =>
        new(fonte, Path.Combine(_pasta, "baixados"), VersaoPrograma.Parse(atual));

    [Fact]
    public async Task Versao_maior_e_oferecida_e_igual_ou_menor_nao()
    {
        var c = Programa();
        var fonte = new FonteFalsa { Ultima = Info("1.3.0", c) };

        var nova = await Servico(fonte, "1.2.9").VerificarAsync();
        Assert.Equal(EstadoVerificacao.Disponivel, nova.Estado);
        Assert.Equal(new VersaoPrograma(1, 3, 0), nova.Nova!.Versao);

        Assert.Equal(EstadoVerificacao.SemNovidade, (await Servico(fonte, "1.3.0").VerificarAsync()).Estado);
        Assert.Equal(EstadoVerificacao.SemNovidade, (await Servico(fonte, "2.0.0").VerificarAsync()).Estado);
        fonte.Ultima = null;
        Assert.Equal(EstadoVerificacao.SemNovidade, (await Servico(fonte).VerificarAsync()).Estado);
    }

    [Fact]
    public async Task Sem_internet_nao_estoura_erro_so_avisa()
    {
        var fonte = new FonteFalsa { Erro = new HttpRequestException("sem rede") };

        var r = await Servico(fonte).VerificarAsync();

        Assert.Equal(EstadoVerificacao.SemConexao, r.Estado);
        Assert.Null(r.Nova);
        Assert.Contains("Não foi possível consultar", r.Mensagem);
    }

    // ---------- baixar ----------

    [Fact]
    public async Task Baixa_confere_e_devolve_o_arquivo_com_progresso_ate_100_por_cento()
    {
        var c = Programa(8000);
        var servico = Servico(new FonteFalsa { Conteudo = c });
        var passos = new List<double>();

        var caminho = await servico.BaixarAsync(Info("1.3.0", c), new Progress<double>(passos.Add));

        Assert.True(File.Exists(caminho));
        Assert.Equal(c, await File.ReadAllBytesAsync(caminho));
        Assert.False(File.Exists(caminho + ".parte"));
        await Task.Delay(50); // o Progress<T> entrega na fila da tela
        Assert.Equal(1.0, passos.Max());
    }

    [Fact]
    public async Task Arquivo_alterado_e_descartado_e_nada_fica_na_pasta()
    {
        var verdadeiro = Programa(8000, 7);
        var adulterado = Programa(8000, 9); // mesmo tamanho, conteúdo diferente
        var fonte = new FonteFalsa { Conteudo = adulterado };
        var servico = Servico(fonte);

        var erro = await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() => servico.BaixarAsync(Info("1.3.0", verdadeiro)));

        Assert.Contains("não confere", erro.Message);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_pasta, "baixados")));
    }

    [Fact]
    public async Task Arquivo_incompleto_e_descartado()
    {
        var c = Programa(8000);
        var fonte = new FonteFalsa { Conteudo = c[..6000] };

        var erro = await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() => Servico(fonte).BaixarAsync(Info("1.3.0", c)));

        Assert.Contains("incompleto", erro.Message);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_pasta, "baixados")));
    }

    [Fact]
    public async Task Versao_sem_hash_nao_e_instalada()
    {
        var c = Programa();
        var erro = await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() =>
            Servico(new FonteFalsa { Conteudo = c }).BaixarAsync(Info("1.3.0", c, sha: null)));

        Assert.Contains("SHA-256", erro.Message);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_um_programa_e_descartado_mesmo_com_hash_certo()
    {
        var lixo = Encoding.UTF8.GetBytes(new string('x', 4096)); // não começa com MZ
        var erro = await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() =>
            Servico(new FonteFalsa { Conteudo = lixo }).BaixarAsync(Info("1.3.0", lixo)));

        Assert.Contains("não é um programa", erro.Message);
    }

    [Fact]
    public async Task Cancelar_o_download_apaga_o_pedaco()
    {
        var c = Programa(8000);
        using var cancelar = new CancellationTokenSource();
        cancelar.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Servico(new FonteFalsa { Conteudo = c }).BaixarAsync(Info("1.3.0", c), null, cancelar.Token));

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_pasta, "baixados")));
    }

    // ---------- trocar o executável ----------

    [Fact]
    public void Troca_deixa_o_antigo_ao_lado_e_o_novo_no_lugar_e_depois_limpa()
    {
        var exe = Path.Combine(_pasta, "NexusLS.exe");
        var novo = Path.Combine(_pasta, "novo.exe");
        File.WriteAllBytes(exe, Programa(2000, 3));
        File.WriteAllBytes(novo, Programa(3000, 5));

        AtualizadorService.TrocarExecutavel(exe, novo);

        Assert.Equal(3000, new FileInfo(exe).Length);                                  // o novo ocupa o nome do programa
        Assert.Equal(2000, new FileInfo(exe + AtualizadorService.SufixoDoAntigo).Length); // o antigo sobrou ao lado
        Assert.False(File.Exists(novo));

        AtualizadorService.LimparRestos(exe);
        Assert.False(File.Exists(exe + AtualizadorService.SufixoDoAntigo));
        Assert.True(File.Exists(exe));
    }

    [Fact]
    public void Troca_sem_o_arquivo_novo_nao_mexe_em_nada()
    {
        var exe = Path.Combine(_pasta, "NexusLS.exe");
        File.WriteAllBytes(exe, Programa(2000));

        Assert.Throws<RegraDeAtualizacaoException>(() => AtualizadorService.TrocarExecutavel(exe, Path.Combine(_pasta, "nao-existe.exe")));

        Assert.Equal(2000, new FileInfo(exe).Length);
        Assert.False(File.Exists(exe + AtualizadorService.SufixoDoAntigo));
    }

    [Fact]
    public void Troca_substitui_um_antigo_que_sobrou_de_uma_atualizacao_anterior()
    {
        var exe = Path.Combine(_pasta, "NexusLS.exe");
        var novo = Path.Combine(_pasta, "novo.exe");
        File.WriteAllBytes(exe, Programa(2000));
        File.WriteAllBytes(exe + AtualizadorService.SufixoDoAntigo, Programa(500));
        File.WriteAllBytes(novo, Programa(3000));

        AtualizadorService.TrocarExecutavel(exe, novo);

        Assert.Equal(2000, new FileInfo(exe + AtualizadorService.SufixoDoAntigo).Length);
        Assert.Equal(3000, new FileInfo(exe).Length);
    }
}
