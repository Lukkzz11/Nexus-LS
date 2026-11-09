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
        public string? Desvinculada;
        public Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default)
            => SemInternet ? throw new FalhaDeConexaoLicencaException("sem net") : Task.FromResult(Doc);
        public Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
        { Registrada = maquina; Doc = Doc! with { Maquina = maquina }; return Task.CompletedTask; }
        public Task DesvincularMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
        { Desvinculada = maquina; return Task.CompletedTask; }
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

    [Fact]
    public async Task Chave_digitada_em_minusculas_vale_como_maiuscula()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "*", null) };
        var r = await Novo(f).AtivarAsync(" nexus-abcd-2345-wxyz ");
        Assert.Equal("NEXUS-ABCD-2345-WXYZ", r.Chave);
    }

    [Fact]
    public async Task Funcao_fora_do_ar_cai_para_o_firestore_e_registra_la()
    {
        var principal = new FonteFalsa { SemInternet = true };
        var reserva = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "", null) };
        var s = Novo2(new FonteLicencaComReserva(principal, reserva));
        var r = await s.AtivarAsync("ABC-123456");
        Assert.Equal(EstadoLicencaNuvem.Valida, r.Estado);
        Assert.Equal("MAQ1", reserva.Registrada);
        Assert.Null(principal.Registrada);
    }

    [Fact]
    public async Task Bloqueio_no_servidor_apaga_cache_local_para_impedir_burla_offline()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "*", null) };
        var s = Novo(f);
        await s.AtivarAsync("ABC-123456");
        Assert.Equal(EstadoLicencaNuvem.Valida, s.AvaliarLocal().Estado);

        f.Doc = f.Doc with { Ativa = false };
        Assert.Equal(EstadoLicencaNuvem.Bloqueada, (await s.VerificarAsync()).Estado);
        Assert.Equal(EstadoLicencaNuvem.NaoEncontrada, s.AvaliarLocal().Estado);
    }

    [Fact]
    public async Task Adulterar_validade_no_arquivo_local_invalida_assinatura_hmac()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "*", null) };
        var s = Novo(f);
        await s.AtivarAsync("ABC-123456");

        var arq = Path.Combine(_pasta, "l.json");
        File.WriteAllText(arq, File.ReadAllText(arq).Replace("2027-05-01", "2099-12-31"));
        Assert.Equal(EstadoLicencaNuvem.NaoEncontrada, s.AvaliarLocal().Estado);
    }

    [Fact]
    public async Task Atrasar_relogio_do_windows_bloqueia_avaliacao_offline()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "*", null) };
        var s = Novo(f);
        await s.AtivarAsync("ABC-123456");

        _agora = _agora.AddDays(-2);
        Assert.Equal(EstadoLicencaNuvem.SemConexao, s.AvaliarLocal().Estado);
    }

    [Fact]
    public async Task Remover_async_desvincula_na_nuvem_e_apaga_local()
    {
        var f = new FonteFalsa { Doc = new("Loja Sol", "2027-05-01", true, "", null) };
        var s = Novo(f, "MAQ77");
        await s.AtivarAsync("ABC-123456");
        await s.RemoverAsync();
        Assert.Equal("MAQ77", f.Desvinculada);
        Assert.Equal(EstadoLicencaNuvem.NaoEncontrada, s.AvaliarLocal().Estado);
    }

    [Fact]
    public async Task Fluxo_completo_LicenseManager_e_Atualizador_multiplos_computadores_e_desvinculacao()
    {
        // 1. Admin gera chave no License Manager com limite de 2 computadores (status inicial PENDENTE, maquina = "")
        var f = new FonteFalsa
        {
            Doc = new("Carlos Silva", "2027-11-09", Ativa: true, Maquina: "", Plano: "anual", Empresa: "Supermercado Central", LimiteDispositivos: 2)
        };

        var pc1 = new ServicoLicencaNuvem(f, Path.Combine(_pasta, "pc1.json"), () => _agora, () => "PC-CAIXA-01");
        var pc2 = new ServicoLicencaNuvem(f, Path.Combine(_pasta, "pc2.json"), () => _agora, () => "PC-CAIXA-02");
        var pc3 = new ServicoLicencaNuvem(f, Path.Combine(_pasta, "pc3.json"), () => _agora, () => "PC-GERENCIA-03");

        // 2. Cliente ativa no PC 1 -> ocupa vaga 1/2
        var r1 = await pc1.AtivarAsync("NEXUS-8K29-XP41-MZ73");
        Assert.Equal(EstadoLicencaNuvem.Valida, r1.Estado);
        Assert.Equal("Supermercado Central", r1.Empresa);
        Assert.Equal(1, r1.DispositivosUsados);
        Assert.Equal(2, r1.LimiteDispositivos);
        Assert.Equal("PC-CAIXA-01", f.Doc.Maquina);

        // 3. Cliente ativa a mesma chave no PC 2 -> ocupa vaga 2/2
        var r2 = await pc2.AtivarAsync("NEXUS-8K29-XP41-MZ73");
        Assert.Equal(EstadoLicencaNuvem.Valida, r2.Estado);
        Assert.Equal(2, r2.DispositivosUsados);
        Assert.Equal("PC-CAIXA-01,PC-CAIXA-02", f.Doc.Maquina);

        // 4. Cliente tenta ativar no PC 3 -> recusado pois atingiu o limite (2/2)
        var r3 = await pc3.AtivarAsync("NEXUS-8K29-XP41-MZ73");
        Assert.Equal(EstadoLicencaNuvem.OutraMaquina, r3.Estado);
        Assert.False(r3.Ativa);

        // 5. Admin desvincula o PC 1 no License Manager (resta apenas PC-CAIXA-02 no servidor)
        f.Doc = f.Doc with { Maquina = "PC-CAIXA-02" };

        // Quando o PC 1 verifica novamente, perde a autorização e apaga o cache local
        var verPc1 = await pc1.VerificarAsync();
        Assert.Equal(EstadoLicencaNuvem.OutraMaquina, verPc1.Estado);
        Assert.Equal(EstadoLicencaNuvem.NaoEncontrada, pc1.AvaliarLocal().Estado);

        // 6. Agora o PC 3 consegue ativar na vaga liberada!
        var r3Novo = await pc3.AtivarAsync("NEXUS-8K29-XP41-MZ73");
        Assert.Equal(EstadoLicencaNuvem.Valida, r3Novo.Estado);
        Assert.Equal("PC-CAIXA-02,PC-GERENCIA-03", f.Doc.Maquina);
    }

    private ServicoLicencaNuvem Novo2(IFonteLicenca f) => new(f, Path.Combine(_pasta, "l2.json"), () => _agora, () => "MAQ1");
}
