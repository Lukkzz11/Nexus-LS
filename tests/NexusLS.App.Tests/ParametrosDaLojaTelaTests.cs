using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class ParametrosDaLojaTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static async Task Esperar(Func<bool> condicao)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "a tela não terminou de carregar");
    }

    [Fact]
    public async Task Tela_salva_os_parametros_e_a_previa_acompanha_o_que_se_liga_e_desliga()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        var vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira()));
        await Esperar(() => vm.NomeLoja.Length > 0);

        vm.RazaoSocial = "Life Confecções LTDA";
        Assert.Contains("Life Confecções LTDA", vm.Previa);
        vm.CupomMostrarRazaoSocial = false;
        Assert.DoesNotContain("Life Confecções LTDA", vm.Previa);

        vm.ChavePix = "pix@life.com"; vm.CupomMostrarPix = true;
        vm.FundoTrocoPadrao = "50,00"; vm.EstoqueMinimoPadrao = "3"; vm.MargemPadrao = "45";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);

        var salvo = await config.ObterAsync();
        Assert.Equal(5000, salvo.FundoTrocoPadraoCentavos);
        Assert.Equal(3, salvo.EstoqueMinimoPadrao);
        Assert.Equal(45, salvo.MargemPadraoPercentual);
        Assert.True(salvo.CupomMostrarPix);
        Assert.False(salvo.CupomMostrarRazaoSocial);

        vm.MargemPadrao = "abc";                                       // incompleto: a prévia não quebra, salvar avisa
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("margem padrão", vm.Mensagem);
    }

    [Fact]
    public async Task Caixa_sugere_o_troco_padrao_e_o_cadastro_sugere_estoque_minimo_e_margem()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        await config.SalvarAsync(new DadosLoja("Life", FundoTrocoPadraoCentavos: 7500, EstoqueMinimoPadrao: 4, MargemPadraoPercentual: 50));

        var fechamento = new FechamentoViewModel(new CaixaService(_banco.Fabrica), config);
        await Esperar(() => fechamento.TrocoInicialTexto.Length > 0);
        Assert.Equal("75,00", fechamento.TrocoInicialTexto);

        var estoque = new EstoqueLojaViewModel(new ProdutoService(_banco.Fabrica), new EstoqueService(_banco.Fabrica), null, config);
        await Esperar(() => estoque.EstoqueMinimo == "4");
        Assert.Equal("50", estoque.MargemDesejada);
        estoque.NovoCommand.Execute(null);                             // "Novo" também volta às sugestões
        Assert.Equal("4", estoque.EstoqueMinimo);
    }

    [Fact]
    public async Task Sem_troco_padrao_o_campo_do_caixa_fica_em_branco()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        var fechamento = new FechamentoViewModel(new CaixaService(_banco.Fabrica), config);
        await Task.Delay(150);
        Assert.Equal("", fechamento.TrocoInicialTexto);
    }
}
