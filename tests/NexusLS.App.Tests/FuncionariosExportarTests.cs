using System.IO;
using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Botão "Exportar CSV" da tela de funcionários.</summary>
[Collection("Sessao")]
public sealed class FuncionariosExportarTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly FuncionarioService _servico;
    private readonly string _arquivo = Path.Combine(Path.GetTempPath(), "nexus-exp-" + Guid.NewGuid().ToString("N") + ".csv");

    public FuncionariosExportarTests()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
        _servico = new FuncionarioService(_banco.Fabrica);
    }

    public void Dispose()
    {
        Sessao.Sair();
        Sessao.DefinirLoginAtivo(false);
        _banco.Dispose();
        try { File.Delete(_arquivo); } catch (IOException) { }
    }

    private async Task<FuncionariosViewModel> PrepararAsync(Funcionario logado)
    {
        Sessao.Entrar(logado);
        var vm = new FuncionariosViewModel(_servico);
        await vm.Carregamento;
        vm.EscolherArquivoParaSalvar = _ => _arquivo;
        return vm;
    }

    [Fact]
    public async Task Administrador_exporta_com_salario_e_sem_hash()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        await _servico.CadastrarAsync(new DadosFuncionario("Ana", PerfilFuncionario.Vendedor,
            Ficha: new FichaFuncionario(null, null, null, null, null, null, null, null, null, null, null, null, 250000, null, null, null, null)), "4321");
        var vm = await PrepararAsync(admin);

        await vm.ExportarCommand.ExecuteAsync(null);

        var texto = File.ReadAllText(_arquivo);
        Assert.Contains("Salário", texto);
        Assert.Contains("2500,00", texto);
        Assert.Contains("Ana", texto);
        Assert.DoesNotContain(admin.PinHash, texto);
        Assert.False(vm.MensagemEhErro);
    }

    [Fact]
    public async Task Quem_nao_e_administrador_mas_gerencia_funcionarios_exporta_sem_salario()
    {
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var chefe = new Funcionario { Id = 99, Nome = "Chefe", Perfil = PerfilFuncionario.Personalizado, PermissoesPersonalizadas = Permissao.Funcionarios };
        var vm = await PrepararAsync(chefe);

        await vm.ExportarCommand.ExecuteAsync(null);

        var texto = File.ReadAllText(_arquivo);
        Assert.DoesNotContain("Salário", texto);
        Assert.Contains("Dona Maria", texto);
        Assert.Contains("sem salários", vm.Mensagem);
    }

    [Fact]
    public async Task Sem_permissao_de_funcionarios_nao_exporta()
    {
        await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var vendedor = new Funcionario { Id = 98, Nome = "Vend", Perfil = PerfilFuncionario.Vendedor };
        var vm = await PrepararAsync(vendedor);

        await vm.ExportarCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.False(File.Exists(_arquivo));
    }

    [Fact]
    public async Task Cancelar_a_caixa_de_salvar_nao_cria_arquivo()
    {
        var (admin, _) = await _servico.CriarPrimeiroAdministradorAsync("Dona Maria", "1234");
        var vm = await PrepararAsync(admin);
        vm.EscolherArquivoParaSalvar = _ => null;

        await vm.ExportarCommand.ExecuteAsync(null);

        Assert.False(File.Exists(_arquivo));
    }
}
