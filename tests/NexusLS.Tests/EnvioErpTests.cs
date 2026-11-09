using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>O envio ao ERP, sempre com um destino falso (nenhuma rede, nenhum Firebase real).</summary>
public class EnvioErpTests
{
    private sealed class DestinoFalso : IDestinoErp
    {
        public List<IReadOnlyList<DocumentoGravacaoErp>> Chamadas { get; } = new();
        public Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default)
        {
            Chamadas.Add(documentos);
            return Task.CompletedTask;
        }
        public IEnumerable<DocumentoGravacaoErp> Todos => Chamadas.SelectMany(c => c);
    }

    private static ConfiguracaoErp Config(bool ligada) => new() { ProjectId = "projeto-teste", ApiKey = "x", EmpresaId = "empresa-teste", GravacaoAtivada = ligada };

    [Fact]
    public async Task Com_a_gravacao_desligada_nada_e_enviado()
    {
        using var banco = new BancoDeTeste();
        var destino = new DestinoFalso();
        var enviador = new EnviadorErp(banco.Fabrica, Config(false), destino);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => enviador.EnviarAsync());
        Assert.Empty(destino.Chamadas);
    }

    [Fact]
    public async Task Venda_vai_com_estoque_em_incremento_e_kardex_e_repetir_nao_duplica()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1, Referencia: "REF1"), 10);
        await produtos.AdicionarTamanhoAsync(p.Id, "M");
        var vendas = new VendaService(banco.Fabrica);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2, "Sem tamanho") }, 0, FormaPagamento.Pix));

        var destino = new DestinoFalso();
        var enviador = new EnviadorErp(banco.Fabrica, Config(true), destino);
        var r = await enviador.EnviarAsync();

        Assert.Equal((1, 1), (r.Vendas, r.Produtos));
        var venda = destino.Todos.Single(d => d.Caminho.StartsWith("empresas/empresa-teste/vendas/"));
        Assert.True(venda.Criar);
        Assert.Equal("pix", venda.Campos["formaPagamento"]);
        Assert.Equal(100.0, venda.Campos["total"]);
        var baixa = destino.Todos.Single(d => d.Incrementos is not null && d.Caminho.Contains("/produtos/"));
        Assert.Equal(-2, baixa.Incrementos!["estoqueTotal"]);
        Assert.Empty(baixa.Campos);
        Assert.Contains(destino.Todos, d => d.Caminho.Contains("/movimentacoes_estoque/"));
        // venda, baixa e kardex vão na MESMA chamada (tudo ou nada)
        Assert.Contains(destino.Chamadas, c => c.Any(d => d.Caminho.Contains("/vendas/")) && c.Any(d => d.Incrementos is not null));

        var segundo = new DestinoFalso();
        var r2 = await new EnviadorErp(banco.Fabrica, Config(true), segundo).EnviarAsync();
        Assert.Equal((0, 0, 0), (r2.Vendas, r2.Clientes, r2.Produtos));
        Assert.Empty(segundo.Chamadas);
    }

    [Fact]
    public async Task Venda_a_prazo_cria_titulo_e_soma_na_divida_do_cliente_sem_regravar_o_saldo()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Calça", "222", 10000, 4000, 1), 5);
        var clientes = new ClienteService(banco.Fabrica);
        var c = await clientes.CadastrarAsync(new DadosCliente("João", Cidade: "Fortaleza", Estado: "CE", Endereco: "Rua B", Numero: "5"));
        await new VendaService(banco.Fabrica).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.APrazo, ClienteId: c.Id));

        var destino = new DestinoFalso();
        await new EnviadorErp(banco.Fabrica, Config(true), destino).EnviarAsync();

        var cadastro = destino.Todos.First(d => d.Caminho.Contains("/clientes/") && d.Campos.ContainsKey("nome"));
        Assert.Equal("Fortaleza", cadastro.Campos["cidade"]);
        Assert.Equal("CE", cadastro.Campos["estado"]);
        Assert.Equal("Rua B, 5", cadastro.Campos["endereco"]);
        Assert.DoesNotContain("saldoDevedor", cadastro.Mascara);  // a dívida só muda por incremento
        Assert.Contains(destino.Todos, d => d.Caminho.Contains("/contas_receber/"));
        var divida = destino.Todos.Single(d => d.Incrementos is not null && d.Incrementos.ContainsKey("saldoDevedor"));
        Assert.Equal(100.0, divida.Incrementos!["saldoDevedor"]);
    }

    [Fact]
    public async Task Caminho_com_tamanho_numerico_vai_entre_crases()
    {
        Assert.Equal("gradeTamanhos.`36`", FirestoreErpDestino_Caminho("gradeTamanhos.36"));
        Assert.Equal("gradeTamanhos.M", FirestoreErpDestino_Caminho("gradeTamanhos.M"));
        await Task.CompletedTask;
    }

    private static string FirestoreErpDestino_Caminho(string campo) =>
        (string)typeof(FirestoreErpDestino).GetMethod("Caminho", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.Invoke(null, new object[] { campo })!;
}
