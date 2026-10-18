using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class CaixaServiceTests
{
    private static readonly DateTime Inicio = new(2026, 10, 18, 8, 0, 0);

    /// <summary>Loja com um produto de R$ 10,00 (estoque 100) e um relógio que a gente controla.</summary>
    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public DateTime Agora { get; set; } = Inicio;
        public VendaService Vendas { get; }
        public CaixaService Caixa { get; }
        public int ProdutoId { get; private set; }

        public Cenario()
        {
            Vendas = new VendaService(Banco.Fabrica, () => Agora);
            Caixa = new CaixaService(Banco.Fabrica, () => Agora);
        }

        public static async Task<Cenario> CriarAsync()
        {
            var c = new Cenario();
            c.ProdutoId = (await new ProdutoService(c.Banco.Fabrica)
                .CadastrarAsync(new DadosProduto("Boné", "111", 1000, 600, 2), 100)).Id;
            return c;
        }

        public Task<Venda> VenderAsync(FormaPagamento forma, int quantidade = 1, long? recebido = null)
        {
            Agora = Agora.AddMinutes(1);
            return Vendas.FinalizarAsync(new PedidoVenda(
                new[] { new ItemCarrinho(ProdutoId, quantidade) }, 0, forma,
                ValorRecebidoCentavos: recebido ?? (forma == FormaPagamento.Dinheiro ? quantidade * 1000L : null)));
        }

        public void Dispose() => Banco.Dispose();
    }

    [Fact]
    public async Task Nao_abre_dois_caixas_ao_mesmo_tempo_nem_com_troco_negativo()
    {
        using var c = await Cenario.CriarAsync();
        await c.Caixa.AbrirAsync(5000);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.AbrirAsync(0));
        await Assert.ThrowsAsync<RegraDeNegocioException>(async () =>
        {
            using var outro = await Cenario.CriarAsync();
            await outro.Caixa.AbrirAsync(-1);
        });
    }

    [Fact]
    public async Task Esperado_em_dinheiro_soma_troco_vendas_e_reforco_e_desconta_sangria_e_despesa()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(5000);                        // 50,00 de troco
        await c.VenderAsync(FormaPagamento.Dinheiro, 2, recebido: 5000);   // 20,00 (troco devolvido ao cliente)
        await c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Reforco, 1000, null);        // +10,00
        await c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.EntradaAvulsa, 500, "Vale"); // +5,00
        await c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 3000, null);        // -30,00
        await c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Despesa, 700, "Café");       // -7,00

        var resumo = await c.Caixa.ResumoAsync(turno.Id);

        Assert.Equal(5000 + 2000 + 1000 + 500 - 3000 - 700, resumo.EsperadoDinheiroCentavos);
        Assert.Equal(4, resumo.Movimentos.Count);
    }

    [Fact]
    public async Task Esperado_separa_cartao_e_pix_e_ignora_vendas_canceladas()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(0);
        await c.VenderAsync(FormaPagamento.Pix);
        await c.VenderAsync(FormaPagamento.CartaoDebito, 2);
        await c.VenderAsync(FormaPagamento.CartaoCredito);
        var cancelada = await c.VenderAsync(FormaPagamento.Pix, 5);
        await c.Vendas.CancelarAsync(cancelada.Id, "Engano");

        var resumo = await c.Caixa.ResumoAsync(turno.Id);

        Assert.Equal(1000, resumo.EsperadoPixCentavos);
        Assert.Equal(3000, resumo.EsperadoCartaoCentavos);
        Assert.Equal(3, resumo.VendasValidas);
        Assert.Equal(1, resumo.VendasCanceladas);
    }

    [Fact]
    public async Task Vendas_de_antes_da_abertura_nao_entram_no_turno()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(FormaPagamento.Pix);          // antes de abrir o caixa
        c.Agora = c.Agora.AddHours(1);
        var turno = await c.Caixa.AbrirAsync(0);
        await c.VenderAsync(FormaPagamento.Pix);

        var resumo = await c.Caixa.ResumoAsync(turno.Id);

        Assert.Equal(1000, resumo.EsperadoPixCentavos);
    }

    [Fact]
    public async Task Movimentos_exigem_caixa_aberto_valor_positivo_e_descricao_nas_despesas()
    {
        using var c = await Cenario.CriarAsync();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Reforco, 100, null));

        await c.Caixa.AbrirAsync(1000);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Reforco, 0, null));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Despesa, 100, " "));
    }

    [Fact]
    public async Task Sangria_ou_despesa_maior_que_o_dinheiro_do_caixa_e_recusada()
    {
        using var c = await Cenario.CriarAsync();
        await c.Caixa.AbrirAsync(1000);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 1001, null));
        await c.Caixa.RegistrarMovimentoAsync(TipoMovimentoCaixa.Sangria, 1000, null); // tirar exatamente tudo pode
    }

    [Fact]
    public async Task Fechar_sem_diferenca_nao_exige_observacao_e_encerra_o_turno()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(1000);
        await c.VenderAsync(FormaPagamento.Dinheiro);
        await c.VenderAsync(FormaPagamento.Pix);

        var f = await c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(2000, 0, 1000));

        Assert.Equal(1, f.Versao);
        Assert.Equal(0, f.DiferencaTotalCentavos);
        Assert.Null(await c.Caixa.ObterAbertoAsync());
        Assert.NotNull((await c.Caixa.ResumoAsync(turno.Id)).Turno.DataFechamento);
    }

    [Fact]
    public async Task Diferenca_exige_observacao_e_fica_registrada()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(1000);
        await c.VenderAsync(FormaPagamento.Dinheiro);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(1900, 0, 0)));
        Assert.NotNull(await c.Caixa.ObterAbertoAsync()); // continua aberto depois da recusa

        var f = await c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(1900, 0, 0, " Faltou troco "));

        Assert.Equal(-100, f.DiferencaDinheiroCentavos);
        Assert.Equal("Faltou troco", f.Observacao);
    }

    [Fact]
    public async Task Valores_conferidos_negativos_sao_recusados()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(0);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(-1, 0, 0, "teste")));
    }

    [Fact]
    public async Task Depois_de_fechar_e_possivel_abrir_outro_caixa_sem_misturar_as_vendas()
    {
        using var c = await Cenario.CriarAsync();
        var primeiro = await c.Caixa.AbrirAsync(0);
        await c.VenderAsync(FormaPagamento.Pix);
        await c.Caixa.FecharAsync(primeiro.Id, new ConferenciaCaixa(0, 0, 1000));

        c.Agora = c.Agora.AddHours(1);
        var segundo = await c.Caixa.AbrirAsync(0);
        await c.VenderAsync(FormaPagamento.Pix, 3);

        Assert.Equal(1000, (await c.Caixa.ResumoAsync(primeiro.Id)).EsperadoPixCentavos);
        Assert.Equal(3000, (await c.Caixa.ResumoAsync(segundo.Id)).EsperadoPixCentavos);
    }

    [Fact]
    public async Task Refazer_a_conferencia_cria_nova_versao_e_mantem_a_anterior()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(0);
        var venda = await c.VenderAsync(FormaPagamento.Pix);
        var v1 = await c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(0, 0, 1000));

        await c.Vendas.CancelarAsync(venda.Id, "Cliente voltou");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(0, 0, 0)));
        var v2 = await c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(0, 0, 0, "Venda cancelada depois do fechamento"));

        Assert.Equal(2, v2.Versao);
        Assert.Equal(0, v2.EsperadoPixCentavos);
        var historico = await c.Caixa.HistoricoAsync();
        Assert.Equal(2, historico.Count);
        var antiga = historico.Single(h => h.Versao == 1);
        Assert.Equal(v1.Id, antiga.Id);
        Assert.Equal(1000, antiga.EsperadoPixCentavos); // a foto original não muda
    }

    [Fact]
    public async Task Descobre_se_um_horario_pertence_a_um_caixa_ja_fechado()
    {
        using var c = await Cenario.CriarAsync();
        var turno = await c.Caixa.AbrirAsync(0);
        var venda = await c.VenderAsync(FormaPagamento.Pix);

        Assert.Null(await c.Caixa.TurnoFechadoDoHorarioAsync(venda.DataHora)); // ainda aberto

        c.Agora = c.Agora.AddHours(1);
        await c.Caixa.FecharAsync(turno.Id, new ConferenciaCaixa(0, 0, 1000));

        Assert.Equal(turno.Id, (await c.Caixa.TurnoFechadoDoHorarioAsync(venda.DataHora))?.Id);
        Assert.Null(await c.Caixa.TurnoFechadoDoHorarioAsync(c.Agora.AddDays(1)));
    }
}
