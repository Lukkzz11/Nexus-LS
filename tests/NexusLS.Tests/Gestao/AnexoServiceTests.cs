using System.Security.Cryptography;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests.Gestao;

public class AnexoServiceTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 19, 10, 0, 0);
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-anexo-" + Guid.NewGuid().ToString("N"));
    private readonly AnexoService _anexos;
    private readonly LancamentoEmpresaService _lancs;
    private int _lancId;

    public AnexoServiceTests()
    {
        Directory.CreateDirectory(_pasta);
        _anexos = new AnexoService(_banco.Fabrica, Path.Combine(_pasta, "anexos"));
        _lancs = new LancamentoEmpresaService(_banco.Fabrica, _anexos);
        _lancId = _lancs.CriarAsync(new DadosLancamentoEmpresa(Agora, TipoMovimentoEmpresa.Saida, "Compras", "Nota do fornecedor", 5000), Agora).GetAwaiter().GetResult().Id;
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private string Arquivo(string nome, byte[] bytes)
    {
        var caminho = Path.Combine(_pasta, nome);
        File.WriteAllBytes(caminho, bytes);
        return caminho;
    }

    private static readonly byte[] Pdf = "%PDF-1.7\nfake"u8.ToArray();
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 };
    private static readonly byte[] Jpg = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };

    [Fact]
    public async Task Copia_o_arquivo_guarda_caminho_relativo_e_dados()
    {
        var origem = Arquivo("Nota Fiscal.pdf", Pdf);
        var a = await _anexos.AdicionarAsync(_lancId, origem, Agora);

        Assert.Equal("Nota Fiscal.pdf", a.NomeOriginal);
        Assert.Equal("application/pdf", a.TipoMime);
        Assert.Equal(Pdf.Length, a.TamanhoBytes);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Pdf)).ToLowerInvariant(), a.Sha256);
        Assert.StartsWith("2026/11/", a.CaminhoRelativo);
        Assert.False(Path.IsPathRooted(a.CaminhoRelativo));
        Assert.DoesNotContain('\\', a.CaminhoRelativo);
        var destino = _anexos.CaminhoCompleto(a.CaminhoRelativo);
        Assert.Equal(Pdf, File.ReadAllBytes(destino));
        Assert.StartsWith(_anexos.Pasta, destino);
        Assert.True(_anexos.Existe(a));
        Assert.True(File.Exists(origem)); // o original não é movido
        Assert.Empty(Directory.GetFiles(_anexos.Pasta, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Mesmo_nome_duas_vezes_nao_sobrescreve()
    {
        var origem = Arquivo("foto.png", Png);
        var a = await _anexos.AdicionarAsync(_lancId, origem, Agora);
        var b = await _anexos.AdicionarAsync(_lancId, origem, Agora);
        Assert.NotEqual(a.CaminhoRelativo, b.CaminhoRelativo);
        Assert.Equal(2, (await _anexos.ListarAsync(_lancId)).Count);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("script.ps1")]
    [InlineData("pagina.html")]
    [InlineData("semextensao")]
    public async Task Recusa_extensao_nao_permitida(string nome)
    {
        var origem = Arquivo(nome, Pdf);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(_lancId, origem, Agora));
        Assert.Empty(await _anexos.ListarAsync(_lancId));
    }

    [Fact]
    public async Task Recusa_arquivo_vazio_grande_demais_e_conteudo_que_nao_bate()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(_lancId, Arquivo("vazio.pdf", Array.Empty<byte>()), Agora));

        var grande = Arquivo("grande.pdf", new byte[AnexoService.TamanhoMaximoBytes + 1]);
        await using (var fs = new FileStream(grande, FileMode.Open)) { fs.Write(Pdf); }
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(_lancId, grande, Agora));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(_lancId, Arquivo("falso.pdf", "MZ executavel"u8.ToArray()), Agora));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(_lancId, Arquivo("falso.jpg", Png), Agora));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(_lancId, Path.Combine(_pasta, "nao-existe.pdf"), Agora));

        Assert.Empty(await _anexos.ListarAsync(_lancId));
        Assert.Empty(Directory.Exists(_anexos.Pasta) ? Directory.GetFiles(_anexos.Pasta, "*", SearchOption.AllDirectories) : Array.Empty<string>());
    }

    [Fact]
    public async Task Aceita_exatamente_o_limite()
    {
        var bytes = new byte[AnexoService.TamanhoMaximoBytes];
        Pdf.CopyTo(bytes, 0);
        var a = await _anexos.AdicionarAsync(_lancId, Arquivo("limite.pdf", bytes), Agora);
        Assert.Equal(AnexoService.TamanhoMaximoBytes, a.TamanhoBytes);
    }

    [Fact]
    public async Task Recusa_lancamento_inexistente_sem_deixar_arquivo()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.AdicionarAsync(9999, Arquivo("a.pdf", Pdf), Agora));
        Assert.Empty(Directory.GetFiles(_anexos.Pasta, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../../Windows/system.ini", "system.ini")]
    [InlineData(@"..\..\x\evil.pdf", "evil.pdf")]
    [InlineData("C:\\temp\\nota.pdf", "nota.pdf")]
    [InlineData("a:b*c?.pdf", "a_b_c_.pdf")]
    [InlineData("FOTO.JPG", "FOTO.jpg")]
    [InlineData("CON.pdf", "_CON.pdf")]
    [InlineData("...pdf", "arquivo.pdf")]
    [InlineData("", "arquivo")]
    [InlineData("nome com espaço.pdf", "nome com espaço.pdf")]
    public void Sanitiza_o_nome(string entrada, string esperado)
    {
        var nome = AnexoService.SanitizarNome(entrada);
        Assert.Equal(esperado, nome);
        Assert.DoesNotContain('/', nome);
        Assert.DoesNotContain('\\', nome);
    }

    [Fact]
    public void Nome_muito_longo_e_cortado_mantendo_a_extensao()
    {
        var nome = AnexoService.SanitizarNome(new string('a', 300) + ".PDF");
        Assert.EndsWith(".pdf", nome);
        Assert.True(nome.Length <= 84);
    }

    [Theory]
    [InlineData("../fora.pdf")]
    [InlineData("2026/../../fora.pdf")]
    [InlineData("..\\fora.pdf")]
    [InlineData("C:/Windows/system.ini")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void Caminho_completo_recusa_traversal(string relativo) =>
        Assert.Throws<RegraDeNegocioException>(() => _anexos.CaminhoCompleto(relativo));

    [Fact]
    public async Task Remover_apaga_registro_e_arquivo_e_tolera_arquivo_ja_sumido()
    {
        var a = await _anexos.AdicionarAsync(_lancId, Arquivo("a.jpg", Jpg), Agora);
        var b = await _anexos.AdicionarAsync(_lancId, Arquivo("b.pdf", Pdf), Agora);
        var arquivoA = _anexos.CaminhoCompleto(a.CaminhoRelativo);

        await _anexos.RemoverAsync(a.Id);
        Assert.False(File.Exists(arquivoA));
        Assert.Single(await _anexos.ListarAsync(_lancId));

        File.Delete(_anexos.CaminhoCompleto(b.CaminhoRelativo));
        Assert.False(_anexos.Existe(b));
        await _anexos.RemoverAsync(b.Id);
        Assert.Empty(await _anexos.ListarAsync(_lancId));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _anexos.RemoverAsync(b.Id));
    }

    [Fact]
    public async Task Remover_nao_apaga_fora_da_pasta_mesmo_com_caminho_adulterado_no_banco()
    {
        var alheio = Arquivo("alheio.txt", "importante"u8.ToArray());
        await using (var db = _banco.CriarContexto())
        {
            db.AnexosLancamento.Add(new AnexoLancamento
            {
                LancamentoEmpresaId = _lancId, NomeOriginal = "x", CaminhoRelativo = "../alheio.txt", TipoMime = "text/plain", Sha256 = "0", CriadoEm = Agora
            });
            await db.SaveChangesAsync();
        }
        var id = (await _anexos.ListarAsync(_lancId)).Single().Id;
        await _anexos.RemoverAsync(id);
        Assert.True(File.Exists(alheio));
    }

    [Fact]
    public void Pasta_padrao_fica_ao_lado_do_banco() =>
        Assert.Equal(Path.Combine(_pasta, "anexos"), AnexoService.PastaPadrao(Path.Combine(_pasta, "nexus.db")));
}
