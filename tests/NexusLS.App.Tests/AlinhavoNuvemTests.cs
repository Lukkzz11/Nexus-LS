using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using NexusLS.Core.Atualizacao;

namespace NexusLS.App.Tests;

public class AlinhavoNuvemTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "alinhavo-nuvem-" + Guid.NewGuid().ToString("N"));

    public AlinhavoNuvemTests() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); } catch { }
    }

    private sealed class FonteFalsa(byte[] conteudo) : IFonteAtualizacao
    {
        public Task<InfoVersao?> UltimaAsync(CancellationToken ct = default) => Task.FromResult<InfoVersao?>(null);

        public async Task BaixarAsync(InfoVersao versao, Stream destino, IProgress<long>? progresso, CancellationToken ct = default)
        {
            await destino.WriteAsync(conteudo, ct);
            progresso?.Report(conteudo.Length);
        }
    }

    private static byte[] CriarZip(string versao, bool comExe = true)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string nome, string texto) { using var w = new StreamWriter(zip.CreateEntry(nome).Open()); w.Write(texto); }
            if (comExe) Add("alinhavo.exe", "exe " + versao);
            Add("assets/manequim/m.glb", "glb");
            Add("versao.txt", versao + "\n");
        }
        return ms.ToArray();
    }

    private static InfoVersao Info(byte[] zip, string? sha = null) =>
        new(new VersaoPrograma(1, 2, 3), "", new Uri("https://github.com/x/Alinhavo.zip"), zip.Length,
            sha ?? Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(), null);

    [Theory]
    [InlineData("0.1.0", 0, 1, 0)]
    [InlineData("v1.2.3\n", 1, 2, 3)]
    [InlineData("﻿2.0.1\r\n", 2, 0, 1)]
    public void Versao_do_alinhavo_e_lida_do_texto(string texto, int maior, int menor, int correcao) =>
        Assert.Equal(new VersaoPrograma(maior, menor, correcao), FonteAlinhavo.LerVersao(texto));

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData(null)]
    public void Versao_invalida_vira_nulo(string? texto) => Assert.Null(FonteAlinhavo.LerVersao(texto));

    [Fact]
    public void So_oferece_atualizacao_quando_a_nuvem_e_maior()
    {
        Assert.True(InstalacaoAlinhavo.HaVersaoNova(null, new VersaoPrograma(0, 1, 0)));
        Assert.True(InstalacaoAlinhavo.HaVersaoNova(new VersaoPrograma(0, 1, 0), new VersaoPrograma(0, 2, 0)));
        Assert.False(InstalacaoAlinhavo.HaVersaoNova(new VersaoPrograma(0, 2, 0), new VersaoPrograma(0, 2, 0)));
        Assert.False(InstalacaoAlinhavo.HaVersaoNova(new VersaoPrograma(1, 0, 0), new VersaoPrograma(0, 9, 9)));
    }

    [Fact]
    public async Task Download_com_hash_certo_devolve_o_zip_e_avisa_o_progresso()
    {
        var zip = CriarZip("0.1.0");
        double ultimo = 0;
        var caminho = await InstalacaoAlinhavo.BaixarAsync(new FonteFalsa(zip), Info(zip), Path.Combine(_pasta, "tmp"), new Progress<double>(p => ultimo = p));
        Assert.True(File.Exists(caminho));
        Assert.Equal(zip, await File.ReadAllBytesAsync(caminho));
        await Task.Delay(50);
        Assert.Equal(1.0, ultimo);
    }

    [Fact]
    public async Task Download_com_hash_errado_e_descartado_com_mensagem_simples()
    {
        var zip = CriarZip("0.1.0");
        var ex = await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() =>
            InstalacaoAlinhavo.BaixarAsync(new FonteFalsa(zip), Info(zip, new string('0', 64)), Path.Combine(_pasta, "tmp")));
        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(Path.Combine(_pasta, "tmp", FonteAlinhavo.NomeDoZip)));
    }

    [Fact]
    public async Task Download_sem_hash_na_release_e_recusado()
    {
        var zip = CriarZip("0.1.0");
        var info = Info(zip) with { Sha256 = null };
        await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() =>
            InstalacaoAlinhavo.BaixarAsync(new FonteFalsa(zip), info, Path.Combine(_pasta, "tmp")));
    }

    [Fact]
    public async Task Download_incompleto_e_recusado()
    {
        var zip = CriarZip("0.1.0");
        var info = Info(zip) with { Tamanho = zip.Length + 10 };
        await Assert.ThrowsAsync<RegraDeAtualizacaoException>(() =>
            InstalacaoAlinhavo.BaixarAsync(new FonteFalsa(zip), info, Path.Combine(_pasta, "tmp")));
    }

    [Fact]
    public async Task Instalar_extrai_e_a_atualizacao_substitui_a_pasta_inteira()
    {
        var destino = Path.Combine(_pasta, "Programs", "Nexus", "Alinhavo");

        var v1 = Path.Combine(_pasta, "v1.zip");
        await File.WriteAllBytesAsync(v1, CriarZip("0.1.0"));
        InstalacaoAlinhavo.Instalar(v1, destino);
        Assert.Equal(new VersaoPrograma(0, 1, 0), InstalacaoAlinhavo.VersaoInstalada(destino));
        Assert.True(File.Exists(Path.Combine(destino, "assets", "manequim", "m.glb")));
        File.WriteAllText(Path.Combine(destino, "sobra-da-versao-antiga.txt"), "x");

        var v2 = Path.Combine(_pasta, "v2.zip");
        await File.WriteAllBytesAsync(v2, CriarZip("0.2.0"));
        InstalacaoAlinhavo.Instalar(v2, destino);
        Assert.Equal(new VersaoPrograma(0, 2, 0), InstalacaoAlinhavo.VersaoInstalada(destino));
        Assert.False(File.Exists(Path.Combine(destino, "sobra-da-versao-antiga.txt")));
        Assert.False(Directory.Exists(destino + ".novo"));
        Assert.False(Directory.Exists(destino + AtualizadorService.SufixoDoAntigo));
    }

    [Fact]
    public async Task Pacote_sem_exe_nao_troca_a_instalacao_existente()
    {
        var destino = Path.Combine(_pasta, "Alinhavo");
        var bom = Path.Combine(_pasta, "bom.zip");
        await File.WriteAllBytesAsync(bom, CriarZip("0.1.0"));
        InstalacaoAlinhavo.Instalar(bom, destino);

        var ruim = Path.Combine(_pasta, "ruim.zip");
        await File.WriteAllBytesAsync(ruim, CriarZip("9.9.9", comExe: false));
        Assert.Throws<RegraDeAtualizacaoException>(() => InstalacaoAlinhavo.Instalar(ruim, destino));

        Assert.Equal(new VersaoPrograma(0, 1, 0), InstalacaoAlinhavo.VersaoInstalada(destino));
        Assert.False(Directory.Exists(destino + ".novo"));
    }

    [Fact]
    public void Zip_corrompido_vira_mensagem_simples()
    {
        var lixo = Path.Combine(_pasta, "lixo.zip");
        File.WriteAllBytes(lixo, new byte[2048]);
        var ex = Assert.Throws<RegraDeAtualizacaoException>(() => InstalacaoAlinhavo.Instalar(lixo, Path.Combine(_pasta, "Alinhavo")));
        Assert.Contains("corrompido", ex.Message);
    }

    private sealed class ReleaseFalsa(string json, string versaoTxt) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var corpo = req.RequestUri!.AbsolutePath.EndsWith("/releases/latest") ? json : versaoTxt;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(corpo) });
        }
    }

    [Fact]
    public async Task Fonte_le_zip_hash_e_a_versao_do_alinhavo_da_release_do_nexus()
    {
        var sha = new string('a', 64);
        var json = "{\"tag_name\":\"v1.2.80\",\"draft\":false,\"prerelease\":false,\"body\":\"notas\",\"assets\":[" +
                   "{\"name\":\"Alinhavo.zip\",\"url\":\"https://api.github.com/a/zip\",\"size\":1234,\"digest\":\"sha256:" + sha + "\"}," +
                   "{\"name\":\"alinhavo-versao.txt\",\"url\":\"https://api.github.com/a/ver\",\"size\":6}]}";
        using var http = new HttpClient(new ReleaseFalsa(json, "0.3.0\n"));
        var info = await new FonteAlinhavo(http, "dono/repo").UltimaAsync();

        Assert.NotNull(info);
        Assert.Equal(new VersaoPrograma(0, 3, 0), info!.Versao);   // a do Alinhavo, não a tag v1.2.80 do Nexus
        Assert.Equal(sha, info.Zip.Sha256);
        Assert.Equal(1234, info.Zip.Tamanho);
    }

    [Fact]
    public async Task Release_sem_alinhavo_devolve_nulo()
    {
        var json = "{\"tag_name\":\"v1.2.80\",\"assets\":[{\"name\":\"NexusLS.exe\",\"url\":\"https://api.github.com/a/x\",\"size\":1}]}";
        using var http = new HttpClient(new ReleaseFalsa(json, ""));
        Assert.Null(await new FonteAlinhavo(http, "dono/repo").UltimaAsync());
    }

    [Fact]
    public void Erros_viram_texto_em_portugues_simples()
    {
        Assert.Contains("internet", InstalacaoAlinhavo.Traduzir(new HttpRequestException("x")));
        Assert.Contains("espaço", InstalacaoAlinhavo.Traduzir(new IOException("disco", unchecked((int)0x80070070))));
        Assert.Contains("Feche o Alinhavo", InstalacaoAlinhavo.Traduzir(new UnauthorizedAccessException()));
    }
}
