using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Fila geral por entidade: Guid e carimbo nas entidades operacionais, e a fila para vendas, clientes e outras.</summary>
public sealed class FilaGeralTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid-ana");
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly DateTime _agora = new(2026, 10, 25, 9, 0, 0);

    public FilaGeralTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private List<ItemFilaSincronizacao> Fila(string entidade)
    {
        using var db = _banco.CriarContexto();
        return db.FilaSincronizacao.AsNoTracking().Where(f => f.Entidade == entidade).OrderBy(f => f.Id).ToList();
    }

    private async Task<Produto> ProdutoComEstoque() =>
        await _produtos.CadastrarAsync(new DadosProduto("Camiseta", "C1", 5000, 2000, 1), 10);

    // ---------- migration: dados que já existiam ----------

    [Fact]
    public void Migration_dá_Guid_unico_e_carimbo_as_linhas_antigas_e_converte_a_fila()
    {
        using var banco = new BancoDeTeste(aplicarMigracoes: false);
        var chave = Guid.Parse("11111111-2222-4333-8444-555555555555");
        var guidProduto = Guid.Parse("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE");

        using (var db = banco.CriarContexto())
        {
            db.GetService<IMigrator>().Migrate("20261021202507_VendaChaveUnica"); // o banco como estava antes desta mudança
            db.Database.ExecuteSqlRaw("INSERT INTO Clientes (Nome, Ativo, DataCadastro) VALUES ('Ana', 1, '2026-10-01 10:00:00'), ('Bia', 1, '2026-10-02 10:00:00')");
            db.Database.ExecuteSqlRaw("INSERT INTO Fornecedores (Nome, Ativo, DataCadastro) VALUES ('Forn 1', 1, '2026-10-03 10:00:00')");
            db.Database.ExecuteSqlRaw(
                "INSERT INTO Vendas (DataHora, SubtotalCentavos, DescontoCentavos, TotalCentavos, FormaPagamento, TrocoCentavos, Cancelada, Parcelas, TabelaPreco, ChaveUnica) " +
                $"VALUES ('2026-10-17 10:00:00', 1000, 0, 1000, 3, 0, 0, 1, 1, '{chave.ToString().ToUpperInvariant()}'), ('2026-10-17 11:00:00', 500, 0, 500, 3, 0, 0, 1, 1, NULL)");
            db.GetService<IMigrator>().Migrate("20261022215804_FilaSincronizacao"); // já com a fila de produtos
            db.Database.ExecuteSqlRaw(
                "INSERT INTO FilaSincronizacao (ProdutoGuid, Versao, Estado, Tentativas, CriadoEm, AtualizadoEm) " +
                $"VALUES ('{guidProduto.ToString().ToUpperInvariant()}', 3, 1, 0, '2026-10-20 10:00:00', '2026-10-20 10:00:00')");
        }

        using (var db = banco.CriarContexto())
            db.Database.Migrate();

        using var leitura = banco.CriarContexto();

        var clientes = leitura.Clientes.AsNoTracking().ToList();
        Assert.Equal(2, clientes.Count);
        Assert.All(clientes, c => Assert.NotEqual(Guid.Empty, c.Guid));
        Assert.Equal(2, clientes.Select(c => c.Guid).Distinct().Count());
        Assert.All(clientes, c => Assert.True(c.AtualizadoEm.Year >= 2026));
        // o Guid gravado pelo SQL é encontrado por uma consulta do EF (mesmo formato de texto)
        var guidAna = clientes.Single(x => x.Nome == "Ana").Guid;
        Assert.Equal("Ana", leitura.Clientes.Single(c => c.Guid == guidAna).Nome);
        Assert.NotEqual(Guid.Empty, Assert.Single(leitura.Fornecedores.AsNoTracking().ToList()).Guid);

        var vendas = leitura.Vendas.AsNoTracking().OrderBy(v => v.Id).ToList();
        Assert.Equal(chave, vendas[0].Guid);           // a chave de idempotência virou o id global
        Assert.NotEqual(Guid.Empty, vendas[1].Guid);
        Assert.NotEqual(vendas[0].Guid, vendas[1].Guid);

        var fila = Assert.Single(leitura.FilaSincronizacao.AsNoTracking().ToList());
        Assert.Equal(NomesEntidade.Produto, fila.Entidade);
        Assert.Equal(guidProduto, fila.EntidadeGuid);   // a coluna foi renomeada sem perder o valor
        Assert.Equal(3, fila.Versao);
    }

    // ---------- fila geral ----------

    [Fact]
    public async Task Venda_entra_na_fila_com_o_Guid_da_chave_e_o_cancelamento_reusa_a_mesma_linha()
    {
        var p = await ProdutoComEstoque();
        var chave = Guid.NewGuid();
        var venda = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix, ChaveUnica: chave));

        var linha = Assert.Single(Fila(NomesEntidade.Venda));
        Assert.Equal(chave, linha.EntidadeGuid);
        Assert.Equal(chave, venda.Guid);
        Assert.Equal("uid-ana", linha.UsuarioUid);
        var versao = linha.Versao;

        await _vendas.CancelarAsync(venda.Id, "cliente desistiu");

        var depois = Assert.Single(Fila(NomesEntidade.Venda)); // continua uma linha só
        Assert.True(depois.Versao > versao);
        Assert.Equal(EstadoFila.Pendente, depois.Estado);
    }

    [Fact]
    public async Task Venda_sem_chave_ganha_Guid_proprio()
    {
        var p = await ProdutoComEstoque();
        var venda = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));

        Assert.NotEqual(Guid.Empty, venda.Guid);
        Assert.Equal(venda.Guid, Assert.Single(Fila(NomesEntidade.Venda)).EntidadeGuid);
    }

    [Fact]
    public async Task Venda_tambem_coloca_na_fila_a_movimentacao_de_estoque_e_o_produto()
    {
        var p = await ProdutoComEstoque();
        var antes = Fila(NomesEntidade.MovimentacaoEstoque).Count; // o estoque inicial já gerou uma

        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2) }, 0, FormaPagamento.Pix));

        Assert.Equal(antes + 1, Fila(NomesEntidade.MovimentacaoEstoque).Count);
        Assert.Equal(EstadoFila.Pendente, Assert.Single(Fila(NomesEntidade.Produto)).Estado);
    }

    [Fact]
    public async Task Cliente_e_fornecedor_entram_na_fila_e_editar_nao_duplica()
    {
        await using (var db = _banco.CriarContexto())
        {
            db.Clientes.Add(new Cliente { Nome = "Ana" });
            db.Fornecedores.Add(new Fornecedor { Nome = "Forn 1" });
            await db.SaveChangesAsync();
        }
        var cliente = Assert.Single(Fila(NomesEntidade.Cliente));
        Assert.Single(Fila(NomesEntidade.Fornecedor));

        await using (var db = _banco.CriarContexto())
        {
            var c = await db.Clientes.SingleAsync();
            c.Telefone = "8199999999";
            await db.SaveChangesAsync();
        }

        var depois = Assert.Single(Fila(NomesEntidade.Cliente));
        Assert.Equal(cliente.EntidadeGuid, depois.EntidadeGuid);
        Assert.Equal(cliente.Versao + 1, depois.Versao);
    }

    [Fact]
    public async Task Item_de_pedido_ou_evento_anota_o_pedido_dono()
    {
        Guid guidPedido;
        await using (var db = _banco.CriarContexto())
        {
            var pedido = new Pedido { Numero = "PED-2610-0001", CriadoEm = _agora, AtualizadoEm = _agora };
            db.Pedidos.Add(pedido);
            await db.SaveChangesAsync();
            guidPedido = pedido.Guid;
        }
        var versao = Assert.Single(Fila(NomesEntidade.Pedido)).Versao;

        await using (var db = _banco.CriarContexto()) // só um evento novo, sem tocar no pedido
        {
            var id = await db.Pedidos.Select(p => p.Id).SingleAsync();
            db.EventosPedido.Add(new EventoPedido { PedidoId = id, Status = StatusPedido.EmSeparacao, DataHora = _agora });
            await db.SaveChangesAsync();
        }

        var linha = Assert.Single(Fila(NomesEntidade.Pedido));
        Assert.Equal(guidPedido, linha.EntidadeGuid);
        Assert.Equal(versao + 1, linha.Versao);
    }

    [Fact]
    public async Task Guid_e_unico_e_AtualizadoEm_acompanha_a_alteracao()
    {
        await using var db = _banco.CriarContexto();
        var a = new Cliente { Nome = "Ana" };
        var b = new Cliente { Nome = "Bia" };
        db.Clientes.AddRange(a, b);
        await db.SaveChangesAsync();
        Assert.NotEqual(a.Guid, b.Guid);

        var primeiro = a.AtualizadoEm;
        await Task.Delay(20);
        a.Nome = "Ana Maria";
        await db.SaveChangesAsync();

        Assert.True(a.AtualizadoEm > primeiro);
        Assert.Equal(DateTimeKind.Utc, DateTime.SpecifyKind(a.AtualizadoEm, DateTimeKind.Utc).Kind);
    }

    [Fact]
    public async Task Salvar_sem_mexer_em_nada_nao_muda_o_carimbo_nem_a_fila()
    {
        await using (var db = _banco.CriarContexto())
        {
            db.Clientes.Add(new Cliente { Nome = "Ana" });
            await db.SaveChangesAsync();
        }
        var antes = Assert.Single(Fila(NomesEntidade.Cliente));

        await using (var db = _banco.CriarContexto())
        {
            await db.Clientes.SingleAsync(); // só leu
            await db.SaveChangesAsync();
        }

        Assert.Equal(antes.Versao, Assert.Single(Fila(NomesEntidade.Cliente)).Versao);
    }

    // ---------- o processador do catálogo só mexe em produtos ----------

    [Fact]
    public async Task Processador_do_catalogo_envia_so_produtos_e_deixa_as_vendas_esperando()
    {
        var p = await ProdutoComEstoque();
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));
        var destino = new DestinoFalso();
        var proc = new ProcessadorFila(_banco.Fabrica, new SincronizacaoRepositorio(_banco.Fabrica, () => _agora), destino, () => _agora);

        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.Enviados); // só o produto
        Assert.Equal(p.Guid, Assert.Single(destino.TodasAsOperacoes).Item.Produto.Guid);
        Assert.Equal(EstadoFila.Pendente, Assert.Single(Fila(NomesEntidade.Venda)).Estado); // a venda segue na fila
        var resumo = await proc.ResumoAsync();
        Assert.Equal(0, resumo.Pendentes); // o indicador do catálogo não conta vendas
        Assert.Equal(1, resumo.Enviados);
    }

    [Fact]
    public async Task EnfileirarExistentes_so_cria_linhas_de_produto()
    {
        await ProdutoComEstoque();
        await using (var db = _banco.CriarContexto())
        {
            db.FilaSincronizacao.RemoveRange(db.FilaSincronizacao);
            await db.SaveChangesAsync();
        }
        var proc = new ProcessadorFila(_banco.Fabrica, new SincronizacaoRepositorio(_banco.Fabrica, () => _agora), new DestinoFalso(), () => _agora);

        Assert.Equal(1, await proc.EnfileirarExistentesAsync());
        Assert.Single(Fila(NomesEntidade.Produto));
        Assert.Empty(Fila(NomesEntidade.Venda));
    }
}
