using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

[Collection("OperadorAtual")] // mexe no OperadorAtual (estático): não pode rodar junto das outras que também mexem
public class AtividadeTests : IDisposable
{
    private static readonly DateTime Agora = new(2026, 10, 20, 15, 0, 0);

    private readonly BancoDeTeste _banco = new();
    private readonly AtividadeService _atividades;
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly EstoqueService _estoque;
    private readonly FinanceiroService _financeiro;
    private readonly FuncionarioService _funcionarios;

    public AtividadeTests()
    {
        _atividades = new AtividadeService(_banco.Fabrica);
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => Agora);
        _estoque = new EstoqueService(_banco.Fabrica);
        _financeiro = new FinanceiroService(_banco.Fabrica, () => Agora);
        _funcionarios = new FuncionarioService(_banco.Fabrica, () => Agora);
        OperadorAtual.Nome = null;
    }

    public void Dispose()
    {
        OperadorAtual.Nome = null;
        _banco.Dispose();
    }

    private async Task<Produto> NovoProdutoAsync() =>
        await _produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 2), 10);

    [Fact]
    public async Task Cancelar_venda_registra_quem_quando_e_o_motivo()
    {
        var p = await NovoProdutoAsync();
        var venda = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));

        await _vendas.CancelarAsync(venda.Id, "cliente desistiu", canceladoPor: "Ana");

        var a = Assert.Single(await _atividades.ListarAsync());
        Assert.Equal(TipoAtividade.VendaCancelada, a.Tipo);
        Assert.Equal("Ana", a.Operador);
        Assert.Equal(Agora, a.DataHora);
        Assert.Contains("cliente desistiu", a.Descricao);
        Assert.Equal($"Venda #{venda.Id}", a.Referencia);
    }

    [Fact]
    public async Task Cancelamento_recusado_nao_deixa_registro()
    {
        var p = await NovoProdutoAsync();
        var venda = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));

        await Assert.ThrowsAsync<RegraDeNegocioException>(() => _vendas.CancelarAsync(venda.Id, "x"));

        Assert.Empty(await _atividades.ListarAsync());
    }

    [Fact]
    public async Task Sem_operador_informado_usa_o_funcionario_logado()
    {
        OperadorAtual.Nome = "Bruno";
        var p = await NovoProdutoAsync();

        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Perda, 1, "rasgou");

        Assert.Equal("Bruno", Assert.Single(await _atividades.ListarAsync()).Operador);
    }

    [Fact]
    public async Task Entrada_de_estoque_nao_entra_na_trilha_mas_perda_e_correcao_entram()
    {
        var p = await NovoProdutoAsync();

        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 5, null);
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Perda, 2, "danificada");
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Correcao, -1, "contagem");

        var lista = await _atividades.ListarAsync();
        Assert.Equal(2, lista.Count);
        Assert.All(lista, a => Assert.Equal(TipoAtividade.EstoqueAjustado, a.Tipo));
    }

    [Fact]
    public async Task Financeiro_registra_cancelamento_e_estorno()
    {
        var criados = await _financeiro.CriarAsync(new DadosLancamento(
            TipoLancamento.Despesa, "Aluguel", 100000, Agora.AddDays(5)));
        var id = criados[0].Id;
        await _financeiro.PagarAsync(id, FormaPagamento.Pix);
        await _financeiro.EstornarPagamentoAsync(id);
        await _financeiro.CancelarAsync(id, "contrato encerrado");

        var tipos = (await _atividades.ListarAsync()).Select(a => a.Tipo).ToList();
        Assert.Contains(TipoAtividade.PagamentoEstornado, tipos);
        Assert.Contains(TipoAtividade.LancamentoCancelado, tipos);
    }

    [Fact]
    public async Task Funcionarios_registram_cadastro_edicao_pin_e_desativacao()
    {
        await _funcionarios.CriarPrimeiroAdministradorAsync("Admin", "1234");
        var f = await _funcionarios.CadastrarAsync(new DadosFuncionario("Carla", PerfilFuncionario.Vendedor), "4321");
        await _funcionarios.EditarAsync(f.Id, new DadosFuncionario("Carla", PerfilFuncionario.OperadorDeCaixa));
        await _funcionarios.DefinirPinAsync(f.Id, "5678");
        await _funcionarios.DesativarAsync(f.Id);
        await _funcionarios.ReativarAsync(f.Id);

        var tipos = (await _atividades.ListarAsync()).Select(a => a.Tipo).ToHashSet();
        Assert.Superset(new HashSet<TipoAtividade>
        {
            TipoAtividade.FuncionarioCadastrado, TipoAtividade.FuncionarioEditado, TipoAtividade.PinRedefinido,
            TipoAtividade.FuncionarioDesativado, TipoAtividade.FuncionarioReativado
        }, tipos);
    }

    [Fact]
    public async Task O_pin_nunca_aparece_na_descricao()
    {
        var f = await _funcionarios.CriarPrimeiroAdministradorAsync("Admin", "1234");
        await _funcionarios.DefinirPinAsync(f.Funcionario.Id, "98765");

        Assert.All(await _atividades.ListarAsync(), a => Assert.DoesNotContain("98765", a.Descricao));
    }

    [Fact]
    public async Task Filtros_por_tipo_periodo_e_texto_e_ordem_mais_recente_primeiro()
    {
        using (var db = _banco.CriarContexto())
        {
            AtividadeService.Registrar(db, TipoAtividade.PedidoCancelado, "Cancelou o pedido A", "Pedido A", "Ana", new DateTime(2026, 10, 18, 9, 0, 0));
            AtividadeService.Registrar(db, TipoAtividade.VendaCancelada, "Cancelou a venda #7", "Venda #7", "Bruno", new DateTime(2026, 10, 19, 9, 0, 0));
            AtividadeService.Registrar(db, TipoAtividade.VendaCancelada, "Cancelou a venda #8", "Venda #8", "Ana", new DateTime(2026, 10, 20, 9, 0, 0));
            await db.SaveChangesAsync();
        }

        var todas = await _atividades.ListarAsync();
        Assert.Equal(new[] { "Venda #8", "Venda #7", "Pedido A" }, todas.Select(a => a.Referencia));

        Assert.Equal(2, (await _atividades.ListarAsync(new FiltroAtividades(Tipo: TipoAtividade.VendaCancelada))).Count);
        Assert.Single(await _atividades.ListarAsync(new FiltroAtividades(De: new DateTime(2026, 10, 20), Ate: new DateTime(2026, 10, 20))));
        Assert.Equal(2, (await _atividades.ListarAsync(new FiltroAtividades(Texto: "ana"))).Count);
        Assert.Single(await _atividades.ListarAsync(new FiltroAtividades(Texto: "#7")));
    }
}
