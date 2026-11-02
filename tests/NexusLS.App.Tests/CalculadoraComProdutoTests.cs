using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class CalculadoraComProdutoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;

    public CalculadoraComProdutoTests() => _produtos = new ProdutoService(_banco.Fabrica);

    public void Dispose() => _banco.Dispose();

    private async Task<int> CadastrarAsync() =>
        (await _produtos.CadastrarAsync(new DadosProduto("Camiseta Surf", "789", 5000, 2000, 1, PrecoAtacadoCentavos: 3500, Categoria: "Camisetas"), 7)).Id;

    [Fact]
    public async Task Carregar_o_produto_preenche_custo_e_precos_da_calculadora()
    {
        await CadastrarAsync();
        var vm = new CalculadoraPrecosViewModel(_produtos) { ProdutoBusca = "camiseta" };

        await vm.BuscarProdutoCommand.ExecuteAsync(null);
        Assert.Equal("Camiseta Surf", vm.ProdutoEscolhido?.Nome);
        await vm.UsarProdutoCommand.ExecuteAsync(null);

        Assert.Equal("20,00", vm.CustoCompra);
        Assert.Equal("50,00", vm.PrecoOriginal);
        Assert.Equal("50,00", vm.PrecoPagamento);
        Assert.Contains("Camiseta Surf", vm.ProdutoInfo);
    }

    [Fact]
    public async Task Aplicar_o_preco_calculado_muda_so_o_preco_de_venda_e_pede_confirmacao()
    {
        var id = await CadastrarAsync();
        var vm = new CalculadoraPrecosViewModel(_produtos) { ProdutoBusca = "camiseta", MargemPct = "50" };
        await vm.BuscarProdutoCommand.ExecuteAsync(null);
        await vm.UsarProdutoCommand.ExecuteAsync(null);
        await vm.CalcularPrecoCommand.ExecuteAsync(null);   // custo 20 e margem 50% → R$ 40,00

        string? pergunta = null;
        vm.Confirmar = t => { pergunta = t; return false; };
        await vm.AplicarPrecoCommand.ExecuteAsync(null);
        Assert.Contains("R$ 50,00", pergunta);
        await using (var db = _banco.CriarContexto())
            Assert.Equal(5000, (await db.Produtos.AsNoTracking().SingleAsync(p => p.Id == id)).PrecoVendaCentavos);   // recusou: nada mudou

        vm.Confirmar = _ => true;
        await vm.AplicarPrecoCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        await using var db2 = _banco.CriarContexto();
        var p = await db2.Produtos.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(4000, p.PrecoVendaCentavos);
        Assert.Equal(2000, p.PrecoCustoCentavos);        // o resto do produto não mexe
        Assert.Equal(3500, p.PrecoAtacadoCentavos);
        Assert.Equal(7, p.EstoqueAtual);
    }

    [Fact]
    public async Task Sem_calcular_ou_sem_produto_a_tela_avisa_e_nao_grava()
    {
        var id = await CadastrarAsync();
        var vm = new CalculadoraPrecosViewModel(_produtos) { ProdutoBusca = "camiseta" };

        await vm.AplicarPrecoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Escolha o produto", vm.Mensagem);

        await vm.BuscarProdutoCommand.ExecuteAsync(null);
        await vm.AplicarPrecoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("Calcule o preço", vm.Mensagem);

        await using var db = _banco.CriarContexto();
        Assert.Equal(5000, (await db.Produtos.AsNoTracking().SingleAsync(p => p.Id == id)).PrecoVendaCentavos);
    }

    [Fact]
    public async Task O_servico_recusa_preco_zero_e_produto_inexistente()
    {
        var id = await CadastrarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _produtos.AplicarPrecoVendaAsync(id, 0));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _produtos.AplicarPrecoVendaAsync(9999, 1000));
    }

    [Fact]
    public async Task Parametros_da_loja_sao_guardados_e_voltam_na_proxima_abertura_sem_o_custo()
    {
        var arquivo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"calc-{Guid.NewGuid():N}.json");
        try
        {
            var loja = new ParametrosCalculadoraStore(arquivo);
            var vm = new CalculadoraPrecosViewModel(null, loja) { ImpostosPct = "8", ComissaoPct = "3", FixasMensais = "5000", PecasPorMes = "400", MargemPct = "35", TerminarEm90 = true, CustoCompra = "99", FreteReais = "7" };
            await vm.SalvarParametrosCommand.ExecuteAsync(null);
            Assert.False(vm.MensagemEhErro, vm.Mensagem);

            var outra = new CalculadoraPrecosViewModel(null, loja);
            Assert.Equal("8", outra.ImpostosPct);
            Assert.Equal("5000", outra.FixasMensais);
            Assert.Equal("35", outra.MargemPct);
            Assert.True(outra.TerminarEm90);
            Assert.Equal("", outra.CustoCompra);
            Assert.Equal("", outra.FreteReais);
        }
        finally { System.IO.File.Delete(arquivo); }
    }

    [Fact]
    public async Task Sem_arquivo_ou_com_arquivo_quebrado_a_calculadora_abre_normal()
    {
        var arquivo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"calc-{Guid.NewGuid():N}.json");
        try
        {
            Assert.Equal("", new CalculadoraPrecosViewModel(null, new ParametrosCalculadoraStore(arquivo)).ImpostosPct);
            await System.IO.File.WriteAllTextAsync(arquivo, "{ isso nao e json");
            Assert.Equal("", new CalculadoraPrecosViewModel(null, new ParametrosCalculadoraStore(arquivo)).ImpostosPct);
        }
        finally { System.IO.File.Delete(arquivo); }
    }
}
