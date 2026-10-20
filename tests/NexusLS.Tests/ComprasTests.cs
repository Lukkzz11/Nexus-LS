using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class ComprasTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly DateTime _agora = new(2026, 10, 20, 10, 0, 0);
    private readonly ProdutoService _produtos;
    private readonly FornecedorService _fornecedores;
    private readonly CompraService _compras;
    private readonly FinanceiroService _financeiro;

    public ComprasTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _fornecedores = new FornecedorService(_banco.Fabrica, () => _agora);
        _compras = new CompraService(_banco.Fabrica, () => _agora);
        _financeiro = new FinanceiroService(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private async Task<(int camiseta, int bone)> PrepararAsync()
    {
        var camiseta = (await _produtos.CadastrarAsync(new DadosProduto("Camiseta", "789", 5000, 2000, 2), 0)).Id;
        foreach (var t in new[] { "P", "M" }) await _produtos.AdicionarTamanhoAsync(camiseta, t);
        var bone = (await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 4)).Id;
        return (camiseta, bone);
    }

    // ---------- fornecedores ----------

    [Fact]
    public async Task Cadastra_edita_e_desativa_fornecedor_sem_repetir_nome()
    {
        var f = await _fornecedores.CadastrarAsync(new DadosFornecedor("  Malharia Sol ", "12.345.678/0001-90", "8199999-0000"));
        Assert.Equal("Malharia Sol", f.Nome);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fornecedores.CadastrarAsync(new DadosFornecedor("malharia sol")));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _fornecedores.CadastrarAsync(new DadosFornecedor("  ")));

        await _fornecedores.EditarAsync(f.Id, new DadosFornecedor("Malharia Sol Ltda", Email: "a@b.com"));
        Assert.Equal("Malharia Sol Ltda", (await _fornecedores.BuscarAsync("sol")).Single().Nome);

        await _fornecedores.DesativarAsync(f.Id);
        Assert.Empty(await _fornecedores.BuscarAsync());
        Assert.Single(await _fornecedores.BuscarAsync(incluirInativos: true));
    }

    // ---------- registrar ----------

    [Fact]
    public async Task Compra_soma_o_estoque_por_tamanho_grava_movimentacao_e_atualiza_o_custo()
    {
        var (camiseta, bone) = await PrepararAsync();

        var compra = await _compras.RegistrarAsync(new DadosCompra(new[]
        {
            new ItemCompraDados(camiseta, 10, 2500, "P"),
            new ItemCompraDados(camiseta, 5, 2500, "m"),
            new ItemCompraDados(bone, 6, 900)
        }));

        Assert.Equal(10 * 2500 + 5 * 2500 + 6 * 900, compra.TotalCentavos);
        await using var db = _banco.CriarContexto();
        var c = await db.Produtos.Include(p => p.Tamanhos).SingleAsync(p => p.Id == camiseta);
        Assert.Equal(15, c.EstoqueAtual);
        Assert.Equal(10, c.Tamanhos.Single(t => t.Tamanho == "P").Estoque);
        Assert.Equal(5, c.Tamanhos.Single(t => t.Tamanho == "M").Estoque);
        Assert.Equal(2500, c.PrecoCustoCentavos);
        var b = await db.Produtos.SingleAsync(p => p.Id == bone);
        Assert.Equal((10, 900L), (b.EstoqueAtual, b.PrecoCustoCentavos));
        Assert.Equal(3, db.Movimentacoes.Count(m => m.Motivo == $"Compra nº {compra.Id}"));
    }

    [Fact]
    public async Task Pode_deixar_o_custo_do_produto_como_estava()
    {
        var (_, bone) = await PrepararAsync();

        await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, 5000) }, AtualizarCusto: false));

        await using var db = _banco.CriarContexto();
        Assert.Equal(800, (await db.Produtos.SingleAsync(p => p.Id == bone)).PrecoCustoCentavos);
    }

    [Fact]
    public async Task Compra_com_pagamento_parcelado_gera_as_contas_a_pagar_ligadas_a_compra()
    {
        var (_, bone) = await PrepararAsync();
        var f = await _fornecedores.CadastrarAsync(new DadosFornecedor("Malharia Sol"));

        var compra = await _compras.RegistrarAsync(new DadosCompra(
            new[] { new ItemCompraDados(bone, 10, 1000) }, f.Id, "NF 123",
            Pagamento: new PagamentoCompra(2, new DateTime(2026, 11, 10))));

        var contas = await _financeiro.ListarAsync(new FiltroLancamentos(new DateTime(2026, 11, 1), new DateTime(2026, 12, 31)));
        Assert.Equal(2, contas.Count);
        Assert.Equal(10000, contas.Sum(c => c.ValorCentavos));
        Assert.All(contas, c => Assert.Equal((compra.Id, "Fornecedores", "Malharia Sol"), (c.CompraId, c.Categoria, c.Contraparte)));
        Assert.Contains("Compra nº", contas[0].Descricao);
    }

    [Fact]
    public async Task Compra_paga_na_hora_cria_conta_ja_paga()
    {
        var (_, bone) = await PrepararAsync();

        await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, 1000) },
            Pagamento: new PagamentoCompra(1, _agora.Date, JaPago: true, Forma: FormaPagamento.Pix)));

        var conta = (await _financeiro.ListarAsync(new FiltroLancamentos(_agora.Date, _agora.Date))).Single();
        Assert.Equal(_agora, conta.PagoEm);
    }

    [Fact]
    public async Task Dados_invalidos_nao_gravam_nada_nem_mexem_no_estoque()
    {
        var (camiseta, bone) = await PrepararAsync();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.RegistrarAsync(new DadosCompra(Array.Empty<ItemCompraDados>())));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 0, 100) })));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, -1) })));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(camiseta, 1, 100, "GG") })));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(999, 1, 100) })));
        // um item bom e um ruim: o bom também não pode entrar
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.RegistrarAsync(new DadosCompra(new[]
        {
            new ItemCompraDados(bone, 5, 100), new ItemCompraDados(camiseta, 1, 100, "GG")
        })));

        await using var db = _banco.CriarContexto();
        Assert.Empty(db.Compras);
        Assert.Equal(4, (await db.Produtos.SingleAsync(p => p.Id == bone)).EstoqueAtual);
    }

    // ---------- cancelar ----------

    [Fact]
    public async Task Cancelar_devolve_o_estoque_e_cancela_as_contas_em_aberto()
    {
        var (_, bone) = await PrepararAsync();
        var compra = await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 6, 900) },
            Pagamento: new PagamentoCompra(1, new DateTime(2026, 11, 5))));

        await _compras.CancelarAsync(compra.Id, "pedido errado");

        await using var db = _banco.CriarContexto();
        Assert.Equal(4, (await db.Produtos.SingleAsync(p => p.Id == bone)).EstoqueAtual);
        Assert.True((await db.Compras.SingleAsync()).Cancelada);
        Assert.True((await db.Lancamentos.SingleAsync()).Cancelado);
        Assert.Contains(db.Movimentacoes, m => m.Motivo == $"Compra nº {compra.Id} cancelada" && m.Quantidade == -6);
    }

    [Fact]
    public async Task Nao_cancela_se_a_mercadoria_ja_foi_vendida_ou_a_conta_ja_foi_paga()
    {
        var (_, bone) = await PrepararAsync();
        var vendida = await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 2, 900) }));
        await new VendaService(_banco.Fabrica).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone, 6) }, 0, FormaPagamento.Pix)); // 4+2 = 6 → zera

        var erro = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.CancelarAsync(vendida.Id, "teste"));
        Assert.Contains("já saiu", erro.Message);

        var paga = await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, 900) },
            Pagamento: new PagamentoCompra(1, _agora.Date, JaPago: true, Forma: FormaPagamento.Pix)));
        var erro2 = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.CancelarAsync(paga.Id, "teste"));
        Assert.Contains("Estorne", erro2.Message);
    }

    [Fact]
    public async Task Cancelar_exige_motivo_e_nao_cancela_duas_vezes()
    {
        var (_, bone) = await PrepararAsync();
        var compra = await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, 900) }));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.CancelarAsync(compra.Id, "x"));
        await _compras.CancelarAsync(compra.Id, "engano");
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _compras.CancelarAsync(compra.Id, "de novo"));
    }

    [Fact]
    public async Task Lista_por_periodo_e_fornecedor_com_a_mais_recente_primeiro()
    {
        var (_, bone) = await PrepararAsync();
        var f = await _fornecedores.CadastrarAsync(new DadosFornecedor("Sol"));
        var a = await new CompraService(_banco.Fabrica, () => _agora.AddDays(-3)).RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, 100) }, f.Id));
        var b = await _compras.RegistrarAsync(new DadosCompra(new[] { new ItemCompraDados(bone, 1, 100) }));

        var todas = await _compras.ListarAsync(_agora.AddDays(-10), _agora);
        Assert.Equal(new[] { b.Id, a.Id }, todas.Select(c => c.Id));
        Assert.Equal(a.Id, (await _compras.ListarAsync(_agora.AddDays(-10), _agora, f.Id)).Single().Id);
        Assert.Equal("Sol", (await _compras.ListarAsync(_agora.AddDays(-10), _agora, f.Id)).Single().Fornecedor!.Nome);
    }
}
