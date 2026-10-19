using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Destino de mentira: guarda o que "enviou" e falha quando o teste manda.</summary>
internal sealed class DestinoFalso : IDestinoCatalogo
{
    public List<List<OperacaoCatalogo>> Lotes { get; } = new();
    public List<string> Atualizacoes { get; } = new();
    public FalhaDestinoException? FalhaNaVerificacao { get; set; }

    /// <summary>Decide a falha de cada chamada de envio (nulo = funciona). Recebe o número da chamada (1, 2, 3...) e o lote.</summary>
    public Func<int, IReadOnlyList<OperacaoCatalogo>, FalhaDestinoException?>? Falha { get; set; }
    public Func<CancellationToken, Task>? AntesDeEnviar { get; set; }
    public FalhaDestinoException? FalhaNaAtualizacao { get; set; }
    public int Chamadas { get; private set; }

    public IEnumerable<OperacaoCatalogo> TodasAsOperacoes => Lotes.SelectMany(l => l);

    public Task VerificarAsync(CancellationToken ct = default) =>
        FalhaNaVerificacao is null ? Task.CompletedTask : Task.FromException(FalhaNaVerificacao);

    public async Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default)
    {
        Chamadas++;
        if (AntesDeEnviar is not null) await AntesDeEnviar(ct);
        ct.ThrowIfCancellationRequested(); // como o HttpClient de verdade: cancelou, não envia
        var falha = Falha?.Invoke(Chamadas, lote);
        if (falha is not null) throw falha;
        Lotes.Add(lote.ToList());
    }

    public Task RegistrarAtualizacaoAsync(DateTime agora, int totalProdutosAtivos, CancellationToken ct = default)
    {
        if (FalhaNaAtualizacao is not null) return Task.FromException(FalhaNaAtualizacao);
        Atualizacoes.Add($"{agora:yyyy-MM-dd HH:mm}:{totalProdutosAtivos}");
        return Task.CompletedTask;
    }
}

public sealed class SincronizadorTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly SincronizacaoRepositorio _repo;
    private readonly DestinoFalso _destino = new();
    private readonly DateTime _agora = new(2026, 10, 19, 15, 0, 0);

    public SincronizadorTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
        _repo = new SincronizacaoRepositorio(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private SincronizadorCatalogo Criar(int lote = SincronizadorCatalogo.TamanhoPadraoDoLote) =>
        new(_repo, _destino, () => _agora, lote);

    private async Task<Produto[]> CadastrarAsync(int quantidade)
    {
        var lista = new List<Produto>();
        for (var i = 1; i <= quantidade; i++)
            lista.Add(await _produtos.CadastrarAsync(new DadosProduto($"Produto {i}", $"C{i:000}", 1000 + i, 400, 1), 10));
        return lista.ToArray();
    }

    private async Task<(PlanoSincronizacao Plano, ResultadoSincronizacaoCatalogo Resultado)> SincronizarAsync(SincronizadorCatalogo s)
    {
        var preparo = await s.PrepararAsync("Caixa");
        Assert.NotNull(preparo.Plano);
        return (preparo.Plano!, await s.ExecutarAsync(preparo.Plano!, "Caixa"));
    }

    // ---------- caminho feliz ----------

    [Fact]
    public async Task Primeira_sincronizacao_envia_tudo_anota_o_estado_e_grava_o_log()
    {
        await CadastrarAsync(3);
        var (_, r) = await SincronizarAsync(Criar());

        Assert.Equal(ResultadoSincronizacao.Sucesso, r.Log.Resultado);
        Assert.Equal((3, 0, 0), (r.Log.Criados, r.Log.Alterados, r.Log.Desativados));
        Assert.Equal(3, _destino.TodasAsOperacoes.Count());
        Assert.Equal(3, (await _repo.ObterEstadoAsync()).Count);
        Assert.Single(_destino.Atualizacoes);
        Assert.EndsWith(":3", _destino.Atualizacoes[0]);

        var log = Assert.Single(await _repo.UltimosLogsAsync());
        Assert.Equal("Caixa", log.Operador);
        Assert.NotNull(log.ConcluidaEm);
        Assert.Contains("3 produto(s) enviado(s)", log.Mensagem);
    }

    [Fact]
    public async Task Segunda_vez_sem_mudancas_nao_envia_nada_e_diz_isso()
    {
        await CadastrarAsync(2);
        await SincronizarAsync(Criar());
        var chamadasAntes = _destino.Chamadas;

        var (plano, r) = await SincronizarAsync(Criar());

        Assert.False(plano.TemQueEnviar);
        Assert.Equal(ResultadoSincronizacao.SemMudancas, r.Log.Resultado);
        Assert.Equal(chamadasAntes, _destino.Chamadas);
        Assert.Equal(2, r.Log.Inalterados);
        Assert.Contains("Nada a enviar", r.Log.Mensagem);
    }

    [Fact]
    public async Task So_o_que_mudou_e_reenviado()
    {
        var produtos = await CadastrarAsync(3);
        await SincronizarAsync(Criar());
        _destino.Lotes.Clear();

        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produtos[1].Id, 2) }, 0, FormaPagamento.Pix));
        await _produtos.DesativarAsync(produtos[2].Id);
        var (_, r) = await SincronizarAsync(Criar());

        Assert.Equal((0, 1, 1), (r.Log.Criados, r.Log.Alterados, r.Log.Desativados));
        Assert.Equal(1, r.Log.Inalterados);
        var enviados = _destino.TodasAsOperacoes.ToList();
        Assert.Equal(2, enviados.Count);
        Assert.Contains(enviados, o => o.Item.Produto.Guid == produtos[1].Guid && o.Tipo == TipoOperacao.Gravar);
        Assert.Contains(enviados, o => o.Item.Produto.Guid == produtos[2].Guid && o.Tipo == TipoOperacao.Desativar && !o.Item.Produto.Ativo);
    }

    [Fact]
    public async Task Divide_em_lotes_do_tamanho_pedido()
    {
        await CadastrarAsync(5);

        await SincronizarAsync(Criar(lote: 2));

        Assert.Equal(new[] { 2, 2, 1 }, _destino.Lotes.Select(l => l.Count));
    }

    [Fact]
    public async Task Tamanho_de_lote_invalido_e_recusado()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Criar(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Criar(501));
        Criar(500); // o máximo do Firestore é permitido
    }

    // ---------- falhas ----------

    [Fact]
    public async Task Sem_conexao_antes_de_comecar_nao_envia_nada_e_registra_a_tentativa()
    {
        await CadastrarAsync(2);
        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem conexão com a internet.");

        var preparo = await Criar().PrepararAsync("Caixa");

        Assert.Null(preparo.Plano);
        Assert.Equal("Sem conexão com a internet.", preparo.Erro);
        Assert.Equal(0, _destino.Chamadas);
        var log = Assert.Single(await _repo.UltimosLogsAsync());
        Assert.Equal(ResultadoSincronizacao.Falhou, log.Resultado);
        Assert.Equal("Sem conexão com a internet.", log.Mensagem);
        Assert.Empty(await _repo.ObterEstadoAsync());
    }

    [Fact]
    public async Task Queda_no_meio_guarda_o_que_foi_confirmado_e_a_proxima_continua_de_onde_parou()
    {
        await CadastrarAsync(5);
        _destino.Falha = (n, _) => n == 3 ? new FalhaDestinoException("A conexão caiu.") : null; // 2 lotes ok, o 3º cai
        var (_, r) = await SincronizarAsync(Criar(lote: 2));

        Assert.Equal(ResultadoSincronizacao.Parcial, r.Log.Resultado);
        Assert.Equal(4, r.Log.Criados);
        Assert.Equal(1, r.Log.Erros);
        Assert.Contains("Interrompida: A conexão caiu.", r.Log.Mensagem);
        Assert.Equal(4, (await _repo.ObterEstadoAsync()).Count); // só os confirmados foram anotados

        // volta a conexão: a próxima envia só o que faltou, sem repetir os 4
        _destino.Falha = null;
        _destino.Lotes.Clear();
        var (plano, r2) = await SincronizarAsync(Criar(lote: 2));

        Assert.Single(plano.Novos);
        Assert.Equal(4, plano.Inalterados);
        Assert.Equal(ResultadoSincronizacao.Sucesso, r2.Log.Resultado);
        Assert.Single(_destino.TodasAsOperacoes);
    }

    [Fact]
    public async Task Falha_no_primeiro_lote_e_falha_total_e_nada_e_anotado()
    {
        await CadastrarAsync(3);
        _destino.Falha = (_, _) => new FalhaDestinoException("Sem permissão para gravar.");

        var (_, r) = await SincronizarAsync(Criar());

        Assert.Equal(ResultadoSincronizacao.Falhou, r.Log.Resultado);
        Assert.Equal(3, r.Log.Erros);
        Assert.Empty(await _repo.ObterEstadoAsync());
        Assert.Empty(_destino.Atualizacoes);
    }

    [Fact]
    public async Task Dado_recusado_pelo_servidor_isola_o_produto_culpado_e_envia_os_outros()
    {
        var produtos = await CadastrarAsync(4);
        var culpado = produtos[2].Guid;
        _destino.Falha = (_, lote) =>
            lote.Any(o => o.Item.Produto.Guid == culpado)
                ? new FalhaDestinoException("campo inválido", GravidadeFalha.DadosRecusados)
                : null;

        var (_, r) = await SincronizarAsync(Criar(lote: 4));

        Assert.Equal(ResultadoSincronizacao.Parcial, r.Log.Resultado);
        Assert.Equal(3, r.Log.Criados);
        Assert.Equal(1, r.Log.Erros);
        Assert.Equal("Produto 3: campo inválido", Assert.Single(r.FalhasPorProduto));
        var estado = await _repo.ObterEstadoAsync();
        Assert.Equal(3, estado.Count);
        Assert.DoesNotContain(culpado, estado.Keys);

        // o culpado continua pendente na próxima
        Assert.Single((await _repo.PlanejarAsync()).Novos);
    }

    [Fact]
    public async Task Produtos_invalidos_sao_pulados_e_contados_sem_atrapalhar_os_demais()
    {
        await CadastrarAsync(2);
        using (var db = _banco.CriarContexto())
        {
            db.Produtos.Add(new Produto { Nome = "Estoque ruim", PrecoVendaCentavos = 100, Tamanhos = { new ProdutoTamanho { Tamanho = "Sem tamanho", Estoque = -3 } } });
            db.SaveChanges();
        }

        var (plano, r) = await SincronizarAsync(Criar());

        Assert.Single(plano.Invalidos);
        Assert.Equal(ResultadoSincronizacao.Parcial, r.Log.Resultado);
        Assert.Equal(2, r.Log.Criados);
        Assert.Equal(1, r.Log.Ignorados);
        Assert.DoesNotContain(_destino.TodasAsOperacoes, o => o.Item.Produto.Nome == "Estoque ruim");
        Assert.Contains("1 ignorado(s) por dados inválidos", r.Log.Mensagem);
        Assert.Single(r.Invalidos);
    }

    [Fact]
    public async Task Falha_so_ao_anotar_a_data_nao_desfaz_o_envio()
    {
        await CadastrarAsync(2);
        _destino.FalhaNaAtualizacao = new FalhaDestinoException("sem permissão em meta");

        var (_, r) = await SincronizarAsync(Criar());

        Assert.Equal(2, r.Log.Criados);
        Assert.Equal(2, (await _repo.ObterEstadoAsync()).Count);
        Assert.Contains("Não foi possível anotar a data", Assert.Single(r.FalhasPorProduto));
        Assert.Equal(ResultadoSincronizacao.Sucesso, r.Log.Resultado); // o catálogo foi enviado
    }

    // ---------- concorrência, cancelamento e progresso ----------

    [Fact]
    public async Task Nao_deixa_duas_sincronizacoes_ao_mesmo_tempo()
    {
        await CadastrarAsync(2);
        var s = Criar();
        var liberar = new TaskCompletionSource();
        var comecou = new TaskCompletionSource();
        _destino.AntesDeEnviar = async _ => { comecou.TrySetResult(); await liberar.Task; };

        var plano = (await s.PrepararAsync()).Plano!;
        var primeira = s.ExecutarAsync(plano);
        await comecou.Task;

        Assert.True(s.EmAndamento);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.ExecutarAsync(plano));

        liberar.SetResult();
        var resultado = await primeira;
        Assert.Equal(2, resultado.Log.Criados);
        Assert.False(s.EmAndamento);
        Assert.Equal(2, _destino.TodasAsOperacoes.Count()); // nada foi enviado em dobro
    }

    [Fact]
    public async Task Cancelar_no_meio_guarda_o_que_ja_foi_confirmado()
    {
        await CadastrarAsync(4);
        using var cancelamento = new CancellationTokenSource();
        _destino.AntesDeEnviar = _ =>
        {
            if (_destino.Chamadas == 2) cancelamento.Cancel(); // cancela quando começa o 2º lote
            return Task.CompletedTask;
        };
        var s = Criar(lote: 2);
        var plano = (await s.PrepararAsync()).Plano!;

        var r = await s.ExecutarAsync(plano, ct: cancelamento.Token);

        Assert.Equal(ResultadoSincronizacao.Parcial, r.Log.Resultado);
        Assert.Contains("Cancelada pelo usuário", r.Log.Mensagem);
        Assert.Equal(2, r.Log.Criados);
        Assert.Equal(2, (await _repo.ObterEstadoAsync()).Count);
        Assert.False(s.EmAndamento);
    }

    [Fact]
    public async Task Reporta_o_progresso_lote_a_lote()
    {
        await CadastrarAsync(5);
        var s = Criar(lote: 2);
        var plano = (await s.PrepararAsync()).Plano!;
        var passos = new List<int>();

        await s.ExecutarAsync(plano, progresso: new Progress<ProgressoSincronizacao>(p => passos.Add(p.Processados)));
        await Task.Delay(50); // o Progress<T> entrega num momento seguinte

        Assert.Contains(0, passos);
        Assert.Contains(5, passos);
    }

    [Fact]
    public async Task Venda_feita_durante_o_envio_e_reenviada_na_proxima_com_o_estoque_novo()
    {
        var produtos = await CadastrarAsync(1);
        var s = Criar();
        var plano = (await s.PrepararAsync()).Plano!;   // retrato com estoque 10

        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produtos[0].Id, 3) }, 0, FormaPagamento.Pix)); // vende ANTES de enviar
        await s.ExecutarAsync(plano);

        var proximo = await _repo.PlanejarAsync();
        Assert.Single(proximo.Alterados);                       // o estoque real (7) difere do que foi enviado (10)
        Assert.Equal(7, proximo.Alterados[0].Produto.EstoqueTotal);
    }
}
