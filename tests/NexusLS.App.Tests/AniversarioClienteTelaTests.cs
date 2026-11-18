using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Cadastro de cliente com data de nascimento e o filtro "Aniversariantes do mês" da lista.</summary>
[Collection("OperadorAtual")]
public sealed class AniversarioClienteTelaTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 11, 5, 10, 0, 0);
    private readonly BancoDeTeste _banco = new();
    private readonly ClienteService _clientes;
    private readonly ClientesViewModel _vm;

    public AniversarioClienteTelaTests()
    {
        _clientes = new ClienteService(_banco.Fabrica, () => Agora);
        _vm = new ClientesViewModel(_clientes, hoje: () => Agora);
    }

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Data_de_nascimento_grava_e_reabre_no_cadastro()
    {
        _vm.NovoCommand.Execute(null);
        _vm.Nome = "Ana";
        _vm.DataNascimento = new DateTime(1990, 11, 20);
        await _vm.SalvarCommand.ExecuteAsync(null);

        _vm.NovoCommand.Execute(null);
        Assert.Null(_vm.DataNascimento);
        await _vm.BuscarCommand.ExecuteAsync(null);
        _vm.Selecionado = _vm.Clientes.Single();
        await Task.Delay(300);
        Assert.Equal(new DateTime(1990, 11, 20), _vm.DataNascimento);
    }

    [Fact]
    public async Task Filtro_aniversariantes_do_mes_mostra_so_quem_faz_anos_no_mes_atual()
    {
        await _clientes.CadastrarAsync(new DadosCliente("Novembro", DataNascimento: new DateTime(1990, 11, 20)));
        await _clientes.CadastrarAsync(new DadosCliente("Dezembro", DataNascimento: new DateTime(1990, 12, 1)));
        await _clientes.CadastrarAsync(new DadosCliente("SemData"));
        await _vm.BuscarCommand.ExecuteAsync(null);
        Assert.Equal(3, _vm.Clientes.Count);

        _vm.SoAniversariantesDoMes = true;
        await Task.Delay(300);

        Assert.Equal(new[] { "Novembro" }, _vm.Clientes.Select(c => c.Nome));
    }

    [Fact]
    public async Task Nascimento_no_futuro_mostra_erro_e_nao_grava()
    {
        _vm.NovoCommand.Execute(null);
        _vm.Nome = "Ana";
        _vm.DataNascimento = DateTime.Today.AddDays(2);
        await _vm.SalvarCommand.ExecuteAsync(null);
        Assert.Contains("passado", _vm.Mensagem);
        Assert.Empty(await _clientes.BuscarAsync(null));
    }
}
