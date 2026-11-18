using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class FichaFuncionarioTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-ficha-" + Guid.NewGuid().ToString("N"));
    private readonly FuncionarioService _servico;

    public FichaFuncionarioTests() => _servico = new FuncionarioService(
        _banco.Fabrica, () => new DateTime(2026, 11, 18, 10, 0, 0),
        new NexusLS.Core.Seguranca.TentativasLoginStore(Path.Combine(_pasta, "t.json")));

    public void Dispose()
    {
        _banco.Dispose();
        try { if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Ficha_completa_e_gravada_e_lida_de_volta()
    {
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var ficha = new FichaFuncionario(
            Cpf: "529.982.247-25", Rg: "12.345.678-9", DataNascimento: new DateTime(1995, 3, 10), Email: "ana@loja.com",
            Cep: "01001-000", Endereco: "Rua A", Numero: "10", Bairro: "Centro", Cidade: "São Paulo", Estado: "sp",
            DataAdmissao: new DateTime(2026, 1, 5), SalarioCentavos: 180000,
            ContatoEmergenciaNome: "João", ContatoEmergenciaTelefone: "11999990000", Observacao: "Folga às quartas", Foto: new byte[] { 1, 2, 3 });
        await _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor, Ficha: ficha), "4321");

        var ana = (await _servico.ListarAsync()).Single(f => f.Nome == "Ana");
        Assert.Equal("52998224725", ana.Cpf);
        Assert.Equal("SP", ana.Estado);
        Assert.Equal(180000, ana.SalarioCentavos);
        Assert.Equal(new DateTime(1995, 3, 10), ana.DataNascimento);
        Assert.Equal(new byte[] { 1, 2, 3 }, ana.Foto);
        Assert.Equal("Folga às quartas", ana.Observacao);
    }

    [Fact]
    public async Task Editar_sem_ficha_nao_apaga_a_ficha_existente()
    {
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var ana = await _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor, Ficha: new FichaFuncionario(Rg: "99")), "4321");
        await _servico.EditarAsync(ana.Id, new DadosFuncionario("Ana", PerfilFuncionario.Vendedor, Cargo: "Caixa"));
        var depois = (await _servico.ListarAsync()).Single(f => f.Nome == "Ana");
        Assert.Equal("99", depois.Rg);
        Assert.Equal("Caixa", depois.Cargo);
    }

    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("123")]
    public async Task Cpf_invalido_nao_cadastra(string cpf)
    {
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor, Ficha: new FichaFuncionario(Cpf: cpf)), "4321"));
    }

    [Fact]
    public async Task Foto_grande_demais_email_e_data_futura_sao_recusados()
    {
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        async Task Tenta(FichaFuncionario f) => await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor, Ficha: f), "4321"));
        await Tenta(new FichaFuncionario(Foto: new byte[FuncionarioService.FotoMaximaBytes + 1]));
        await Tenta(new FichaFuncionario(Email: "sem-arroba"));
        await Tenta(new FichaFuncionario(DataNascimento: DateTime.Today.AddDays(1)));
        await Tenta(new FichaFuncionario(SalarioCentavos: -1));
    }
}
