using System.IO;
using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Desconto acima do limite do balcão só passa com a autorização de um gerente. Mexe no estático Alcada: não roda em paralelo.</summary>
[Collection("Sessao")]
public sealed class AlcadaPdvTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-alc-" + Guid.NewGuid().ToString("N"));
    private readonly PdvViewModel _vm;

    public AlcadaPdvTests()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var config = new ConfiguracaoService(_banco.Fabrica);
        _vm = new PdvViewModel(produtos, new VendaService(_banco.Fabrica),
            new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "r.json")), _banco.Fabrica),
            new ClienteService(_banco.Fabrica), new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()), TimeSpan.Zero);
        produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 10).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        Alcada.Config = () => new();
        Alcada.Solicitar = _ => true;
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private async Task AteDescontoAsync()
    {
        _vm.Busca = "555";
        _vm.ProcessarTecla(ComandoTecla.Enter);
        for (var i = 0; i < 300 && _vm.Etapa != EtapaPdv.Tamanho; i++) await Task.Delay(10);
        _vm.ProcessarTecla(ComandoTecla.Enter);   // tamanho
        _vm.ProcessarTecla(ComandoTecla.Enter);   // quantidade 1
        _vm.ProcessarTecla(ComandoTecla.Enter);   // busca vazia -> desconto
        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        _vm.ProcessarTecla(ComandoTecla.Direita); // %
    }

    [Fact]
    public async Task Desconto_acima_do_limite_sem_gerente_nao_avanca()
    {
        Alcada.Config = () => new ConfiguracaoSeguranca { DescontoMaximoSemGerentePercent = 10 };
        var pediu = 0;
        Alcada.Solicitar = _ => { pediu++; return false; };
        await AteDescontoAsync();

        _vm.DescontoTexto = "20";
        _vm.ProcessarTecla(ComandoTecla.Enter);

        Assert.Equal(1, pediu);
        Assert.Equal(EtapaPdv.Desconto, _vm.Etapa);
        Assert.True(_vm.MensagemEhErro);
    }

    [Fact]
    public async Task Desconto_acima_do_limite_com_gerente_avanca()
    {
        Alcada.Config = () => new ConfiguracaoSeguranca { DescontoMaximoSemGerentePercent = 10 };
        Alcada.Solicitar = _ => true;
        await AteDescontoAsync();

        _vm.DescontoTexto = "20";
        _vm.ProcessarTecla(ComandoTecla.Enter);

        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
    }

    [Fact]
    public async Task Desconto_dentro_do_limite_nao_pede_gerente()
    {
        Alcada.Config = () => new ConfiguracaoSeguranca { DescontoMaximoSemGerentePercent = 10 };
        var pediu = 0;
        Alcada.Solicitar = _ => { pediu++; return false; };
        await AteDescontoAsync();

        _vm.DescontoTexto = "10";
        _vm.ProcessarTecla(ComandoTecla.Enter);

        Assert.Equal(0, pediu);
        Assert.Equal(EtapaPdv.Pagamento, _vm.Etapa);
    }

    [Fact]
    public void Gerente_e_administrador_dispensam_a_autorizacao()
    {
        Assert.True(Alcada.EhGerente(new Funcionario { Perfil = PerfilFuncionario.Gerente }));
        Assert.True(Alcada.EhGerente(new Funcionario { Perfil = PerfilFuncionario.Administrador }));
        Assert.False(Alcada.EhGerente(new Funcionario { Perfil = PerfilFuncionario.Vendedor }));
        Assert.False(Alcada.EhGerente(null));
    }
}
