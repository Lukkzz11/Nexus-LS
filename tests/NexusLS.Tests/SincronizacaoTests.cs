using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Regras puras: o que se publica, o hash, a validação e o plano (o que mudou).</summary>
public class RegrasSincronizacaoTests
{
    private static ProdutoPublicavel Camiseta(
        string nome = "Camiseta", long preco = 5990, bool ativo = true, string? codigo = "789",
        params (string, int)[] tamanhos) => new(
        Guid.Parse("11111111-1111-4111-8111-111111111111"), nome, "Roupas", "CAM-01", codigo, preco, false, ativo,
        (tamanhos.Length == 0 ? new[] { ("M", 3), ("P", 2) } : tamanhos)
            .Select(t => new TamanhoPublicavel(t.Item1, t.Item2)).ToList());

    private static readonly IReadOnlyDictionary<Guid, EstadoEnviado> Nada = new Dictionary<Guid, EstadoEnviado>();

    // ---------- hash ----------

    [Fact]
    public void Mesmo_conteudo_mesmo_hash_e_a_ordem_dos_tamanhos_nao_importa()
    {
        var a = Camiseta(tamanhos: new[] { ("M", 3), ("P", 2) });
        var b = Camiseta(tamanhos: new[] { ("P", 2), ("M", 3) });

        Assert.Equal(RegrasSincronizacao.Hash(a), RegrasSincronizacao.Hash(b));
        Assert.Equal(64, RegrasSincronizacao.Hash(a).Length);
    }

    [Fact]
    public void Qualquer_campo_publicado_que_muda_muda_o_hash()
    {
        var baseHash = RegrasSincronizacao.Hash(Camiseta());

        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta(nome: "Camiseta Nova")));
        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta(preco: 6990)));
        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta(ativo: false)));
        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta(codigo: "790")));
        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta(tamanhos: new[] { ("M", 2), ("P", 2) })));   // vendeu 1 M
        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta(tamanhos: new[] { ("M", 3), ("P", 2), ("G", 0) })));
        Assert.NotEqual(baseHash, RegrasSincronizacao.Hash(Camiseta() with { SemControleDeEstoque = true }));
    }

    [Fact]
    public void Dados_que_nao_sao_publicados_nao_mexem_no_hash()
    {
        // Custo, atacado, estoque mínimo, "AtualizadoEm": mudar nada disso gera reenvio nem vaza para a nuvem.
        var produto = new Produto
        {
            Guid = Guid.Parse("22222222-2222-4222-8222-222222222222"), Nome = "Boné", PrecoVendaCentavos = 3000,
            PrecoCustoCentavos = 900, PrecoAtacadoCentavos = 2000, EstoqueMinimo = 3,
            Tamanhos = { new ProdutoTamanho { Tamanho = "Sem tamanho", Estoque = 4 } }
        };
        var antes = RegrasSincronizacao.Hash(RegrasSincronizacao.Montar(produto));

        produto.PrecoCustoCentavos = 1500;
        produto.PrecoAtacadoCentavos = 2500;
        produto.EstoqueMinimo = 10;
        produto.AtualizadoEm = produto.AtualizadoEm.AddDays(5);

        Assert.Equal(antes, RegrasSincronizacao.Hash(RegrasSincronizacao.Montar(produto)));
    }

    [Fact]
    public void Montar_aparara_textos_vazios_e_nao_leva_custo_nem_atacado()
    {
        var p = RegrasSincronizacao.Montar(new Produto
        {
            Nome = "Meia", Categoria = "  ", Referencia = " R1 ", CodigoBarras = "", PrecoVendaCentavos = 1000, PrecoCustoCentavos = 400
        });

        Assert.Null(p.Categoria);
        Assert.Equal("R1", p.Referencia);
        Assert.Null(p.CodigoBarras);
        Assert.DoesNotContain("Custo", typeof(ProdutoPublicavel).GetProperties().Select(x => x.Name));
        Assert.DoesNotContain("PrecoAtacadoCentavos", typeof(ProdutoPublicavel).GetProperties().Select(x => x.Name));
    }

    // ---------- validação ----------

    [Fact]
    public void Produto_bom_nao_tem_erro_e_produto_ruim_lista_todos_os_motivos()
    {
        Assert.Empty(RegrasSincronizacao.Validar(Camiseta()));

        var ruim = new ProdutoPublicavel(Guid.Empty, " ", null, null, null, -1, false, true,
            new[] { new TamanhoPublicavel("M", -2), new TamanhoPublicavel("M", 1), new TamanhoPublicavel(" ", 0) });
        var erros = RegrasSincronizacao.Validar(ruim);

        Assert.Contains("sem identificador global", erros);
        Assert.Contains("sem nome", erros);
        Assert.Contains("preço de venda negativo", erros);
        Assert.Contains("estoque negativo no tamanho M", erros);
        Assert.Contains("tamanho M repetido", erros);
        Assert.Contains("tamanho sem nome", erros);
    }

    // ---------- plano ----------

    [Fact]
    public void Primeira_vez_tudo_e_novo()
    {
        var plano = RegrasSincronizacao.Planejar(new[] { Camiseta() }, Nada);

        Assert.Single(plano.Novos);
        Assert.Empty(plano.Alterados);
        Assert.True(plano.TemQueEnviar);
        Assert.Equal(RegrasSincronizacao.Hash(Camiseta()), plano.Novos[0].Hash);
    }

    [Fact]
    public void Nada_mudou_nada_a_enviar()
    {
        var p = Camiseta();
        var estado = new Dictionary<Guid, EstadoEnviado> { [p.Guid] = new(RegrasSincronizacao.Hash(p), true) };

        var plano = RegrasSincronizacao.Planejar(new[] { p }, estado);

        Assert.False(plano.TemQueEnviar);
        Assert.Equal(1, plano.Inalterados);
    }

    [Fact]
    public void Mudou_o_estoque_vira_alterado()
    {
        var antigo = Camiseta();
        var estado = new Dictionary<Guid, EstadoEnviado> { [antigo.Guid] = new(RegrasSincronizacao.Hash(antigo), true) };

        var plano = RegrasSincronizacao.Planejar(new[] { Camiseta(tamanhos: new[] { ("M", 1), ("P", 2) }) }, estado);

        Assert.Single(plano.Alterados);
        Assert.Equal(0, plano.Inalterados);
    }

    [Fact]
    public void Produto_desativado_que_estava_publicado_e_desativado_no_remoto_e_nunca_apagado()
    {
        var p = Camiseta(ativo: false);
        var estado = new Dictionary<Guid, EstadoEnviado> { [p.Guid] = new("qualquer", true) };

        var plano = RegrasSincronizacao.Planejar(new[] { p }, estado);

        Assert.Single(plano.Desativar);
        Assert.Empty(plano.Novos);
        Assert.Empty(plano.Alterados);
    }

    [Fact]
    public void Desativado_que_nunca_foi_publicado_e_ignorado_e_o_que_ja_estava_desativado_no_remoto_nao_reenvia()
    {
        var p = Camiseta(ativo: false);

        Assert.False(RegrasSincronizacao.Planejar(new[] { p }, Nada).TemQueEnviar);

        var jaDesativado = new Dictionary<Guid, EstadoEnviado> { [p.Guid] = new("x", false) };
        Assert.False(RegrasSincronizacao.Planejar(new[] { p }, jaDesativado).TemQueEnviar);
    }

    [Fact]
    public void Produto_reativado_volta_como_alterado()
    {
        var p = Camiseta();
        var estado = new Dictionary<Guid, EstadoEnviado> { [p.Guid] = new(RegrasSincronizacao.Hash(p), false) };

        var plano = RegrasSincronizacao.Planejar(new[] { p }, estado);

        Assert.Single(plano.Alterados);
    }

    [Fact]
    public void Produto_invalido_vai_para_a_lista_e_nao_atrapalha_os_outros()
    {
        var bom = Camiseta();
        var ruim = Camiseta(nome: " ") with { Guid = Guid.Parse("33333333-3333-4333-8333-333333333333"), CodigoBarras = "111" };

        var plano = RegrasSincronizacao.Planejar(new[] { ruim, bom }, Nada);

        Assert.Single(plano.Novos);
        var problema = Assert.Single(plano.Invalidos);
        Assert.Equal(ruim.Guid, problema.Guid);
        Assert.Contains("sem nome", problema.Motivo);
    }

    [Fact]
    public void Codigo_de_barras_repetido_barra_o_segundo_e_diz_com_quem()
    {
        var a = Camiseta(nome: "A");
        var b = Camiseta(nome: "B") with { Guid = Guid.Parse("44444444-4444-4444-8444-444444444444") };

        var plano = RegrasSincronizacao.Planejar(new[] { a, b }, Nada);

        Assert.Single(plano.Novos);
        Assert.Equal("A", plano.Novos[0].Produto.Nome);
        Assert.Contains("repetido com \"A\"", Assert.Single(plano.Invalidos).Motivo);
    }
}

/// <summary>Banco de verdade: carimbo de alteração, id global, estado e log da sincronização.</summary>
public sealed class SincronizacaoBancoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly VendaService _vendas;
    private readonly SincronizacaoRepositorio _repo;
    private DateTime _agora = new(2026, 10, 19, 10, 0, 0);

    public SincronizacaoBancoTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
        _repo = new SincronizacaoRepositorio(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private Task<Produto> NovoAsync(string nome = "Camiseta", string? codigo = null, int estoque = 10) =>
        _produtos.CadastrarAsync(new DadosProduto(nome, codigo, 5000, 2000, 1), estoque);

    private DateTime AtualizadoEm(int produtoId)
    {
        using var db = _banco.CriarContexto();
        return db.Produtos.AsNoTracking().Single(p => p.Id == produtoId).AtualizadoEm;
    }

    // ---------- id global ----------

    [Fact]
    public async Task Todo_produto_nasce_com_guid_proprio_e_ele_nunca_muda_na_edicao()
    {
        var a = await NovoAsync("A");
        var b = await NovoAsync("B");
        Assert.NotEqual(Guid.Empty, a.Guid);
        Assert.NotEqual(a.Guid, b.Guid);

        await _produtos.EditarAsync(a.Id, new DadosProduto("A renomeado", null, 6000, 2000, 1));
        await _produtos.DesativarAsync(a.Id);

        using var db = _banco.CriarContexto();
        Assert.Equal(a.Guid, db.Produtos.Single(p => p.Id == a.Id).Guid);
    }

    [Fact]
    public async Task Guid_e_unico_no_banco()
    {
        var a = await NovoAsync("A");
        using var db = _banco.CriarContexto();
        db.Produtos.Add(new Produto { Nome = "Clone", Guid = a.Guid });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    // ---------- AtualizadoEm ----------

    [Fact]
    public async Task Cadastro_edicao_ajuste_de_estoque_e_venda_atualizam_o_carimbo()
    {
        var p = await NovoAsync();
        var criado = AtualizadoEm(p.Id);

        await Task.Delay(20);
        await _produtos.EditarAsync(p.Id, new DadosProduto("Camiseta Nova", null, 5000, 2000, 1));
        var aposEdicao = AtualizadoEm(p.Id);
        Assert.True(aposEdicao > criado, "a edição deveria carimbar");

        await Task.Delay(20);
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 5, null);
        var aposAjuste = AtualizadoEm(p.Id);
        Assert.True(aposAjuste > aposEdicao, "o ajuste de estoque deveria carimbar");

        await Task.Delay(20);
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 1) }, 0, FormaPagamento.Pix));
        Assert.True(AtualizadoEm(p.Id) > aposAjuste, "a venda deveria carimbar");
    }

    [Fact]
    public async Task Ajuste_do_estoque_de_um_tamanho_carimba_o_produto_mesmo_sem_ele_estar_carregado()
    {
        var p = await NovoAsync();
        await _produtos.AdicionarTamanhoAsync(p.Id, "M");
        var antes = AtualizadoEm(p.Id);

        await Task.Delay(20);
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 3, null, "M");

        Assert.True(AtualizadoEm(p.Id) > antes);
    }

    [Fact]
    public async Task Ler_e_nao_alterar_nao_carimba()
    {
        var p = await NovoAsync();
        var antes = AtualizadoEm(p.Id);

        await Task.Delay(20);
        await _produtos.BuscarAsync("Camiseta");
        await _repo.ObterPublicaveisAsync();

        Assert.Equal(antes, AtualizadoEm(p.Id));
    }

    // ---------- publicáveis, plano e estado ----------

    [Fact]
    public async Task Publicaveis_trazem_tamanhos_e_estoque_do_banco()
    {
        var p = await NovoAsync("Camiseta", "789", estoque: 4);
        await _produtos.AdicionarTamanhoAsync(p.Id, "M");
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 6, null, "M");

        var publicavel = Assert.Single(await _repo.ObterPublicaveisAsync());

        Assert.Equal(p.Guid, publicavel.Guid);
        Assert.Equal("789", publicavel.CodigoBarras);
        Assert.Contains(publicavel.Tamanhos, t => t.Tamanho == "M" && t.Estoque == 6);
        Assert.Equal(10, publicavel.EstoqueTotal);
    }

    [Fact]
    public async Task Ciclo_completo_novo_depois_inalterado_depois_alterado_pela_venda()
    {
        var p = await NovoAsync("Camiseta", "789", estoque: 10);

        var primeiro = await _repo.PlanejarAsync();
        Assert.Single(primeiro.Novos);

        await _repo.RegistrarEnviadosAsync(primeiro.Novos);

        var segundo = await _repo.PlanejarAsync();
        Assert.False(segundo.TemQueEnviar);
        Assert.Equal(1, segundo.Inalterados);

        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2) }, 0, FormaPagamento.Pix));

        var terceiro = await _repo.PlanejarAsync();
        Assert.Single(terceiro.Alterados);
        Assert.Equal(8, terceiro.Alterados[0].Produto.EstoqueTotal);
    }

    [Fact]
    public async Task Venda_cancelada_devolve_o_estoque_e_o_produto_volta_a_ficar_igual_ao_enviado()
    {
        var p = await NovoAsync("Camiseta", "789", estoque: 10);
        await _repo.RegistrarEnviadosAsync((await _repo.PlanejarAsync()).Novos);

        var venda = await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2) }, 0, FormaPagamento.Pix));
        Assert.True((await _repo.PlanejarAsync()).TemQueEnviar);

        await _vendas.CancelarAsync(venda.Id, "Teste");

        Assert.False((await _repo.PlanejarAsync()).TemQueEnviar); // o hash depende do conteúdo, não de "ter mexido"
    }

    [Fact]
    public async Task Desativar_um_produto_ja_enviado_vira_desativar_e_depois_nada_a_fazer()
    {
        var p = await NovoAsync("Camiseta", "789");
        await _repo.RegistrarEnviadosAsync((await _repo.PlanejarAsync()).Novos);

        await _produtos.DesativarAsync(p.Id);
        var plano = await _repo.PlanejarAsync();
        Assert.Single(plano.Desativar);

        await _repo.RegistrarEnviadosAsync(plano.Desativar);
        Assert.False((await _repo.PlanejarAsync()).TemQueEnviar);

        await _produtos.ReativarAsync(p.Id);
        Assert.Single((await _repo.PlanejarAsync()).Alterados);
    }

    [Fact]
    public async Task Registrar_duas_vezes_atualiza_a_mesma_linha_e_nunca_duplica()
    {
        await NovoAsync();
        var plano = await _repo.PlanejarAsync();

        await _repo.RegistrarEnviadosAsync(plano.Novos);
        await _repo.RegistrarEnviadosAsync(plano.Novos);

        Assert.Single(await _repo.ObterEstadoAsync());
    }

    [Fact]
    public async Task Registrar_so_o_que_foi_confirmado_deixa_o_resto_pendente_para_a_proxima()
    {
        await NovoAsync("A", "1");
        await NovoAsync("B", "2");
        await NovoAsync("C", "3");
        var plano = await _repo.PlanejarAsync();

        await _repo.RegistrarEnviadosAsync(plano.Novos.Take(2)); // o terceiro falhou no envio

        var proximo = await _repo.PlanejarAsync();
        Assert.Single(proximo.Novos);
        Assert.Equal(2, proximo.Inalterados);
    }

    [Fact]
    public async Task Log_guarda_resultado_e_lista_do_mais_recente_para_o_mais_antigo()
    {
        await _repo.GravarLogAsync(new SincronizacaoLog
        {
            IniciadaEm = _agora, ConcluidaEm = _agora.AddSeconds(3), Operador = "Caixa",
            Criados = 5, Alterados = 2, Resultado = ResultadoSincronizacao.Sucesso, Mensagem = "7 produtos enviados"
        });
        await _repo.GravarLogAsync(new SincronizacaoLog
        {
            IniciadaEm = _agora.AddHours(1), Resultado = ResultadoSincronizacao.Falhou, Mensagem = "Sem internet", Erros = 1
        });

        var logs = await _repo.UltimosLogsAsync();

        Assert.Equal(2, logs.Count);
        Assert.Equal(ResultadoSincronizacao.Falhou, logs[0].Resultado);
        Assert.Equal("Sem internet", logs[0].Mensagem);
        Assert.Equal(5, logs[1].Criados);
        Assert.Equal("Caixa", logs[1].Operador);
    }
}

/// <summary>A migration em banco de quem já usava o programa: nada se perde e cada produto ganha o seu id.</summary>
public class MigracaoSincronizacaoTests
{
    [Fact]
    public void Produtos_antigos_ganham_guid_proprio_valido_e_dados_intactos()
    {
        using var banco = new BancoDeTeste(aplicarMigracoes: false);

        using (var db = banco.CriarContexto())
        {
            db.GetService<IMigrator>().Migrate("20261019152425_AbrirNoPdv"); // o banco como estava antes desta mudança
            db.Database.ExecuteSqlRaw(
                "INSERT INTO Produtos (CodigoBarras, Nome, PrecoVendaCentavos, PrecoCustoCentavos, PrecoAtacadoCentavos, EstoqueAtual, EstoqueMinimo, Ativo, VenderSemBaixa) " +
                "VALUES ('111', 'Café', 1000, 600, 800, 7, 2, 1, 0), ('222', 'Pão', 250, 100, 0, 0, 5, 1, 0), (NULL, 'Chá', 500, 200, 0, 3, 1, 0, 0)");
            db.Database.ExecuteSqlRaw("INSERT INTO TamanhosProduto (ProdutoId, Tamanho, Estoque) VALUES (1, 'Sem tamanho', 7), (2, 'Sem tamanho', 0), (3, 'Sem tamanho', 3)");
        }

        using (var db = banco.CriarContexto())
            db.Database.Migrate();

        using var leitura = banco.CriarContexto();
        var produtos = leitura.Produtos.Include(p => p.Tamanhos).OrderBy(p => p.Id).ToList();

        // dados antigos intactos
        Assert.Equal(new[] { "Café", "Pão", "Chá" }, produtos.Select(p => p.Nome));
        Assert.Equal(new long[] { 1000, 250, 500 }, produtos.Select(p => p.PrecoVendaCentavos));
        Assert.Equal(new[] { 7, 0, 3 }, produtos.Select(p => p.Tamanhos.Single().Estoque));
        Assert.False(produtos[2].Ativo);

        // cada um tem o seu id global, válido e diferente
        Assert.All(produtos, p => Assert.NotEqual(Guid.Empty, p.Guid));
        Assert.Equal(3, produtos.Select(p => p.Guid).Distinct().Count());
        // versão 4 do UUID, como se gera em C#
        Assert.All(produtos, p => Assert.Equal('4', p.Guid.ToString()[14]));
        // carimbo preenchido com "agora" (não a data zero)
        Assert.All(produtos, p => Assert.True(p.AtualizadoEm > DateTime.UtcNow.AddMinutes(-5)));
    }

    [Fact]
    public void Guid_gravado_pela_migration_e_encontrado_por_consulta_do_EF_e_aparece_em_publicaveis()
    {
        using var banco = new BancoDeTeste(aplicarMigracoes: false);
        using (var db = banco.CriarContexto())
        {
            db.GetService<IMigrator>().Migrate("20261019152425_AbrirNoPdv");
            db.Database.ExecuteSqlRaw(
                "INSERT INTO Produtos (CodigoBarras, Nome, PrecoVendaCentavos, PrecoCustoCentavos, EstoqueAtual, EstoqueMinimo, Ativo, PrecoAtacadoCentavos, VenderSemBaixa) VALUES ('1', 'Café', 1000, 600, 7, 2, 1, 0, 0)");
        }
        using (var db = banco.CriarContexto()) db.Database.Migrate();

        // A comparação por Guid no SQLite é por texto: a migration precisa gravar no mesmo formato que o EF usa.
        using var leitura = banco.CriarContexto();
        var guid = leitura.Produtos.AsNoTracking().Single().Guid;
        Assert.Single(leitura.Produtos.Where(p => p.Guid == guid).ToList());

        var plano = new SincronizacaoRepositorio(banco.Fabrica).PlanejarAsync().GetAwaiter().GetResult();
        Assert.Single(plano.Novos);
        Assert.Equal(guid, plano.Novos[0].Produto.Guid);
    }

    [Fact]
    public void Migrar_um_banco_sem_produtos_funciona()
    {
        using var banco = new BancoDeTeste(); // aplica todas as migrations em banco vazio
        using var db = banco.CriarContexto();
        Assert.Empty(db.Produtos);
        Assert.Empty(db.SincronizacaoProdutos);
        Assert.Empty(db.SincronizacaoLogs);
    }
}
