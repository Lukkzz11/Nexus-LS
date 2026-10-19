using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha a tela Início num PNG para conferir o visual. Só roda quando a variável NEXUSLS_PRINTS aponta
/// para uma pasta; nos testes normais não faz nada.
/// </summary>
public sealed class CapturaInicio : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Gera_a_tela_inicial()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var agora = new DateTime(2026, 10, 19, 14, 30, 0);
        var produtos = new ProdutoService(_banco.Fabrica);
        var camiseta = (await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 5), 8)).Id;
        var bone = (await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 3), 8)).Id;
        await produtos.CadastrarAsync(new DadosProduto("Camisa Polo Piquet", null, 8990, 3000, 4), 1);
        await produtos.CadastrarAsync(new DadosProduto("Meia Esportiva", null, 1500, 400, 6), 2);
        var maria = await new ClienteService(_banco.Fabrica).CadastrarAsync(new DadosCliente("Maria Souza"));

        await new CaixaService(_banco.Fabrica, () => agora.AddHours(-6)).AbrirAsync(10000);
        var vendas = new VendaService(_banco.Fabrica, () => agora);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta, 2) }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 1) }, 0, FormaPagamento.Pix));
        await new VendaService(_banco.Fabrica, () => agora.AddDays(-45)).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(bone, 3) }, 0, FormaPagamento.APrazo, ClienteId: maria.Id));

        var vm = new InicioViewModel(new InicioService(_banco.Fabrica, () => agora), null, _ => { }, () => agora);
        await vm.Carregamento;

        Capturador.SalvarInicio(vm, Path.Combine(saida, "inicio.png"));
    }
}
