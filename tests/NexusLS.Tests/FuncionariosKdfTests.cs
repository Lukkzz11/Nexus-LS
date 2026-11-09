using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class FuncionariosKdfTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta;
    private readonly FuncionarioService _servico;

    public FuncionariosKdfTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "nexus-kdf-testes-" + Guid.NewGuid().ToString("N"));
        _servico = new FuncionarioService(
            _banco.Fabrica,
            () => new DateTime(2026, 11, 9, 10, 0, 0),
            new TentativasLoginStore(Path.Combine(_pasta, "tentativas.json")));
    }

    public void Dispose()
    {
        _banco.Dispose();
        try
        {
            if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
        }
        catch (Exception)
        {
            // pasta temporária presa: o Windows limpa depois.
        }
    }

    /// <summary>Gera o hash do formato antigo, igual ao que a versão anterior gravava.</summary>
    private static string HashLegado(string segredo)
    {
        var sal = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(segredo, sal, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(sal) + ":" + Convert.ToBase64String(hash);
    }

    private async Task GravarPinHashAsync(int id, string pinHash)
    {
        await using var db = _banco.CriarContexto();
        var f = await db.Funcionarios.SingleAsync(x => x.Id == id);
        f.PinHash = pinHash;
        await db.SaveChangesAsync();
    }

    private async Task GravarRecuperacaoHashAsync(int id, string recuperacaoHash)
    {
        await using var db = _banco.CriarContexto();
        var f = await db.Funcionarios.SingleAsync(x => x.Id == id);
        f.RecuperacaoHash = recuperacaoHash;
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Hash_novo_nasce_no_formato_v2_com_600_mil_iteracoes()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");

        await using var db = _banco.CriarContexto();
        var guardado = await db.Funcionarios.SingleAsync(f => f.Id == admin.Id);

        Assert.StartsWith("v2:600000:", guardado.PinHash);
        Assert.DoesNotContain("1234", guardado.PinHash);
        Assert.StartsWith("v2:600000:", guardado.RecuperacaoHash);
    }

    [Fact]
    public async Task Hash_legado_autentica_e_e_regravado_em_v2_no_login()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var legado = HashLegado("1234");
        await GravarPinHashAsync(admin.Id, legado);

        var autenticado = await _servico.AutenticarAsync(admin.Id, "1234");

        Assert.Equal(admin.Id, autenticado.Id);
        await using var db = _banco.CriarContexto();
        var atualizado = await db.Funcionarios.AsNoTracking().SingleAsync(f => f.Id == admin.Id);
        Assert.StartsWith("v2:", atualizado.PinHash);
        Assert.NotEqual(legado, atualizado.PinHash);
    }

    [Fact]
    public async Task Pin_errado_nao_atualiza_o_hash_legado()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var legado = HashLegado("1234");
        await GravarPinHashAsync(admin.Id, legado);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AutenticarAsync(admin.Id, "9999"));

        await using var db = _banco.CriarContexto();
        var guardado = await db.Funcionarios.AsNoTracking().SingleAsync(f => f.Id == admin.Id);
        Assert.Equal(legado, guardado.PinHash);
    }

    [Fact]
    public async Task Codigo_de_recuperacao_legado_continua_valendo_e_o_fluxo_grava_v2()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var codigo = "ABCD-EFGH-JKLM";
        await GravarRecuperacaoHashAsync(admin.Id, HashLegado("ABCDEFGHJKLM"));

        var (funcionario, _) = await _servico.RedefinirPinComCodigoAsync(codigo, "8888");

        Assert.Equal(admin.Id, funcionario.Id);
        await using var db = _banco.CriarContexto();
        var guardado = await db.Funcionarios.AsNoTracking().SingleAsync(f => f.Id == admin.Id);
        Assert.StartsWith("v2:600000:", guardado.PinHash);
        Assert.StartsWith("v2:600000:", guardado.RecuperacaoHash);
    }
}
