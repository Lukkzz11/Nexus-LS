using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class PedidosViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly PedidoService _pedidos;

    public PedidosViewModelTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _pedidos = new PedidoService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private async Task<PedidosViewModel> CriarAsync()
    {
        var vm = new PedidosViewModel(_pedidos, _produtos, new ClienteService(_banco.Fabrica));
        await vm.Carregamento;
        return vm;
    }

    private async Task<Pedido> PedidoAsync(string cliente = "Maria", string item = "Camiseta") =>
        await _pedidos.CriarAsync(new DadosPedido(OrigemPedido.WhatsApp, new[] { new ItemPedidoDados(null, 2, item, 5000) }, ClienteNome: cliente));

    private static ColunaPedido Coluna(PedidosViewModel vm, StatusPedido s) => vm.Colunas.Single(c => c.Status == s);

    [Fact]
    public async Task Quadro_tem_uma_coluna_por_etapa_e_os_pedidos_caem_na_certa()
    {
        var a = await PedidoAsync("Maria");
        var b = await PedidoAsync("João");
        await _pedidos.AvancarAsync(b.Id);

        var vm = await CriarAsync();

        Assert.Equal(new[] { "Novo", "Em separação", "Separado", "Pronto para retirada", "Entregue" }, vm.Colunas.Select(c => c.Titulo));
        Assert.Equal("Maria", Assert.Single(Coluna(vm, StatusPedido.Novo).Cartoes).Cliente);
        Assert.Equal("João", Assert.Single(Coluna(vm, StatusPedido.EmSeparacao).Cartoes).Cliente);
        Assert.Equal("Novo (1)", Coluna(vm, StatusPedido.Novo).TituloComContagem);
    }

    [Fact]
    public async Task Escolher_um_cartao_mostra_o_detalhe_com_itens_e_historico()
    {
        var p = await PedidoAsync();
        var vm = await CriarAsync();

        vm.SelecionarCommand.Execute(Coluna(vm, StatusPedido.Novo).Cartoes.Single());
        await Task.Delay(200);

        Assert.True(vm.TemSelecao);
        Assert.Contains(p.Numero, vm.DetalheTitulo);
        Assert.Contains("2 × Camiseta", Assert.Single(vm.DetalheItens));
        Assert.Single(vm.DetalheHistorico);
        Assert.Equal("Avançar para Em separação", vm.TextoAvancar);
        Assert.True(vm.PodeAvancar);
        Assert.False(vm.PodeVoltar);
    }

    [Fact]
    public async Task Avancar_e_voltar_movem_o_cartao_de_coluna_e_mantem_a_selecao()
    {
        await PedidoAsync();
        var vm = await CriarAsync();
        vm.Selecionado = Coluna(vm, StatusPedido.Novo).Cartoes.Single();

        await vm.AvancarCommand.ExecuteAsync(null);
        Assert.Empty(Coluna(vm, StatusPedido.Novo).Cartoes);
        Assert.Single(Coluna(vm, StatusPedido.EmSeparacao).Cartoes);
        Assert.Equal(StatusPedido.EmSeparacao, vm.Selecionado!.Pedido.Status);
        Assert.True(vm.PodeVoltar);

        await vm.VoltarCommand.ExecuteAsync(null);
        Assert.Single(Coluna(vm, StatusPedido.Novo).Cartoes);
    }

    [Fact]
    public async Task Cancelar_pede_o_motivo_e_o_pedido_so_aparece_com_mostrar_cancelados()
    {
        await PedidoAsync();
        var vm = await CriarAsync();
        vm.Selecionado = Coluna(vm, StatusPedido.Novo).Cartoes.Single();

        vm.PedirMotivo = _ => null;
        await vm.CancelarPedidoCommand.ExecuteAsync(null);
        Assert.Single(Coluna(vm, StatusPedido.Novo).Cartoes);

        vm.PedirMotivo = _ => "cliente desistiu";
        await vm.CancelarPedidoCommand.ExecuteAsync(null);

        Assert.Empty(Coluna(vm, StatusPedido.Novo).Cartoes);
        Assert.True(vm.MostrarCancelados);
        Assert.Single(vm.Cancelados.Cartoes);
        Assert.False(vm.PodeAvancar);
        Assert.False(vm.PodeCancelar);

        vm.MostrarCancelados = false;
        await Task.Delay(200);
        Assert.Empty(vm.Cancelados.Cartoes);
    }

    [Fact]
    public async Task Busca_filtra_os_cartoes()
    {
        await PedidoAsync("Maria", "Camiseta");
        await PedidoAsync("João", "Boné");
        var vm = await CriarAsync();

        vm.Busca = "boné";
        await Task.Delay(250);

        Assert.Equal("João", Assert.Single(Coluna(vm, StatusPedido.Novo).Cartoes).Cliente);
    }

    // ---------- novo pedido ----------

    [Fact]
    public async Task Novo_pedido_com_produto_e_item_livre_vira_cartao_em_Novo()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 1), 0);
        var vm = await CriarAsync();

        await vm.NovoPedidoCommand.ExecuteAsync(null);
        Assert.True(vm.FormAberto);
        vm.ClienteNome = "Maria";
        vm.ClienteTelefone = "8199999-0000";
        vm.BuscaProduto = "boné";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        vm.ItensForm[0].QuantidadeTexto = "3";
        vm.AdicionarItemLivreCommand.Execute(null);
        vm.ItensForm[1].Nome = "Bordado do nome";
        vm.ItensForm[1].PrecoTexto = "15,00";
        Assert.Contains("104,70", vm.TotalForm.Replace(' ', ' '));

        await vm.SalvarPedidoCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.False(vm.FormAberto);
        var cartao = Assert.Single(Coluna(vm, StatusPedido.Novo).Cartoes);
        Assert.Equal(("Maria", "4 peça(s) · WhatsApp"), (cartao.Cliente, cartao.Resumo));
        Assert.Equal(cartao.Id, vm.Selecionado!.Id);
    }

    [Fact]
    public async Task Item_sem_preco_ou_sem_quantidade_mostra_o_erro_e_nao_salva()
    {
        var vm = await CriarAsync();
        await vm.NovoPedidoCommand.ExecuteAsync(null);
        vm.AdicionarItemLivreCommand.Execute(null);
        vm.ItensForm[0].Nome = "Conserto";
        vm.ItensForm[0].PrecoTexto = "abc";

        await vm.SalvarPedidoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("preço", vm.Mensagem);

        vm.ItensForm[0].PrecoTexto = "10,00";
        vm.ItensForm[0].QuantidadeTexto = "0";
        await vm.SalvarPedidoCommand.ExecuteAsync(null);
        Assert.Contains("quantidade", vm.Mensagem);
        Assert.Empty(await _pedidos.ListarAsync());
    }

    [Fact]
    public async Task Pedido_sem_itens_e_recusado_e_voltar_fecha_o_formulario_sem_salvar()
    {
        var vm = await CriarAsync();
        await vm.NovoPedidoCommand.ExecuteAsync(null);

        await vm.SalvarPedidoCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);

        vm.FecharFormCommand.Execute(null);
        Assert.False(vm.FormAberto);
        Assert.Empty(await _pedidos.ListarAsync());
    }
}
