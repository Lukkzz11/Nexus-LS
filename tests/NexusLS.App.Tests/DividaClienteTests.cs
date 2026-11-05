using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Tela de clientes: limite de crédito e recebimento de dívida a prazo.</summary>
public sealed class DividaClienteTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ClienteService _clientes;
    private readonly VendaService _vendas;
    private int _produtoId;

    public DividaClienteTests()
    {
        _clientes = new ClienteService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica);
    }

    public void Dispose() => _banco.Dispose();

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    private async Task<Cliente> PrepararAsync(long limite = 0)
    {
        _produtoId = (await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 50)).Id;
        return await _clientes.CadastrarAsync(new DadosCliente("Maria Souza", LimiteCreditoCentavos: limite));
    }

    private Task<Venda> VenderAPrazoAsync(int clienteId, int qtd) =>
        _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(_produtoId, qtd) }, 0, FormaPagamento.APrazo, ClienteId: clienteId));

    private async Task<ClientesViewModel> AbrirAsync(Cliente cliente)
    {
        var vm = new ClientesViewModel(_clientes);
        await AteAsync(() => vm.Clientes.Count == 1, "lista de clientes");
        vm.Selecionado = vm.Clientes[0];
        await AteAsync(() => vm.IdEditando == cliente.Id && vm.ResumoDivida != "", "abrir o cliente");
        return vm;
    }

    [Fact]
    public async Task Mostra_a_divida_e_o_limite_do_cliente_aberto()
    {
        var maria = await PrepararAsync(limite: 50000);
        await VenderAPrazoAsync(maria.Id, 2);   // 40,00

        var vm = await AbrirAsync(maria);

        Assert.Equal("500,00", vm.LimiteTexto);
        Assert.Contains("Deve R$ 40,00", vm.ResumoDivida.Replace(' ', ' '));
        Assert.Single(vm.Titulos);
        Assert.Equal("Em aberto", vm.Titulos[0].Situacao);
    }

    [Fact]
    public async Task Receber_pagamento_atualiza_a_divida()
    {
        var maria = await PrepararAsync();
        await VenderAPrazoAsync(maria.Id, 2);   // 40,00
        var vm = await AbrirAsync(maria);

        vm.PagamentoValorTexto = "15,00";
        vm.PagamentoForma = vm.FormasDePagamento.Single(f => f.Forma == FormaPagamento.Pix);
        await vm.ReceberPagamentoCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("Deve R$ 25,00", vm.ResumoDivida.Replace(' ', ' '));
        Assert.Equal("", vm.PagamentoValorTexto);
    }

    [Fact]
    public async Task Ajustar_o_titulo_recalcula_a_divida_e_mostra_o_historico()
    {
        var maria = await PrepararAsync();
        await VenderAPrazoAsync(maria.Id, 2);   // 40,00
        var titulos = new TituloPrazoService(_banco.Fabrica);
        var vm = new ClientesViewModel(_clientes, titulos);
        await AteAsync(() => vm.Clientes.Count == 1, "lista de clientes");
        vm.Selecionado = vm.Clientes[0];
        await AteAsync(() => vm.IdEditando == maria.Id && vm.ResumoDivida != "", "abrir o cliente");

        // sem escolher o título, pede para escolher
        await vm.SalvarAjusteTituloCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);

        vm.TituloSelecionado = vm.Titulos.Single();
        Assert.Equal("40,00", vm.AjusteValorTexto);
        vm.AjusteValorTexto = "30,00";
        vm.AjusteMotivo = "";
        await vm.SalvarAjusteTituloCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);                          // motivo é obrigatório

        vm.AjusteMotivo = "desconto combinado";
        await vm.SalvarAjusteTituloCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Contains("Deve R$ 30,00", vm.ResumoDivida.Replace(' ', ' '));
        var ajuste = Assert.Single(vm.Ajustes);
        Assert.Contains("desconto combinado", ajuste);
        Assert.Contains("→", ajuste);
        Assert.StartsWith("ajustado", vm.Titulos.Single().Ajuste);
    }

    [Fact]
    public async Task Pagamento_maior_que_a_divida_mostra_erro()
    {
        var maria = await PrepararAsync();
        await VenderAPrazoAsync(maria.Id, 1);   // 20,00
        var vm = await AbrirAsync(maria);

        vm.PagamentoValorTexto = "99,00";
        await vm.ReceberPagamentoCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
    }

    [Fact]
    public async Task Salvar_o_cliente_guarda_o_limite_digitado_e_recusa_texto_invalido()
    {
        var maria = await PrepararAsync();
        var vm = await AbrirAsync(maria);

        vm.LimiteTexto = "abc";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.True(vm.MensagemEhErro);

        vm.LimiteTexto = "300,00";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.False(vm.MensagemEhErro, vm.Mensagem);
        Assert.Equal(30000, (await _clientes.BuscarAsync(null)).Single().LimiteCreditoCentavos);
    }

    [Fact]
    public async Task Gera_a_tela_de_clientes_com_divida()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var maria = await PrepararAsync(limite: 100000);
        await VenderAPrazoAsync(maria.Id, 3);
        await VenderAPrazoAsync(maria.Id, 1);
        await _clientes.RegistrarPagamentoAsync(maria.Id, 7000, FormaPagamento.Pix);

        ClientesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ClientesViewModel(_clientes));
        await AteAsync(() => vm!.Clientes.Count == 1, "lista");
        Capturador.Rodar(() => vm!.Selecionado = vm.Clientes[0]);
        await AteAsync(() => vm!.ResumoDivida != "", "dívida");
        await Task.Delay(100);

        Capturador.Desenhar(() => new ClientesView { DataContext = vm }, Path.Combine(saida, "17-clientes-divida.png"), 1400, 900);
    }
}
