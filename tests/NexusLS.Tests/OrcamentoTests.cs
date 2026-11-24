using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class OrcamentoTests : IDisposable
{
    private DateTime _agora = new(2026, 11, 10, 9, 0, 0);
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    private OrcamentoService Orcamentos() => new(_banco.Fabrica, () => _agora);
    private PedidoService Pedidos() => new(_banco.Fabrica, () => _agora);

    private static DadosOrcamento Simples(long desconto = 0, int dias = 7) => new(
        new[] { new ItemPedidoDados(null, 10, "Camiseta estampada", 3500, "m"), new ItemPedidoDados(null, 1, "Arte", 5000) },
        ClienteNome: "Escola Sol", ClienteTelefone: "11999990000", ValidadeDias: dias, DescontoCentavos: desconto, Observacao: "Entrega em 10 dias");

    [Fact]
    public async Task Cria_com_numero_total_e_validade_sem_criar_pedido()
    {
        var o = await Orcamentos().CriarAsync(Simples(desconto: 2000));

        Assert.Equal("ORC-2611-0001", o.Numero);
        Assert.Equal(10 * 3500 + 5000 - 2000, o.TotalCentavos);
        Assert.Equal(new DateTime(2026, 11, 17), o.ValidoAte);
        Assert.Equal("M", o.Itens[0].Tamanho);
        Assert.Equal("Aguardando resposta", OrcamentoService.Situacao(o, _agora));
        Assert.Equal("Vencido", OrcamentoService.Situacao(o, _agora.AddDays(8)));
        Assert.Empty(await Pedidos().ListarAsync());
    }

    [Fact]
    public async Task Recusa_dados_ruins()
    {
        var s = Orcamentos();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.CriarAsync(new DadosOrcamento(Array.Empty<ItemPedidoDados>(), ClienteNome: "A")));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.CriarAsync(Simples() with { ClienteNome = " " }));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.CriarAsync(Simples(desconto: 999999)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.CriarAsync(Simples(dias: 0)));
        Assert.Empty(await s.ListarAsync());
    }

    [Fact]
    public async Task Corrigir_troca_os_itens_sem_deixar_sobra_e_mantem_o_numero()
    {
        var o = await Orcamentos().CriarAsync(Simples());
        _agora = _agora.AddDays(3);

        var depois = await Orcamentos().AlterarAsync(o.Id, Simples() with { Itens = new[] { new ItemPedidoDados(null, 2, "Boné", 4000) } });

        Assert.Equal(o.Numero, depois.Numero);
        Assert.Equal(8000, depois.TotalCentavos);
        Assert.Equal(new DateTime(2026, 11, 20), depois.ValidoAte);
        await using var db = _banco.CriarContexto();
        Assert.Equal(1, await db.ItensOrcamento.CountAsync());
    }

    [Fact]
    public async Task Virar_pedido_leva_itens_e_total_com_desconto_uma_vez_so()
    {
        var o = await Orcamentos().CriarAsync(Simples(desconto: 2000));

        var pedido = await Orcamentos().VirarPedidoAsync(o.Id, "Bia");

        Assert.Equal(o.TotalCentavos, pedido.TotalCentavos);
        Assert.Equal(OrigemPedido.Encomenda, pedido.Origem);
        Assert.StartsWith("PED-2611-", pedido.Numero);
        var gravado = await Pedidos().ObterAsync(pedido.Id);
        Assert.Equal(pedido.Numero, gravado.Numero);
        Assert.Equal(2, gravado.Itens.Count);
        Assert.Contains("desconto", gravado.Observacao);
        var aprovado = await Orcamentos().ObterAsync(o.Id);
        Assert.Equal((StatusOrcamento.Aprovado, (int?)pedido.Id), (aprovado.Status, aprovado.PedidoId));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Orcamentos().VirarPedidoAsync(o.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Orcamentos().AlterarAsync(o.Id, Simples()));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Orcamentos().ReabrirAsync(o.Id));
        Assert.Single(await Pedidos().ListarAsync());

        await Pedidos().CancelarAsync(pedido.Id, "cliente desistiu");
        await Orcamentos().ReabrirAsync(o.Id);
        Assert.Equal(StatusOrcamento.Aberto, (await Orcamentos().ObterAsync(o.Id)).Status);
    }

    [Fact]
    public async Task Secretaria_cobra_resposta_do_dia_seguinte_ate_uma_semana_depois_de_vencer()
    {
        var o = await Orcamentos().CriarAsync(Simples());
        async Task<List<TarefaDaSecretaria>> Cobrancas() => (await new SecretariaService(_banco.Fabrica, () => _agora)
            .TarefasAsync(false, false, "Loja X")).Where(t => t.Assunto == AssuntoTarefa.Orcamento).ToList();

        Assert.Empty(await Cobrancas());                      // feito hoje
        _agora = _agora.AddDays(1);
        var tarefa = Assert.Single(await Cobrancas());
        Assert.Equal("11999990000", tarefa.Telefone);
        Assert.Contains(o.Numero, tarefa.Mensagem);
        _agora = _agora.AddDays(14);                          // 8 dias depois de vencer
        Assert.Empty(await Cobrancas());
    }

    [Fact]
    public async Task Pdf_sai_com_o_numero()
    {
        var o = await Orcamentos().CriarAsync(Simples(desconto: 100));
        var pdf = OrcamentoService.GerarPdf(o, new ConfiguracaoLoja { NomeLoja = "Loja X" });

        Assert.StartsWith("%PDF", Encoding.Latin1.GetString(pdf));
        Assert.Equal($"orcamento-{o.Numero}.pdf", OrcamentoService.NomeDoPdf(o));
    }
}
