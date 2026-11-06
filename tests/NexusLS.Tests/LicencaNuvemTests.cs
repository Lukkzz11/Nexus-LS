using NexusLS.Core.Licenciamento;
using Xunit;

namespace NexusLS.Tests;

public class LicencaNuvemTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "licnuvem-" + Guid.NewGuid().ToString("N"));
    private DateTime _agora = new(2026, 11, 5, 10, 0, 0);

    private sealed class FonteFalsa : IFonteLicenca
    {
        public DocumentoLicenca? Doc;
        public bool SemInternet;
        public string? Registrada;
        public Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default)
            => SemInternet ? throw new FalhaDeConexaoLicencaException("sem net") : Task.FromResult(Doc);
        public Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
        { Registrada = maquina; Doc = Doc! with { Maquina = maquina }; return Task.CompletedTask; }
    }

    private ServicoLicencaNuvem Novo(FonteFalsa f, string maquina = "MAQ1")
        => new(f, Path.Combine(_pasta, "l.json"), () => _agora, () => maquina);

    public void Dispose() { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); }

    [Fact]
    public async Task Ativa_valida_prende_a_maquina_e_salva()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "", null) };
        var s = Novo(f);
        var r = await s.AtivarAsync("ABC-123456");
        Assert.Equal(EstadoLicencaNuvem.Valida, r.Estado);
        Assert.Equal("Loja Sol", r.Cliente);
        Assert.Equal("MAQ1", f.Registrada);
        Assert.Equal(EstadoLicencaNuvem.Valida, s.AvaliarLocal().Estado);
    }

    [Fact]
    public async Task Outra_maquina_e_recusada_e_nada_e_salvo()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "MAQ9", null) };
        var s = Novo(f);
        var r = await s.AtivarAsync("ABC-123456");
        Assert.Equal(EstadoLicencaNuvem.OutraMaquina, r.Estado);
        Assert.Null(f.Registrada);
        Assert.Equal(EstadoLicencaNuvem.NaoEncontrada, s.AvaliarLocal().Estado);
    }

    [Fact]
    public async Task Bloqueada_vencida_e_vencendo()
    {
        var f = new FonteFalsa { Doc = new("X", "2027-05-01", false, "*", null) };
        Assert.Equal(EstadoLicencaNuvem.Bloqueada, (await Novo(f).AtivarAsync("ABC-123456")).Estado);
        f.Doc = new("X", "2026-11-04", true, "*", null);
        Assert.Equal(EstadoLicencaNuvem.Vencida, (await Novo(f).AtivarAsync("ABC-123456")).Estado);
        f.Doc = new("X", "2026-11-20", true, "*", null);
        var r = await Novo(f).AtivarAsync("ABC-123456");
        Assert.Equal(EstadoLicencaNuvem.Vencendo, r.Estado);
        Assert.Equal(15, r.DiasRestantes);
        Assert.True(r.Ativa);
    }

    [Fact]
    public async Task Chave_invalida_ou_inexistente()
    {
        var f = new FonteFalsa();
        Assert.Equal(EstadoLicencaNuvem.ChaveInvalida, (await Novo(f).AtivarAsync("../x")).Estado);
        Assert.Equal(EstadoLicencaNuvem.NaoEncontrada, (await Novo(f).AtivarAsync("ABC-123456")).Estado);
    }

    [Fact]
    public async Task Sem_internet_vale_a_ultima_verificacao_por_sete_dias()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "*", null) };
        var s = Novo(f);
        await s.AtivarAsync("ABC-123456");
        f.SemInternet = true;
        _agora = _agora.AddDays(3);
        var r = await s.VerificarAsync();
        Assert.True(r.Ativa);
        Assert.True(r.Offline);
        _agora = _agora.AddDays(5);
        Assert.Equal(EstadoLicencaNuvem.SemConexao, (await s.VerificarAsync()).Estado);
    }

    [Fact]
    public async Task Verificar_pega_o_bloqueio_feito_no_site()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "*", null) };
        var s = Novo(f);
        await s.AtivarAsync("ABC-123456");
        f.Doc = f.Doc with { Ativa = false };
        Assert.Equal(EstadoLicencaNuvem.Bloqueada, (await s.VerificarAsync()).Estado);
    }

    [Fact]
    public void Le_o_json_do_firestore_com_validade_em_texto_ou_timestamp()
    {
        var d = FonteLicencaFirestore.Ler("{\"fields\":{\"cliente\":{\"stringValue\":\"Ana\"},\"validade\":{\"timestampValue\":\"2027-03-10T03:00:00Z\"},\"ativa\":{\"booleanValue\":true},\"maquina\":{\"stringValue\":\"\"}}}");
        Assert.NotNull(d);
        Assert.Equal("Ana", d!.Cliente);
        Assert.True(ServicoLicencaNuvem.TentarValidade(d.Validade, out var v));
        Assert.Equal(new DateTime(2027, 3, 10), v);
        Assert.True(d.Ativa);
    }
}
