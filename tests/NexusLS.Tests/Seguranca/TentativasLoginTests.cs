using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests.Seguranca;

public sealed class TentativasLoginTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta;
    private readonly string _caminho;
    private DateTime _agora = new(2026, 11, 9, 10, 0, 0);

    public TentativasLoginTests()
    {
        _pasta = Path.Combine(Path.GetTempPath(), "nexus-tentativas-testes-" + Guid.NewGuid().ToString("N"));
        _caminho = Path.Combine(_pasta, "seguranca", "tentativas.json");
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

    /// <summary>Store em pasta temporária: não encosta no perfil real do usuário.</summary>
    private FuncionarioService NovoServico() =>
        new(_banco.Fabrica, () => _agora, new TentativasLoginStore(_caminho));

    private async Task<(FuncionarioService Servico, Funcionario Admin)> AdminAsync()
    {
        var servico = NovoServico();
        var (admin, _) = await servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        return (servico, admin);
    }

    private static async Task ErrarCincoVezesAsync(FuncionarioService servico, int id)
    {
        for (var i = 0; i < FuncionarioService.MaxTentativas; i++)
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.AutenticarAsync(id, "0000"));
    }

    [Fact]
    public async Task Cinco_falhas_bloqueiam_e_o_bloqueio_sobrevive_a_nova_instancia_do_servico()
    {
        var (servico, admin) = await AdminAsync();
        await ErrarCincoVezesAsync(servico, admin.Id);

        var bloqueado = await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.AutenticarAsync(admin.Id, "1234"));
        Assert.Contains("Aguarde", bloqueado.Message);

        // Simula fechar e abrir o programa: outra instância lê o mesmo arquivo.
        var aposReiniciar = NovoServico();
        var aindaBloqueado = await Assert.ThrowsAsync<RegraDeNegocioException>(() => aposReiniciar.AutenticarAsync(admin.Id, "1234"));
        Assert.Contains("Aguarde", aindaBloqueado.Message);
    }

    [Fact]
    public async Task Depois_de_30_segundos_o_bloqueio_libera()
    {
        var (servico, admin) = await AdminAsync();
        await ErrarCincoVezesAsync(servico, admin.Id);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.AutenticarAsync(admin.Id, "1234"));

        _agora = _agora.Add(FuncionarioService.TempoDeBloqueio);

        Assert.Equal(admin.Id, (await servico.AutenticarAsync(admin.Id, "1234")).Id);
    }

    [Fact]
    public async Task Acerto_zera_as_falhas_para_a_proxima_sequencia()
    {
        var (servico, admin) = await AdminAsync();
        for (var i = 0; i < FuncionarioService.MaxTentativas - 1; i++)
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.AutenticarAsync(admin.Id, "0000"));

        await servico.AutenticarAsync(admin.Id, "1234");

        for (var i = 0; i < FuncionarioService.MaxTentativas - 1; i++)
            await Assert.ThrowsAsync<RegraDeNegocioException>(() => servico.AutenticarAsync(admin.Id, "0000"));

        Assert.Equal(admin.Id, (await servico.AutenticarAsync(admin.Id, "1234")).Id); // não foi bloqueado
    }

    [Fact]
    public async Task Pin_correto_entra_normalmente()
    {
        var (servico, admin) = await AdminAsync();

        Assert.Equal(admin.Id, (await servico.AutenticarAsync(admin.Id, "1234")).Id);
        Assert.Equal(admin.Id, (await servico.AutenticarAsync(admin.Id, "1234")).Id);
    }

    [Fact]
    public async Task Bloqueio_de_um_funcionario_nao_afeta_outro()
    {
        var (servico, admin) = await AdminAsync();
        var ana = await servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor), "4321");

        await ErrarCincoVezesAsync(servico, admin.Id);

        Assert.Equal(ana.Id, (await servico.AutenticarAsync(ana.Id, "4321")).Id);
    }
}
