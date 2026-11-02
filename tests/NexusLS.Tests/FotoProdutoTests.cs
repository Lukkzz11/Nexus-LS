using System.Net;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class FotoProdutoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-fotos-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private sealed class ProcessadorFalso : IProcessadorImagem
    {
        public int UltimoLimite { get; private set; }
        public byte[] ReduzirParaJpeg(byte[] original, int maiorLado)
        {
            UltimoLimite = maiorLado;
            if (original.Length < 3 || original[0] != 0xFF) throw new RegraDeNegocioException("Esse arquivo não parece ser uma imagem.");
            return original.Take(original.Length - 1).ToArray(); // "recomprime": fica menor
        }
    }

    private sealed class EnviadorFalso : IEnviadorFoto
    {
        public List<string> Nomes { get; } = new();
        public Exception? Erro { get; set; }
        public Task<string> EnviarAsync(string nome, byte[] jpeg, CancellationToken ct = default)
        {
            if (Erro is not null) throw Erro;
            Nomes.Add(nome);
            return Task.FromResult("https://exemplo/" + Uri.EscapeDataString(nome));
        }
    }

    private int _seq;
    private static readonly byte[] Foto = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };

    private async Task<int> NovoProduto(string nome = "Camisa")
    {
        var p = await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto(nome, "789" + (++_seq), 5000, 2000, 1), 3);
        return p.Id;
    }

    private FotoProdutoService Servico(IEnviadorFoto? env = null, ProcessadorFalso? proc = null) =>
        new(_banco.Fabrica, proc ?? new ProcessadorFalso(), _pasta, env);

    private async Task<Produto> Ler(int id) => (await new ProdutoService(_banco.Fabrica).ObterAsync(id))!;

    [Fact]
    public async Task Define_a_foto_guarda_no_disco_e_limita_o_tamanho()
    {
        var proc = new ProcessadorFalso();
        var s = Servico(proc: proc);
        var id = await NovoProduto();
        await s.DefinirAsync(id, Foto);

        var p = await Ler(id);
        Assert.NotNull(p.FotoArquivo);
        Assert.Null(p.FotoUrl);
        Assert.Equal(Foto.Length - 1, new FileInfo(s.CaminhoDe(p.FotoArquivo)!).Length);
        Assert.Equal(FotoProdutoService.MaiorLadoProduto, proc.UltimoLimite);
    }

    [Fact]
    public async Task Trocar_apaga_o_arquivo_e_o_endereco_antigos_e_remover_limpa_tudo()
    {
        var env = new EnviadorFalso();
        var s = Servico(env);
        var id = await NovoProduto();
        await s.DefinirAsync(id, Foto);
        await s.EnviarPendentesAsync();
        var antes = await Ler(id);
        Assert.NotNull(antes.FotoUrl);

        await Task.Delay(5);
        await s.DefinirAsync(id, Foto);
        var depois = await Ler(id);
        Assert.NotEqual(antes.FotoArquivo, depois.FotoArquivo);
        Assert.Null(depois.FotoUrl);                                    // a foto nova ainda precisa subir
        Assert.Null(s.CaminhoDe(antes.FotoArquivo));                    // o arquivo velho foi apagado

        await s.RemoverAsync(id);
        var removido = await Ler(id);
        Assert.Null(removido.FotoArquivo);
        Assert.Null(removido.FotoUrl);
        Assert.Empty(Directory.GetFiles(_pasta));
    }

    [Fact]
    public async Task Recusa_arquivo_que_nao_e_imagem_vazio_e_produto_inexistente()
    {
        var s = Servico();
        var id = await NovoProduto();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.DefinirAsync(id, new byte[] { 1, 2, 3, 4 }));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.DefinirAsync(id, Array.Empty<byte>()));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.DefinirAsync(999, Foto));
        Assert.Null((await Ler(id)).FotoArquivo);
    }

    [Fact]
    public async Task Envio_grava_o_endereco_so_das_pendentes_e_uma_falha_de_rede_para_tudo()
    {
        var env = new EnviadorFalso();
        var s = Servico(env);
        var a = await NovoProduto("A");
        var b = await NovoProduto("B");
        await s.DefinirAsync(a, Foto);
        await s.DefinirAsync(b, Foto);
        Assert.Equal(2, await s.ContarPendentesAsync());

        env.Erro = new FalhaDestinoException("Sem conexão com a internet.");
        var falhou = await s.EnviarPendentesAsync();
        Assert.Equal(0, falhou.Enviadas);
        Assert.Equal(1, falhou.Falhas);                                  // parou na primeira
        Assert.Equal("Sem conexão com a internet.", falhou.PrimeiroErro);

        env.Erro = null;
        var ok = await s.EnviarPendentesAsync();
        Assert.Equal(2, ok.Enviadas);
        Assert.StartsWith("https://exemplo/produtos%2F", (await Ler(a)).FotoUrl);
        Assert.Equal(0, await s.ContarPendentesAsync());
        Assert.Equal(0, (await s.EnviarPendentesAsync()).Enviadas);      // nada novo: não reenvia
    }

    [Fact]
    public async Task Foto_sumida_do_disco_conta_como_falha_sem_travar_as_outras()
    {
        var env = new EnviadorFalso();
        var s = Servico(env);
        var a = await NovoProduto("A");
        var b = await NovoProduto("B");
        await s.DefinirAsync(a, Foto);
        await s.DefinirAsync(b, Foto);
        File.Delete(s.CaminhoDe((await Ler(a)).FotoArquivo)!);

        var r = await s.EnviarPendentesAsync();
        Assert.Equal(1, r.Enviadas);
        Assert.Equal(1, r.Falhas);
        Assert.Contains("não está mais no disco", r.PrimeiroErro);
    }

    [Fact]
    public void Foto_entra_no_hash_e_na_mascara_so_quando_existe()
    {
        var p = new Produto { Nome = "Camisa", PrecoVendaCentavos = 5000 };
        var semFoto = RegrasSincronizacao.Hash(RegrasSincronizacao.Montar(p));
        p.FotoUrl = "https://exemplo/f.jpg";
        var comFoto = RegrasSincronizacao.Hash(RegrasSincronizacao.Montar(p));
        Assert.NotEqual(semFoto, comFoto);

        var item = new ItemPlano(RegrasSincronizacao.Montar(p), comFoto);
        var json = FirestoreJson.Produto("proj", new OperacaoCatalogo(item, TipoOperacao.Gravar), DateTime.Now).ToJsonString();
        Assert.Contains("\"fotoUrl\"", json);

        p.FotoUrl = null;
        var item2 = new ItemPlano(RegrasSincronizacao.Montar(p), semFoto);
        var json2 = FirestoreJson.Produto("proj", new OperacaoCatalogo(item2, TipoOperacao.Gravar), DateTime.Now).ToJsonString();
        Assert.DoesNotContain("fotoUrl", json2);   // sem foto no Nexus, a da vitrine online fica intocada
    }

    [Fact]
    public async Task Storage_envia_o_jpeg_com_o_token_e_monta_o_endereco_de_download()
    {
        var servidor = new ServidorFalso().Responde(HttpStatusCode.OK, "{\"name\":\"produtos/a b.jpg\",\"downloadTokens\":\"tok-1,tok-2\"}");
        var env = new StorageRestEnviador(new HttpClient(servidor), "loja.firebasestorage.app", new TokenFixo());
        var url = await env.EnviarAsync("produtos/a b.jpg", Foto);

        var pedido = Assert.Single(servidor.Pedidos);
        Assert.Equal("Firebase token-1", pedido.Autorizacao);
        Assert.Contains("/b/loja.firebasestorage.app/o?uploadType=media&name=produtos", pedido.Url);
        Assert.Equal("https://firebasestorage.googleapis.com/v0/b/loja.firebasestorage.app/o/produtos%2Fa%20b.jpg?alt=media&token=tok-1", url);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "Sem permissão", GravidadeFalha.Interrompe)]
    [InlineData(HttpStatusCode.NotFound, "Storage não foi encontrado", GravidadeFalha.Interrompe)]
    [InlineData(HttpStatusCode.BadRequest, "recusou a foto", GravidadeFalha.DadosRecusados)]
    public async Task Storage_traduz_os_erros_para_portugues(HttpStatusCode status, string trecho, GravidadeFalha gravidade)
    {
        var servidor = new ServidorFalso().Responde(status, "{}");
        var env = new StorageRestEnviador(new HttpClient(servidor), "b", new TokenFixo());
        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => env.EnviarAsync("x.jpg", Foto));
        Assert.Contains(trecho, ex.Message);
        Assert.Equal(gravidade, ex.Gravidade);
    }

    [Fact]
    public async Task Storage_renova_o_token_uma_vez_e_trata_falta_de_internet()
    {
        var token = new TokenFixo();
        var servidor = new ServidorFalso().Responde(HttpStatusCode.Unauthorized).Responde(HttpStatusCode.OK, "{\"name\":\"x.jpg\",\"downloadTokens\":\"t\"}");
        var env = new StorageRestEnviador(new HttpClient(servidor), "b", token);
        await env.EnviarAsync("x.jpg", Foto);
        Assert.Equal(1, token.Invalidacoes);

        var semRede = new StorageRestEnviador(new HttpClient(new ServidorFalso().Falha(new HttpRequestException("x"))), "b", new TokenFixo());
        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => semRede.EnviarAsync("x.jpg", Foto));
        Assert.Contains("Sem conexão", ex.Message);
    }

    [Fact]
    public void Bucket_padrao_e_o_do_projeto_e_pode_ser_trocado()
    {
        var cfg = new ConfiguracaoFirebase { ProjectId = "minha-loja-1a2b3" };
        Assert.Equal("minha-loja-1a2b3.firebasestorage.app", cfg.BucketEfetivo);
        cfg.Bucket = " minha-loja-1a2b3.appspot.com ";
        Assert.Equal("minha-loja-1a2b3.appspot.com", cfg.BucketEfetivo);
    }
}
