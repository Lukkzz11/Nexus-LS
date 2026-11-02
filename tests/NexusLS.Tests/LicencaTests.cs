using System.Security.Cryptography;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

public sealed class LicencaTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-lic-" + Guid.NewGuid().ToString("N"));
    private readonly string _privada;
    private readonly string _publica;
    private DateTime _hoje = new(2026, 11, 2);

    public LicencaTests()
    {
        Directory.CreateDirectory(_pasta);
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _privada = Convert.ToBase64String(ec.ExportPkcs8PrivateKey());
        _publica = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
    }

    public void Dispose() => Directory.Delete(_pasta, recursive: true);

    private LicencaService Servico(string? maquina = null) =>
        new(Path.Combine(_pasta, "licenca.json"), () => _hoje, _publica, () => maquina ?? "AAAA1111BBBB");

    private string Arquivo(string conteudo)
    {
        var p = Path.Combine(_pasta, Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(p, conteudo);
        return p;
    }

    [Fact]
    public void Sem_arquivo_e_modo_de_avaliacao()
    {
        var r = Servico().Avaliar();
        Assert.Equal(EstadoLicenca.SemLicenca, r.Estado);
        Assert.False(r.Ativa);
    }

    [Fact]
    public void Licenca_assinada_instala_e_vale_ate_a_data()
    {
        var json = GeradorLicenca.Gerar(_privada, "Loja Surf", new DateTime(2027, 11, 2));
        var s = Servico();

        var r = s.Instalar(Arquivo(json));

        Assert.Equal(EstadoLicenca.Valida, r.Estado);
        Assert.Equal("Loja Surf", r.Cliente);
        Assert.Equal(365, r.DiasRestantes);
        Assert.Equal(EstadoLicenca.Valida, s.Avaliar().Estado);   // ficou instalada
    }

    [Fact]
    public void Perto_do_fim_avisa_e_depois_do_fim_vence()
    {
        var s = Servico();
        s.Instalar(Arquivo(GeradorLicenca.Gerar(_privada, "Loja", new DateTime(2026, 11, 10))));

        Assert.Equal(EstadoLicenca.Vencendo, s.Avaliar().Estado);
        Assert.True(s.Avaliar().Ativa);

        _hoje = new DateTime(2026, 11, 11);
        var r = s.Avaliar();
        Assert.Equal(EstadoLicenca.Vencida, r.Estado);
        Assert.False(r.Ativa);
    }

    [Fact]
    public void Arquivo_editado_ou_de_outra_chave_e_recusado()
    {
        var json = GeradorLicenca.Gerar(_privada, "Loja Surf", new DateTime(2027, 1, 1));
        var adulterado = json.Replace("2027-01-01", "2099-01-01");
        var ex = Assert.Throws<RegraDeNegocioException>(() => Servico().Instalar(Arquivo(adulterado)));
        Assert.Contains("inválida", ex.Message);

        using var outra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var deOutro = GeradorLicenca.Gerar(Convert.ToBase64String(outra.ExportPkcs8PrivateKey()), "Loja", new DateTime(2027, 1, 1));
        Assert.Throws<RegraDeNegocioException>(() => Servico().Instalar(Arquivo(deOutro)));
        Assert.Throws<RegraDeNegocioException>(() => Servico().Instalar(Arquivo("isso nao e json")));
        Assert.Equal(EstadoLicenca.SemLicenca, Servico().Avaliar().Estado);   // nada foi instalado
    }

    [Fact]
    public void Licenca_presa_a_um_computador_so_vale_nele()
    {
        var json = GeradorLicenca.Gerar(_privada, "Loja", new DateTime(2027, 1, 1), "AAAA1111BBBB");

        Servico("AAAA1111BBBB").Instalar(Arquivo(json));                         // o computador certo aceita
        var ex = Assert.Throws<RegraDeNegocioException>(() => Servico("OUTROPC00000").Instalar(Arquivo(json)));
        Assert.Contains("outro computador", ex.Message);
    }

    [Fact]
    public void A_chave_publica_oficial_do_programa_e_valida_e_o_codigo_da_maquina_e_estavel()
    {
        using var ec = ECDsa.Create();
        ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(LicencaService.ChavePublicaOficial), out _);
        Assert.Equal(12, LicencaService.CodigoDaMaquina().Length);
        Assert.Equal(LicencaService.CodigoDaMaquina(), LicencaService.CodigoDaMaquina());
    }
}
