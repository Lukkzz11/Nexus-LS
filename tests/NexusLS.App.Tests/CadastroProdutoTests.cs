using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Cadastro do produto na tela: categoria, código interno, gerador de código de barras e calculadora de preço.</summary>
public sealed class CadastroProdutoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly EstoqueLojaViewModel _vm;

    public CadastroProdutoTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vm = new EstoqueLojaViewModel(_produtos, new EstoqueService(_banco.Fabrica));
    }

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Salvar_guarda_a_categoria_e_mostra_o_codigo_interno_gerado()
    {
        _vm.NovoCommand.Execute(null);
        _vm.Nome = "Camiseta";
        _vm.PrecoVenda = "50,00";
        _vm.Categoria = "Roupas";
        await _vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.Equal("Roupas", _vm.Categoria);
        Assert.Equal("INT-000001", _vm.CodigoInterno);
    }

    [Fact]
    public async Task Botao_gerar_preenche_um_ean13_interno_valido()
    {
        await _vm.GerarCodigoBarrasCommand.ExecuteAsync(null);

        Assert.True(CodigoBarrasInterno.EhEan13Valido(_vm.CodigoBarras));
        Assert.StartsWith("2", _vm.CodigoBarras);
    }

    [Fact]
    public async Task Dois_produtos_gerados_em_sequencia_recebem_codigos_diferentes()
    {
        for (var i = 1; i <= 2; i++)
        {
            _vm.NovoCommand.Execute(null);
            _vm.Nome = "Produto " + i;
            _vm.PrecoVenda = "10,00";
            await _vm.GerarCodigoBarrasCommand.ExecuteAsync(null);
            await _vm.SalvarCommand.ExecuteAsync(null);
            Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        }

        var codigos = (await _produtos.BuscarAsync(null)).Select(p => p.CodigoBarras).ToList();
        Assert.Equal(2, codigos.Distinct().Count());
    }

    [Fact]
    public void Calculadora_mostra_margem_e_markup_e_avisa_preco_abaixo_do_custo()
    {
        _vm.PrecoCusto = "60,00";
        _vm.PrecoVenda = "100,00";
        Assert.Contains("Margem 40%", _vm.TextoMargem);
        Assert.Contains("Markup 66,67%", _vm.TextoMargem);

        _vm.PrecoVenda = "50,00";
        Assert.Contains("abaixo do custo", _vm.TextoMargem);

        _vm.PrecoCusto = "";
        Assert.Equal("", _vm.TextoMargem);
    }

    [Fact]
    public void Calcular_preco_usa_o_custo_e_a_margem_desejada()
    {
        _vm.PrecoCusto = "60,00";
        _vm.MargemDesejada = "40";
        _vm.CalcularPrecoCommand.Execute(null);

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.Equal("100,00", _vm.PrecoVenda);
    }

    [Theory]
    [InlineData("", "40")]        // sem custo
    [InlineData("60,00", "abc")]  // margem inválida
    [InlineData("60,00", "100")]  // margem impossível
    public void Calcular_preco_com_dados_ruins_mostra_erro_e_nao_mexe_no_preco(string custo, string margem)
    {
        _vm.PrecoVenda = "12,00";
        _vm.PrecoCusto = custo;
        _vm.MargemDesejada = margem;

        _vm.CalcularPrecoCommand.Execute(null);

        Assert.True(_vm.MensagemEhErro);
        Assert.Equal("12,00", _vm.PrecoVenda);
    }

    [Fact]
    public async Task Produto_duplicado_mostra_erro_na_tela()
    {
        for (var i = 0; i < 2; i++)
        {
            _vm.NovoCommand.Execute(null);
            _vm.Nome = "Boné";
            _vm.PrecoVenda = "10,00";
            await _vm.SalvarCommand.ExecuteAsync(null);
        }

        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Já existe", _vm.Mensagem);
    }
}
