using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Limite padrão do cliente novo e multa/juros na ficha. Mexe no estático Alcada: não roda em paralelo.</summary>
[Collection("Sessao")]
public sealed class CrediarioConfigTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose()
    {
        Alcada.Config = () => new();
        _banco.Dispose();
    }

    [Fact]
    public void Cliente_novo_ja_vem_com_o_limite_padrao()
    {
        Alcada.Config = () => new ConfiguracaoSeguranca { LimiteCreditoPadraoCentavos = 50000 };
        ClientesViewModel? vm = null;
        Capturador.Rodar(() => { vm = new ClientesViewModel(new ClienteService(_banco.Fabrica)); vm.NovoCommand.Execute(null); });
        Assert.Equal("500,00", vm!.LimiteTexto);
    }

    [Fact]
    public void Sem_limite_padrao_o_campo_fica_em_branco()
    {
        Alcada.Config = () => new();
        ClientesViewModel? vm = null;
        Capturador.Rodar(() => { vm = new ClientesViewModel(new ClienteService(_banco.Fabrica)); vm.NovoCommand.Execute(null); });
        Assert.Equal("", vm!.LimiteTexto);
    }
}
