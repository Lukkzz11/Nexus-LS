using Microsoft.EntityFrameworkCore;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class ComprasViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly DateTime _agora = new(2026, 10, 20, 10, 0, 0);
    private readonly ProdutoService _produtos;
    private readonly FornecedorService _fornecedores;

    public ComprasViewModelTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _fornecedores = new FornecedorService(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private async Task<ComprasViewModel> CriarAsync()
    {
        var vm = new ComprasViewModel(new CompraService(_banco.Fabrica, () => _agora), _produtos, _fornecedores, () => _agora);
        await vm.Carregamento;
        return vm;
    }

    [Fact]
    public async Task Enter_na_busca_adiciona_o_produto_com_o_custo_atual_e_soma_o_total()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2000, 800, 1), 0);
        var vm = await CriarAsync();

        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Itens);
        Assert.Equal(("Boné Aba Reta", "8,00"), (item.Nome, item.CustoTexto));
        Assert.Contains("8,00", vm.TotalTexto.Replace(' ', ' '));
        Assert.Equal("", vm.BuscaProduto);

        item.QuantidadeTexto = "10";
        Assert.Contains("80,00", vm.TotalTexto.Replace(' ', ' '));
    }

    [Fact]
    public async Task Adicionar_o_mesmo_produto_de_novo_soma_a_quantidade()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 0);
        var vm = await CriarAsync();

        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);

        Assert.Equal(2, Assert.Single(vm.Itens).Quantidade);
    }

    [Fact]
    public async Task Produto_inexistente_mostra_o_erro()
    {
        var vm = await CriarAsync();

        vm.BuscaProduto = "nada";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Empty(vm.Itens);
    }

    [Fact]
    public async Task Produto_com_grade_comeca_no_primeiro_tamanho_real()
    {
        var camiseta = (await _produtos.CadastrarAsync(new DadosProduto("Camiseta", "789", 5000, 2000, 2), 0)).Id;
        foreach (var t in new[] { "P", "M" }) await _produtos.AdicionarTamanhoAsync(camiseta, t);
        var vm = await CriarAsync();

        vm.BuscaProduto = "camiseta";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);

        var item = Assert.Single(vm.Itens);
        Assert.Equal(new[] { "P", "M" }, item.Tamanhos);
        Assert.Equal("P", item.Tamanho);
    }

    [Fact]
    public async Task Registrar_atualiza_estoque_gera_a_conta_e_limpa_a_tela()
    {
        var bone = (await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 0)).Id;
        var forn = await _fornecedores.CadastrarAsync(new DadosFornecedor("Malharia Sol"));
        var vm = await CriarAsync();
        vm.Fornecedor = vm.Fornecedores.Single();
        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        vm.Itens.Single().QuantidadeTexto = "5";
        vm.Itens.Single().CustoTexto = "10,00";
        vm.ParcelasTexto = "2";

        await vm.RegistrarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("registrada", vm.Mensagem);
        Assert.Empty(vm.Itens);
        Assert.Single(vm.Historico);
        await using var db = _banco.CriarContexto();
        Assert.Equal(5, (await db.Produtos.SingleAsync(p => p.Id == bone)).EstoqueAtual);
        Assert.Equal(2, await db.Lancamentos.CountAsync(l => l.CompraId != null));
    }

    [Fact]
    public async Task Quantidade_ou_custo_invalidos_nao_registram()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 0);
        var vm = await CriarAsync();
        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);

        vm.Itens.Single().QuantidadeTexto = "abc";
        await vm.RegistrarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("quantidade", vm.Mensagem);

        vm.Itens.Single().QuantidadeTexto = "2";
        vm.Itens.Single().CustoTexto = "";
        await vm.RegistrarCommand.ExecuteAsync(null);
        Assert.Contains("custo", vm.Mensagem);
        Assert.Empty(vm.Historico);
    }

    [Fact]
    public async Task Cancelar_pelo_historico_pede_o_motivo_e_devolve_o_estoque()
    {
        var bone = (await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 0)).Id;
        var vm = await CriarAsync();
        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        vm.GerarContas = false;
        await vm.RegistrarCommand.ExecuteAsync(null);
        vm.Selecionada = vm.Historico.Single();
        Assert.True(vm.PodeCancelar);

        vm.PedirMotivo = _ => null;
        await vm.CancelarCompraCommand.ExecuteAsync(null);
        Assert.False(vm.Historico.Single().Compra.Cancelada);

        vm.PedirMotivo = _ => "lançada por engano";
        await vm.CancelarCompraCommand.ExecuteAsync(null);

        Assert.Equal("Cancelada", vm.Historico.Single().Situacao);
        await using var db = _banco.CriarContexto();
        Assert.Equal(0, (await db.Produtos.SingleAsync(p => p.Id == bone)).EstoqueAtual);
    }
}

public sealed class FornecedoresViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private async Task<FornecedoresViewModel> CriarAsync()
    {
        var vm = new FornecedoresViewModel(new FornecedorService(_banco.Fabrica));
        await vm.Carregamento;
        return vm;
    }

    [Fact]
    public async Task Salvar_cadastra_limpa_o_formulario_e_lista_o_fornecedor()
    {
        var vm = await CriarAsync();
        vm.Nome = "Malharia Sol";
        vm.Telefone = "8199999-0000";

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("cadastrado", vm.Mensagem);
        Assert.Equal("", vm.Nome);
        Assert.Equal("Malharia Sol", Assert.Single(vm.Fornecedores).Nome);
    }

    [Fact]
    public async Task Nome_repetido_ou_vazio_mostra_o_erro()
    {
        var vm = await CriarAsync();
        vm.Nome = "Sol";
        await vm.SalvarCommand.ExecuteAsync(null);

        vm.Nome = "sol";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);

        vm.Nome = "";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Single(vm.Fornecedores);
    }

    [Fact]
    public async Task Selecionar_carrega_no_formulario_e_desativar_tira_da_lista()
    {
        var vm = await CriarAsync();
        vm.Nome = "Sol";
        vm.Email = "a@b.com";
        await vm.SalvarCommand.ExecuteAsync(null);

        vm.Selecionado = vm.Fornecedores.Single();
        Assert.True(vm.Editando);
        Assert.Equal("a@b.com", vm.Email);

        await vm.AlternarAtivoCommand.ExecuteAsync(null);
        Assert.Empty(vm.Fornecedores);
        vm.MostrarInativos = true;
        await Task.Delay(200);
        Assert.Single(vm.Fornecedores);
    }
}
