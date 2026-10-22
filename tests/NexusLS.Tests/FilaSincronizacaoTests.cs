using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class FilaSincronizacaoTests : IDisposable
{
    private readonly string? _uid = "uid-ana";
    private readonly BancoDeTeste _banco;
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly SincronizacaoRepositorio _repo;
    private readonly DestinoFalso _destino = new();
    private DateTime _agora = new(2026, 10, 22, 9, 0, 0);

    public FilaSincronizacaoTests()
    {
        _banco = new BancoDeTeste(comFilaSincronizacao: true, usuarioUid: () => _uid);
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        _repo = new SincronizacaoRepositorio(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private ProcessadorFila Processador() => new(_banco.Fabrica, _repo, _destino, () => _agora);

    private Task<Produto> Cadastrar(string nome, string codigo) =>
        _produtos.CadastrarAsync(new DadosProduto(nome, codigo, 5000, 2000, 1), 10);

    private List<ItemFilaSincronizacao> Fila()
    {
        using var db = _banco.CriarContexto();
        return db.FilaSincronizacao.AsNoTracking().OrderBy(f => f.Id).ToList();
    }

    [Fact]
    public async Task Cadastrar_produto_grava_no_banco_e_entra_na_fila_na_mesma_transacao()
    {
        var p = await Cadastrar("Camiseta", "C1");

        var fila = Fila();
        var item = Assert.Single(fila);
        Assert.Equal(p.Guid, item.ProdutoGuid);
        Assert.Equal(EstadoFila.Pendente, item.Estado);
        Assert.Equal("uid-ana", item.UsuarioUid);
    }

    [Fact]
    public async Task Varias_mudancas_no_mesmo_produto_continuam_uma_unica_linha()
    {
        var p = await Cadastrar("Camiseta", "C1");
        await _produtos.EditarAsync(p.Id, new DadosProduto("Camiseta nova", "C1", 6000, 2000, 1));
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 3, "reposição");

        var item = Assert.Single(Fila());
        Assert.True(item.Versao >= 3);
    }

    [Fact]
    public async Task Processar_envia_e_marca_como_enviado()
    {
        var p = await Cadastrar("Camiseta", "C1");

        var r = await Processador().ProcessarAsync("ana");

        Assert.Equal(1, r.Enviados);
        Assert.Null(r.Parada);
        Assert.Equal(p.Guid, Assert.Single(_destino.TodasAsOperacoes).Item.Produto.Guid);
        Assert.Equal(EstadoFila.Enviado, Assert.Single(Fila()).Estado);
    }

    [Fact]
    public async Task Processar_de_novo_nao_reenvia_nem_duplica()
    {
        await Cadastrar("Camiseta", "C1");
        var proc = Processador();
        await proc.ProcessarAsync();

        var r2 = await proc.ProcessarAsync();
        Assert.True(r2.NadaFeito);
        Assert.Single(_destino.TodasAsOperacoes);
    }

    [Fact]
    public async Task Item_reenfileirado_sem_mudanca_real_nao_e_reenviado_pelo_hash()
    {
        var p = await Cadastrar("Camiseta", "C1");
        var proc = Processador();
        await proc.ProcessarAsync();

        // Salvar de novo sem mudar nada de publicável (só marca o item na fila).
        await using (var db = _banco.CriarContexto())
        {
            var prod = await db.Produtos.FirstAsync(x => x.Id == p.Id);
            prod.AtualizadoEm = prod.AtualizadoEm.AddMinutes(1);
            await db.SaveChangesAsync();
        }
        Assert.Equal(EstadoFila.Pendente, Assert.Single(Fila()).Estado);

        var r = await proc.ProcessarAsync();
        Assert.Equal(1, r.SemMudanca);
        Assert.Equal(0, r.Enviados);
        Assert.Single(_destino.TodasAsOperacoes);
        Assert.Equal(EstadoFila.Enviado, Assert.Single(Fila()).Estado);
    }

    [Fact]
    public async Task Sem_internet_o_item_fica_pendente_com_espera_e_depois_envia()
    {
        await Cadastrar("Camiseta", "C1");
        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem conexão com a internet.");
        var proc = Processador();

        var r1 = await proc.ProcessarAsync();
        Assert.Equal(1, r1.Reagendados);
        var item = Assert.Single(Fila());
        Assert.Equal(EstadoFila.Pendente, item.Estado);
        Assert.Equal(1, item.Tentativas);
        Assert.Equal(_agora + ProcessadorFila.Espera(1), item.ProximaTentativa);
        Assert.Empty(_destino.TodasAsOperacoes);

        // Ainda dentro da espera: nem tenta.
        _agora = _agora.AddSeconds(10);
        Assert.True((await proc.ProcessarAsync()).NadaFeito);

        // Internet voltou e a espera passou.
        _destino.FalhaNaVerificacao = null;
        _agora = _agora.AddMinutes(5);
        var r2 = await proc.ProcessarAsync();
        Assert.Equal(1, r2.Enviados);
        Assert.Equal(EstadoFila.Enviado, Assert.Single(Fila()).Estado);
    }

    [Fact]
    public async Task Queda_de_conexao_no_meio_do_envio_reagenda_so_o_que_nao_foi_confirmado()
    {
        for (var i = 1; i <= 3; i++) await Cadastrar($"Produto {i}", $"C{i}");
        _destino.Falha = (n, _) => n == 2 ? new FalhaDestinoException("conexão caiu") : null;
        var proc = new ProcessadorFila(_banco.Fabrica, _repo, _destino, () => _agora, tamanhoLote: 1);

        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.Enviados);
        Assert.Equal(2, r.Reagendados);
        Assert.Equal(new[] { EstadoFila.Enviado, EstadoFila.Pendente, EstadoFila.Pendente }, Fila().Select(f => f.Estado));

        _destino.Falha = null;
        _agora = _agora.AddHours(1);
        var r2 = await proc.ProcessarAsync();
        Assert.Equal(2, r2.Enviados);
        Assert.Equal(3, _destino.TodasAsOperacoes.Select(o => o.Item.Produto.Guid).Distinct().Count());
        Assert.Equal(3, _destino.TodasAsOperacoes.Count()); // nenhum produto enviado duas vezes
    }

    [Fact]
    public async Task Programa_fechado_no_meio_do_envio_recupera_e_envia_sem_duplicar()
    {
        var p = await Cadastrar("Camiseta", "C1");
        // Simula a queda: o item ficou "Enviando" e o programa fechou antes da confirmação.
        await using (var db = _banco.CriarContexto())
        {
            var f = await db.FilaSincronizacao.FirstAsync();
            f.Estado = EstadoFila.Enviando;
            await db.SaveChangesAsync();
        }
        var proc = Processador();
        Assert.True((await proc.ProcessarAsync()).NadaFeito); // preso: nada acontece sem a recuperação

        Assert.Equal(1, await proc.RecuperarInterrompidosAsync());
        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.Enviados);
        Assert.Equal(p.Guid, Assert.Single(_destino.TodasAsOperacoes).Item.Produto.Guid);
    }

    [Fact]
    public async Task Envio_confirmado_mas_programa_caiu_antes_de_anotar_reenvia_o_mesmo_documento()
    {
        var p = await Cadastrar("Camiseta", "C1");
        await using (var db = _banco.CriarContexto())
        {
            var f = await db.FilaSincronizacao.FirstAsync();
            f.Estado = EstadoFila.Enviando; // servidor recebeu, mas o hash local não foi gravado
            await db.SaveChangesAsync();
        }
        var proc = Processador();
        await proc.RecuperarInterrompidosAsync();
        await proc.ProcessarAsync();

        // O reenvio usa o mesmo id de documento (o Guid): sobrescreve, não cria outro.
        Assert.Equal(p.Guid, _destino.TodasAsOperacoes.Single().Item.Produto.Guid);
        Assert.Single(Fila());
    }

    [Fact]
    public async Task Produto_alterado_durante_o_envio_nao_e_dado_como_enviado()
    {
        var p = await Cadastrar("Camiseta", "C1");
        _destino.AntesDeEnviar = async _ =>
            await _produtos.EditarAsync(p.Id, new DadosProduto("Camiseta v2", "C1", 7000, 2000, 1));
        var proc = Processador();

        await proc.ProcessarAsync();
        Assert.Equal(EstadoFila.Pendente, Assert.Single(Fila()).Estado);

        _destino.AntesDeEnviar = null;
        await proc.ProcessarAsync();
        Assert.Equal(EstadoFila.Enviado, Assert.Single(Fila()).Estado);
        Assert.Equal("Camiseta v2", _destino.TodasAsOperacoes.Last().Item.Produto.Nome);
    }

    [Fact]
    public async Task Dados_recusados_pelo_servidor_viram_Falhou_e_nao_travam_os_outros()
    {
        var ruim = await Cadastrar("Ruim", "C1");
        await Cadastrar("Bom", "C2");
        _destino.Falha = (_, lote) => lote.Any(o => o.Item.Produto.Guid == ruim.Guid)
            ? new FalhaDestinoException("campo inválido", GravidadeFalha.DadosRecusados) : null;

        var r = await Processador().ProcessarAsync();

        Assert.Equal(1, r.Enviados);
        Assert.Equal(1, r.Recusados);
        var fila = Fila();
        Assert.Equal(EstadoFila.Falhou, fila.Single(f => f.ProdutoGuid == ruim.Guid).Estado);
        Assert.Contains("campo inválido", fila.Single(f => f.ProdutoGuid == ruim.Guid).UltimoErro);

        // Corrigir o produto devolve o item à fila.
        _destino.Falha = null;
        await _produtos.EditarAsync(ruim.Id, new DadosProduto("Ruim corrigido", "C1", 5000, 2000, 1));
        Assert.Equal(EstadoFila.Pendente, Fila().Single(f => f.ProdutoGuid == ruim.Guid).Estado);
        Assert.Equal(1, (await Processador().ProcessarAsync()).Enviados);
    }

    [Fact]
    public async Task Tentativas_registram_falha_no_log_de_sincronizacao()
    {
        await Cadastrar("Camiseta", "C1");
        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem conexão com a internet.");
        await Processador().ProcessarAsync("ana");

        var log = Assert.Single(await _repo.UltimosLogsAsync());
        Assert.Equal(ResultadoSincronizacao.Falhou, log.Resultado);
        Assert.Contains("reagendado", log.Mensagem);
        Assert.Equal("ana", log.Operador);
    }

    [Fact]
    public async Task Produtos_antigos_sem_linha_na_fila_sao_enfileirados_uma_vez_so_e_enviados()
    {
        await Cadastrar("Antigo 1", "A1");
        await Cadastrar("Antigo 2", "A2");
        await using (var db = _banco.CriarContexto()) // simula um banco de antes da fila existir
        {
            db.FilaSincronizacao.RemoveRange(db.FilaSincronizacao);
            await db.SaveChangesAsync();
        }
        var proc = Processador();

        Assert.Equal(2, await proc.EnfileirarExistentesAsync());
        Assert.Equal(0, await proc.EnfileirarExistentesAsync()); // repetir não duplica
        Assert.Equal(2, Fila().Count);

        Assert.Equal(2, (await proc.ProcessarAsync()).Enviados);
    }

    [Fact]
    public async Task Produto_que_o_catalogo_ja_tem_nao_e_reenviado_depois_de_enfileirar()
    {
        var p = await Cadastrar("Camiseta", "C1");
        var proc = Processador();
        await proc.ProcessarAsync();
        await using (var db = _banco.CriarContexto())
        {
            db.FilaSincronizacao.RemoveRange(db.FilaSincronizacao);
            await db.SaveChangesAsync();
        }

        await proc.EnfileirarExistentesAsync();
        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.SemMudanca);
        Assert.Single(_destino.TodasAsOperacoes);
    }

    [Fact]
    public void Espera_cresce_e_tem_teto()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), ProcessadorFila.Espera(1));
        Assert.Equal(TimeSpan.FromSeconds(60), ProcessadorFila.Espera(2));
        Assert.Equal(TimeSpan.FromMinutes(30), ProcessadorFila.Espera(50));
    }

    [Fact]
    public async Task Duas_execucoes_ao_mesmo_tempo_nao_enviam_duas_vezes()
    {
        await Cadastrar("Camiseta", "C1");
        var liberar = new TaskCompletionSource();
        _destino.AntesDeEnviar = _ => liberar.Task;
        var proc = Processador();

        var primeira = proc.ProcessarAsync();
        var segunda = await proc.ProcessarAsync(); // enquanto a primeira está presa no destino
        liberar.SetResult();
        await primeira;

        Assert.Contains("andamento", segunda.Parada);
        Assert.Single(_destino.TodasAsOperacoes);
    }

    [Fact]
    public async Task Venda_baixa_estoque_e_coloca_o_produto_na_fila_de_novo()
    {
        var p = await Cadastrar("Camiseta", "C1");
        var proc = Processador();
        await proc.ProcessarAsync();
        Assert.Equal(EstadoFila.Enviado, Assert.Single(Fila()).Estado);

        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Perda, 2, "teste");

        Assert.Equal(EstadoFila.Pendente, Assert.Single(Fila()).Estado);
        await proc.ProcessarAsync();
        Assert.Equal(8, _destino.TodasAsOperacoes.Last().Item.Produto.EstoqueTotal);
    }
}
