using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

[Collection("OperadorAtual")]
public sealed class ProducaoServiceTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly ProducaoService _servico;
    private DateTime _agora = new(2026, 11, 1, 9, 0, 0);

    public ProducaoServiceTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _servico = new ProducaoService(_banco.Fabrica, _produtos, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private static Dictionary<string, int> Grade(params (string tam, int qtd)[] itens) => itens.ToDictionary(i => i.tam, i => i.qtd);

    private Task<OrdemProducao> NovaOrdem(string modelo = "Camiseta Surf", string? referencia = "CS-01", params (string, int)[] grade) =>
        _servico.CriarOrdemAsync(new DadosOrdemProducao(modelo, Grade(grade.Length == 0 ? new[] { ("P", 10), ("M", 20), ("G", 5) } : grade),
            Referencia: referencia, Categoria: "Camisetas", CustoEstimadoPecaCentavos: 2500));

    // ---------------- matéria-prima

    [Fact]
    public async Task Materia_prima_cadastra_edita_e_avisa_do_estoque_minimo()
    {
        var tecido = await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Malha PV", "Tecido", "metros", 50, 1890), estoqueInicial: 30.5m);
        await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Linha branca", "Linha", "un", 5, 450), estoqueInicial: 40);

        var baixas = await _servico.ListarMateriasAsync(soAbaixoDoMinimo: true);
        Assert.Equal("Malha PV", Assert.Single(baixas).Nome);
        Assert.Equal(30.5m, baixas[0].EstoqueAtual);

        await _servico.EditarMateriaAsync(tecido.Id, new DadosMateriaPrima("Malha PV 30.1", "Tecido", "metros", 10, 2000, "Têxtil Sul", "Prateleira 2"));
        Assert.Empty(await _servico.ListarMateriasAsync(soAbaixoDoMinimo: true));
        Assert.Equal("Têxtil Sul", (await _servico.ListarMateriasAsync()).Single(m => m.Id == tecido.Id).Fornecedor);
    }

    [Fact]
    public async Task Materia_prima_recusa_nome_repetido_estoque_negativo_e_saldo_que_ficaria_negativo()
    {
        var m = await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Zíper 15cm", "Zíper", "un", 0, 100), 10);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarMateriaAsync(new DadosMateriaPrima(" zíper 15CM ", null, null, 0, 0)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarMateriaAsync(new DadosMateriaPrima("", null, null, 0, 0)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CadastrarMateriaAsync(new DadosMateriaPrima("X", null, null, -1, 0)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AjustarEstoqueMateriaAsync(m.Id, -10.5m));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AjustarEstoqueMateriaAsync(m.Id, 0));

        Assert.Equal(7.5m, (await _servico.AjustarEstoqueMateriaAsync(m.Id, -2.5m)).EstoqueAtual);
        Assert.Equal(17.5m, (await _servico.AjustarEstoqueMateriaAsync(m.Id, 10)).EstoqueAtual);
    }

    [Fact]
    public async Task Materia_desativada_some_da_lista_mas_nao_e_apagada()
    {
        var m = await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Botão", null, null, 0, 0));
        await _servico.DefinirMateriaAtivaAsync(m.Id, false);

        Assert.Empty(await _servico.ListarMateriasAsync());
        Assert.Single(await _servico.ListarMateriasAsync(incluirInativas: true));
    }

    // ---------------- ordens

    [Fact]
    public async Task Ordem_recebe_codigo_sequencial_do_ano_com_a_grade_e_o_total()
    {
        var a = await NovaOrdem();
        var b = await NovaOrdem("Bermuda", "BM-1", ("38", 8), ("40", 8));

        Assert.Equal("OP-2026-0001", a.Codigo);
        Assert.Equal("OP-2026-0002", b.Codigo);
        Assert.Equal(35, a.TotalPlanejado);
        Assert.Equal(35 * 2500, a.CustoEstimadoTotalCentavos);
        Assert.Equal(StatusOrdemProducao.Planejamento, a.Status);

        _agora = new DateTime(2027, 1, 5);
        Assert.Equal("OP-2027-0001", (await NovaOrdem("Boné", "BN-1", ("Sem tamanho", 3))).Codigo);
    }

    [Fact]
    public async Task Ordem_recusa_modelo_vazio_grade_vazia_ou_negativa_e_normaliza_tamanhos()
    {
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarOrdemAsync(new DadosOrdemProducao("", Grade(("P", 1)))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarOrdemAsync(new DadosOrdemProducao("X", Grade(("P", 0)))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.CriarOrdemAsync(new DadosOrdemProducao("X", Grade(("P", -2)))));

        var o = await _servico.CriarOrdemAsync(new DadosOrdemProducao("Regata", new Dictionary<string, int> { ["p"] = 3, ["P"] = 2, ["gg"] = 4, ["M"] = 0 }));
        Assert.Equal(new[] { "GG:4", "P:5" }, o.Grade.OrderBy(g => g.Tamanho).Select(g => $"{g.Tamanho}:{g.Quantidade}").ToArray());
    }

    [Fact]
    public async Task Etapas_avancam_na_ordem_e_a_concluida_so_vem_pela_transferencia()
    {
        var o = await NovaOrdem();

        Assert.Equal(StatusOrdemProducao.Corte, (await _servico.AvancarEtapaAsync(o.Id)).Status);
        Assert.Equal(StatusOrdemProducao.Costura, (await _servico.AvancarEtapaAsync(o.Id)).Status);
        Assert.Equal(StatusOrdemProducao.Estampa, (await _servico.AvancarEtapaAsync(o.Id)).Status);
        Assert.Equal(StatusOrdemProducao.Acabamento, (await _servico.AvancarEtapaAsync(o.Id)).Status);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AvancarEtapaAsync(o.Id)); // do acabamento só a transferência conclui
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.MudarEtapaAsync(o.Id, StatusOrdemProducao.Concluida));
    }

    [Fact]
    public async Task Ordem_cancelada_nao_muda_mais_e_nao_transfere()
    {
        var o = await NovaOrdem();
        await _servico.CancelarOrdemAsync(o.Id);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.AvancarEtapaAsync(o.Id));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.MudarEtapaAsync(o.Id, StatusOrdemProducao.Corte));
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.TransferirParaLojaAsync(o.Id, new DadosTransferenciaFabrica(Grade(("P", 1)))));
        Assert.Contains("cancelada", ex.Message);
        Assert.Empty(await _servico.ListarOrdensAsync(incluirEncerradas: false));
    }

    // ---------------- perdas

    [Fact]
    public async Task Perda_com_materia_baixa_o_estoque_e_calcula_o_custo()
    {
        var tecido = await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Malha PV", "Tecido", "metros", 0, 1890), 20);
        var ordem = await NovaOrdem();

        var perda = await _servico.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.FalhaNoCorte, 2.5m, MateriaPrimaId: tecido.Id, OrdemProducaoId: ordem.Id));

        Assert.Equal(4725, perda.CustoEstimadoCentavos);   // 2,5 m × R$ 18,90
        Assert.Equal("metros", perda.Unidade);
        Assert.Equal("OP-2026-0001", perda.OrdemCodigo);
        Assert.True(perda.BaixouEstoque);
        Assert.Equal(17.5m, (await _servico.ListarMateriasAsync()).Single().EstoqueAtual);
    }

    [Fact]
    public async Task Perda_sem_baixar_ou_sem_materia_cadastrada_nao_mexe_no_estoque()
    {
        var tecido = await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Malha", null, "kg", 0, 1000), 5);

        var semBaixa = await _servico.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.SobraDeRolo, 1, MateriaPrimaId: tecido.Id, BaixarDoEstoque: false));
        var avulsa = await _servico.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.Outro, 3, MaterialNome: "Retalho", CustoEstimadoCentavos: 900));

        Assert.False(semBaixa.BaixouEstoque);
        Assert.False(avulsa.BaixouEstoque);
        Assert.Equal(900, avulsa.CustoEstimadoCentavos);
        Assert.Equal(5m, (await _servico.ListarMateriasAsync()).Single().EstoqueAtual);
        Assert.Equal(2, (await _servico.ListarPerdasAsync()).Count);
    }

    [Fact]
    public async Task Perda_recusa_quantidade_invalida_material_ausente_e_estoque_insuficiente_sem_gravar_nada()
    {
        var tecido = await _servico.CadastrarMateriaAsync(new DadosMateriaPrima("Malha", null, "kg", 0, 1000), 2);

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.Outro, 0, MaterialNome: "X")));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.Outro, 1)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.RegistrarPerdaAsync(new DadosPerda(MotivoPerda.Outro, 3, MateriaPrimaId: tecido.Id)));

        Assert.Empty(await _servico.ListarPerdasAsync());
        Assert.Equal(2m, (await _servico.ListarMateriasAsync()).Single().EstoqueAtual);
    }

    // ---------------- transferência para a loja

    private async Task<int> EstoqueDe(int produtoId, string tamanho)
    {
        await using var db = _banco.CriarContexto();
        return (await db.TamanhosProduto.SingleAsync(t => t.ProdutoId == produtoId && t.Tamanho == tamanho)).Estoque;
    }

    [Fact]
    public async Task Transferencia_cria_o_produto_da_loja_com_a_grade_o_historico_e_conclui_a_ordem()
    {
        var ordem = await NovaOrdem();

        var r = await _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(
            Grade(("P", 10), ("M", 20), ("G", 5)), PrecoVendaCentavos: 8990, PrecoAtacadoCentavos: 5990));

        Assert.True(r.ProdutoNovo);
        Assert.Equal(35, r.TotalPecas);
        await using var db = _banco.CriarContexto();
        var produto = await db.Produtos.Include(p => p.Tamanhos).SingleAsync(p => p.Id == r.ProdutoId);
        Assert.Equal("Camiseta Surf", produto.Nome);
        Assert.Equal("CS-01", produto.Referencia);
        Assert.Equal(8990, produto.PrecoVendaCentavos);
        Assert.Equal(2500, produto.PrecoCustoCentavos);        // o custo estimado da ordem vira o custo do produto
        Assert.Equal(35, produto.EstoqueAtual);
        Assert.Equal(20, produto.Tamanhos.Single(t => t.Tamanho == "M").Estoque);
        Assert.Equal(3, await db.Movimentacoes.CountAsync(m => m.ProdutoId == produto.Id && m.Motivo!.Contains("OP-2026-0001")));

        var fechada = await db.OrdensProducao.SingleAsync();
        Assert.Equal(StatusOrdemProducao.Concluida, fechada.Status);
        Assert.Equal(r.ProdutoId, fechada.ProdutoDestinoId);
        Assert.Equal(_agora, fechada.TransferidaEm);
    }

    [Fact]
    public async Task Transferencia_soma_no_produto_que_ja_existe_pela_referencia_e_cria_o_tamanho_que_faltava()
    {
        var existente = await _produtos.CadastrarAsync(new DadosProduto("Camiseta Surf Clássica", null, 7990, 2000, 0, "cs-01"), 4);
        var ordem = await NovaOrdem(referencia: "CS-01");

        var r = await _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(Grade(("Sem tamanho", 6), ("XG", 3))));

        Assert.False(r.ProdutoNovo);
        Assert.Equal(existente.Id, r.ProdutoId);
        Assert.Equal(10, await EstoqueDe(existente.Id, "Sem tamanho"));   // 4 + 6
        Assert.Equal(3, await EstoqueDe(existente.Id, "XG"));
        await using var db = _banco.CriarContexto();
        Assert.Equal(13, (await db.Produtos.SingleAsync()).EstoqueAtual);
        Assert.Equal(1, await db.Produtos.CountAsync()); // não criou um segundo produto
    }

    [Fact]
    public async Task Transferencia_sem_referencia_acha_o_produto_pelo_mesmo_nome()
    {
        var existente = await _produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", null, 3990, 1000, 0), 0);
        var ordem = await _servico.CriarOrdemAsync(new DadosOrdemProducao("boné aba reta", Grade(("Sem tamanho", 12))));

        var r = await _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(Grade(("Sem tamanho", 12))));

        Assert.False(r.ProdutoNovo);
        Assert.Equal(existente.Id, r.ProdutoId);
        Assert.Equal(12, await EstoqueDe(existente.Id, "Sem tamanho"));
    }

    [Fact]
    public async Task Transferencia_pode_ir_para_um_produto_escolhido_e_a_ordem_nao_transfere_duas_vezes()
    {
        var destino = await _produtos.CadastrarAsync(new DadosProduto("Outro nome", null, 5000, 1000, 0, "OUTRA"), 0);
        var ordem = await NovaOrdem();

        var r = await _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(Grade(("Sem tamanho", 9)), ProdutoId: destino.Id));
        Assert.Equal(destino.Id, r.ProdutoId);
        Assert.Equal(9, await EstoqueDe(destino.Id, "Sem tamanho"));

        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() =>
            _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(Grade(("Sem tamanho", 9)), ProdutoId: destino.Id)));
        Assert.Contains("já foi transferida", ex.Message);
        Assert.Equal(9, await EstoqueDe(destino.Id, "Sem tamanho")); // nada mudou na segunda tentativa
    }

    [Fact]
    public async Task Transferencia_invalida_nao_cria_produto_nem_mexe_na_ordem()
    {
        var ordem = await NovaOrdem();

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(Grade(("P", 0)))));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.TransferirParaLojaAsync(ordem.Id, new DadosTransferenciaFabrica(Grade(("P", 1)), ProdutoId: 999)));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _servico.TransferirParaLojaAsync(999, new DadosTransferenciaFabrica(Grade(("P", 1)))));

        await using var db = _banco.CriarContexto();
        Assert.Equal(0, await db.Produtos.CountAsync());
        Assert.Equal(StatusOrdemProducao.Planejamento, (await db.OrdensProducao.SingleAsync()).Status);
    }
}
