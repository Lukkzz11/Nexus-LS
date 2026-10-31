using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Operacional;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

internal sealed class DestinoOperacionalFalso : IDestinoOperacional
{
    public List<List<DocumentoOperacional>> Lotes { get; } = new();
    public FalhaDestinoException? FalhaNaVerificacao { get; set; }
    public Func<int, IReadOnlyList<DocumentoOperacional>, FalhaDestinoException?>? Falha { get; set; }
    public Func<CancellationToken, Task>? AntesDeEnviar { get; set; }
    public int Chamadas { get; private set; }
    public IEnumerable<DocumentoOperacional> Todos => Lotes.SelectMany(l => l);

    public Task VerificarAsync(CancellationToken ct = default) =>
        FalhaNaVerificacao is null ? Task.CompletedTask : Task.FromException(FalhaNaVerificacao);

    public async Task EnviarLoteAsync(IReadOnlyList<DocumentoOperacional> lote, CancellationToken ct = default)
    {
        Chamadas++;
        if (AntesDeEnviar is not null) await AntesDeEnviar(ct);
        var falha = Falha?.Invoke(Chamadas, lote);
        if (falha is not null) throw falha;
        Lotes.Add(lote.ToList());
    }
}

public sealed class OperacionalTests : IDisposable
{
    private readonly BancoDeTeste _banco = new(comFilaSincronizacao: true, usuarioUid: () => "uid-ana");
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly DestinoOperacionalFalso _destino = new();
    private DateTime _agora = new(2026, 10, 30, 9, 0, 0);

    public OperacionalTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _vendas = new VendaService(_banco.Fabrica, () => _agora);
    }

    public void Dispose() => _banco.Dispose();

    private ProcessadorFilaOperacional Processador(int lote = ProcessadorFilaOperacional.TamanhoPadraoDoLote) =>
        new(_banco.Fabrica, new MontadorOperacional(_banco.Fabrica), _destino, () => _agora, lote);

    private async Task<Venda> Vender(Guid? chave = null)
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto("Camiseta " + Guid.NewGuid().ToString("N")[..4], null, 5000, 2000, 1), 10);
        return await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(p.Id, 2) }, 500, FormaPagamento.Pix, ChaveUnica: chave));
    }

    private List<ItemFilaSincronizacao> FilaDeVendas()
    {
        using var db = _banco.CriarContexto();
        return db.FilaSincronizacao.AsNoTracking().Where(f => f.Entidade == NomesEntidade.Venda).OrderBy(f => f.Id).ToList();
    }

    // ---------- montador ----------

    [Fact]
    public async Task Montador_gera_o_documento_da_venda_com_itens_e_guids()
    {
        var chave = Guid.NewGuid();
        var venda = await Vender(chave);

        var r = await new MontadorOperacional(_banco.Fabrica).MontarAsync(NomesEntidade.Venda, new[] { venda.Guid, Guid.NewGuid() });

        var doc = Assert.Single(r.Documentos);
        Assert.Equal(chave, doc.Guid);
        Assert.Equal("vendas", doc.Colecao);
        Assert.Equal(venda.TotalCentavos, doc.Campos["totalCentavos"]);
        Assert.Equal(500L, doc.Campos["descontoCentavos"]);
        Assert.Equal("Pix", doc.Campos["formaPagamento"]);
        Assert.Equal(false, doc.Campos["cancelada"]);
        Assert.Equal("nexus-ls", doc.Campos["origem"]);
        var itens = Assert.IsAssignableFrom<IEnumerable<object?>>(doc.Campos["itens"]).Cast<IDictionary<string, object?>>().ToList();
        var item = Assert.Single(itens);
        Assert.Equal(2, item["quantidade"]);
        Assert.NotNull(item["produtoGuid"]);
        Assert.Single(r.Ausentes); // o Guid que não existe é avisado
        Assert.Equal(DateTimeKind.Utc, ((DateTime)doc.Campos["dataHora"]!).Kind);
    }

    [Fact]
    public async Task Montador_inclui_o_cancelamento_no_mesmo_documento()
    {
        var venda = await Vender();
        await _vendas.CancelarAsync(venda.Id, "cliente desistiu");

        var doc = Assert.Single((await new MontadorOperacional(_banco.Fabrica).MontarAsync(NomesEntidade.Venda, new[] { venda.Guid })).Documentos);

        Assert.Equal(true, doc.Campos["cancelada"]);
        Assert.Equal("cliente desistiu", doc.Campos["motivoCancelamento"]);
        Assert.NotNull(doc.Campos["canceladaEm"]);
    }

    // ---------- destino Firestore (REST simulado) ----------

    private sealed class Token : IProvedorToken
    {
        public int Invalidacoes { get; private set; }
        public Task<string> ObterAsync(CancellationToken ct = default) => Task.FromResult("TOKEN-ANA");
        public void Invalidar() => Invalidacoes++;
    }

    private sealed class Http : HttpMessageHandler
    {
        public List<(HttpMethod Metodo, string Url, string Auth, string Corpo)> Pedidos { get; } = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Resposta { get; set; } = _ => new(HttpStatusCode.OK) { Content = new StringContent("{}") };

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Pedidos.Add((r.Method, r.RequestUri!.ToString(), r.Headers.Authorization?.Parameter ?? "", r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return Resposta(r);
        }
    }

    [Fact]
    public async Task Destino_grava_em_lojas_loja_vendas_guid_com_mascara_e_token_do_usuario()
    {
        var http = new Http();
        var destino = new FirestoreOperacionalDestino(new HttpClient(http), "proj-operacional", "minha-loja", new Token());
        var guid = Guid.NewGuid();
        var campos = new Dictionary<string, object?>
        {
            ["guid"] = guid, ["totalCentavos"] = 9500L, ["cancelada"] = false, ["motivo"] = null,
            ["dataHora"] = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc),
            ["itens"] = new List<object?> { new Dictionary<string, object?> { ["nome"] = "Camiseta", ["quantidade"] = 2 } }
        };

        await destino.EnviarLoteAsync(new[] { new DocumentoOperacional("vendas", guid, campos) });

        var pedido = Assert.Single(http.Pedidos);
        Assert.Equal(HttpMethod.Post, pedido.Metodo);
        Assert.EndsWith("/projects/proj-operacional/databases/(default)/documents:commit", pedido.Url);
        Assert.Equal("TOKEN-ANA", pedido.Auth);
        var escrita = JsonNode.Parse(pedido.Corpo)!["writes"]![0]!;
        Assert.Equal($"projects/proj-operacional/databases/(default)/documents/lojas/minha-loja/vendas/{guid:D}", escrita["update"]!["name"]!.GetValue<string>());
        var f = escrita["update"]!["fields"]!;
        Assert.Equal("9500", f["totalCentavos"]!["integerValue"]!.GetValue<string>()); // a API pede inteiro como texto
        Assert.False(f["cancelada"]!["booleanValue"]!.GetValue<bool>());
        Assert.Equal(guid.ToString("D"), f["guid"]!["stringValue"]!.GetValue<string>());
        Assert.StartsWith("2026-10-30T12:00:00", f["dataHora"]!["timestampValue"]!.GetValue<string>());
        Assert.Equal("Camiseta", f["itens"]!["arrayValue"]!["values"]![0]!["mapValue"]!["fields"]!["nome"]!["stringValue"]!.GetValue<string>());
        Assert.Null(f["motivo"]); // nulo não vira campo...
        var mascara = escrita["updateMask"]!["fieldPaths"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("motivo", mascara); // ...mas entra na máscara: o campo é removido se existia
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NotFound, true)]   // a loja ainda não tem documento: login e permissão estão ok
    [InlineData(HttpStatusCode.Forbidden, false)]
    public async Task Verificar_distingue_ok_de_sem_permissao(HttpStatusCode status, bool ok)
    {
        var http = new Http { Resposta = _ => new HttpResponseMessage(status) { Content = new StringContent("{}") } };
        var destino = new FirestoreOperacionalDestino(new HttpClient(http), "proj-operacional", "minha-loja", new Token());

        if (ok) await destino.VerificarAsync();
        else Assert.Contains("permissão", (await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.VerificarAsync())).Message);
        Assert.EndsWith("/documents/lojas/minha-loja", http.Pedidos[0].Url);
    }

    [Fact]
    public async Task Sem_internet_vira_falha_de_conexao_e_recusa_de_dado_vira_DadosRecusados()
    {
        var http = new Http { Resposta = _ => throw new HttpRequestException("sem rede") };
        var destino = new FirestoreOperacionalDestino(new HttpClient(http), "proj-operacional", "minha-loja", new Token());
        var doc = new DocumentoOperacional("vendas", Guid.NewGuid(), new Dictionary<string, object?> { ["guid"] = "x" });

        var conexao = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { doc }));
        Assert.Equal(GravidadeFalha.Interrompe, conexao.Gravidade);

        http.Resposta = _ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":{\"message\":\"campo ruim\"}}", Encoding.UTF8, "application/json") };
        var recusa = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { doc }));
        Assert.Equal(GravidadeFalha.DadosRecusados, recusa.Gravidade);
    }

    // ---------- processador da fila operacional ----------

    [Fact]
    public async Task Venda_e_enviada_uma_vez_e_marcada_como_enviada()
    {
        var venda = await Vender();
        var proc = Processador();

        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.Enviados);
        Assert.Equal(venda.Guid, Assert.Single(_destino.Todos).Guid);
        Assert.Equal(EstadoFila.Enviado, Assert.Single(FilaDeVendas()).Estado);

        Assert.True((await proc.ProcessarAsync()).NadaFeito); // de novo: nada a fazer, nenhum envio extra
        Assert.Single(_destino.Todos);
    }

    [Fact]
    public async Task Cancelar_a_venda_reenvia_o_mesmo_documento_atualizado()
    {
        var venda = await Vender();
        var proc = Processador();
        await proc.ProcessarAsync();

        await _vendas.CancelarAsync(venda.Id, "cliente desistiu");
        await proc.ProcessarAsync();

        Assert.Equal(2, _destino.Todos.Count());
        Assert.All(_destino.Todos, d => Assert.Equal(venda.Guid, d.Guid)); // mesmo id: sobrescreve, não duplica
        Assert.Equal(true, _destino.Todos.Last().Campos["cancelada"]);
    }

    [Fact]
    public async Task Sem_internet_reagenda_e_depois_envia_sem_duplicar()
    {
        await Vender();
        _destino.FalhaNaVerificacao = new FalhaDestinoException("Sem conexão com a internet.");
        var proc = Processador();

        var r1 = await proc.ProcessarAsync();
        Assert.Equal(1, r1.Reagendados);
        var item = Assert.Single(FilaDeVendas());
        Assert.Equal(EstadoFila.Pendente, item.Estado);
        Assert.Equal(_agora + ProcessadorFila.Espera(1), item.ProximaTentativa);

        _destino.FalhaNaVerificacao = null;
        Assert.True((await proc.ProcessarAsync()).NadaFeito); // ainda na espera

        _agora = _agora.AddMinutes(5);
        Assert.Equal(1, (await proc.ProcessarAsync()).Enviados);
        Assert.Single(_destino.Todos);
    }

    [Fact]
    public async Task Queda_no_meio_do_lote_reagenda_so_o_que_nao_foi_confirmado()
    {
        for (var i = 0; i < 3; i++) await Vender();
        _destino.Falha = (n, _) => n == 2 ? new FalhaDestinoException("conexão caiu") : null;
        var proc = Processador(lote: 1);

        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.Enviados);
        Assert.Equal(2, r.Reagendados);
        _destino.Falha = null;
        _agora = _agora.AddHours(1);
        Assert.Equal(2, (await proc.ProcessarAsync()).Enviados);
        Assert.Equal(3, _destino.Todos.Select(d => d.Guid).Distinct().Count());
        Assert.Equal(3, _destino.Todos.Count());
    }

    [Fact]
    public async Task Programa_fechado_no_meio_recupera_e_envia()
    {
        await Vender();
        await using (var db = _banco.CriarContexto())
        {
            var f = await db.FilaSincronizacao.FirstAsync(x => x.Entidade == NomesEntidade.Venda);
            f.Estado = EstadoFila.Enviando;
            await db.SaveChangesAsync();
        }
        var proc = Processador();

        Assert.True((await proc.ProcessarAsync()).NadaFeito);
        Assert.Equal(1, await proc.RecuperarInterrompidosAsync());
        Assert.Equal(1, (await proc.ProcessarAsync()).Enviados);
    }

    [Fact]
    public async Task Venda_alterada_durante_o_envio_continua_pendente()
    {
        var venda = await Vender();
        _destino.AntesDeEnviar = async _ => await _vendas.CancelarAsync(venda.Id, "cancelada no meio");
        var proc = Processador();

        await proc.ProcessarAsync();
        Assert.Equal(EstadoFila.Pendente, Assert.Single(FilaDeVendas()).Estado);

        _destino.AntesDeEnviar = null;
        await proc.ProcessarAsync();
        Assert.Equal(EstadoFila.Enviado, Assert.Single(FilaDeVendas()).Estado);
        Assert.Equal(true, _destino.Todos.Last().Campos["cancelada"]);
    }

    [Fact]
    public async Task Documento_recusado_vira_Falhou_sem_travar_os_outros_e_volta_com_reenviar()
    {
        var ruim = await Vender();
        await Vender();
        _destino.Falha = (_, lote) => lote.Any(d => d.Guid == ruim.Guid)
            ? new FalhaDestinoException("campo inválido", GravidadeFalha.DadosRecusados) : null;
        var proc = Processador();

        var r = await proc.ProcessarAsync();

        Assert.Equal(1, r.Enviados);
        Assert.Equal(1, r.Recusados);
        Assert.Equal(EstadoFila.Falhou, FilaDeVendas().Single(f => f.EntidadeGuid == ruim.Guid).Estado);

        _destino.Falha = null;
        Assert.Equal(1, await proc.ReenfileirarFalhasAsync());
        Assert.Equal(1, (await proc.ProcessarAsync()).Enviados);
    }

    [Fact]
    public async Task Vendas_antigas_sem_linha_na_fila_sao_enfileiradas_uma_vez_so()
    {
        await Vender();
        await Vender();
        await using (var db = _banco.CriarContexto())
        {
            db.FilaSincronizacao.RemoveRange(db.FilaSincronizacao);
            await db.SaveChangesAsync();
        }
        var proc = Processador();

        Assert.Equal(2, await proc.EnfileirarExistentesAsync());
        Assert.Equal(0, await proc.EnfileirarExistentesAsync());
        Assert.Equal(2, (await proc.ProcessarAsync()).Enviados);
    }

    [Fact]
    public async Task Processador_operacional_nao_toca_nos_produtos_da_fila_do_catalogo()
    {
        await Vender();
        await Processador().ProcessarAsync();

        await using var db = _banco.CriarContexto();
        Assert.All(db.FilaSincronizacao.Where(f => f.Entidade == NomesEntidade.Produto).ToList(), f => Assert.Equal(EstadoFila.Pendente, f.Estado));
        var resumo = await Processador().ResumoAsync();
        Assert.Equal(1, resumo.Enviados); // conta só vendas
    }

    // ---------- login no projeto operacional ----------

    [Fact]
    public void Login_vai_para_o_projeto_operacional_quando_configurado_e_nao_serve_mais_ao_catalogo()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "nexus-oper-" + Guid.NewGuid().ToString("N"));
        try
        {
            var protetor = new ProtetorFalso();
            var config = new ConfiguracaoFirebaseStore(Path.Combine(pasta, "firebase.json"), protetor);
            config.Salvar(new ConfiguracaoFirebase { ProjectId = "catalogo-loja-1a2b", ApiKey = "chave-a", Email = "licenca@loja.com" }, "senha");
            var oper = new ConfiguracaoOperacionalStore(Path.Combine(pasta, "operacional.json"));
            var login = new GestorLogin(config, new HttpClient(), new SessaoStore(Path.Combine(pasta, "sessao.json")), protetor, operacional: oper);

            Assert.Equal(("catalogo-loja-1a2b", "chave-a"), login.ProjetoDoLogin); // sem operacional: um projeto só, como antes
            Assert.True(login.LoginServeAoCatalogo);

            oper.Salvar(new ConfiguracaoOperacional { ProjectId = "operacional-loja-9z", ApiKey = "chave-b", LojaId = "minha-loja" });

            Assert.Equal(("operacional-loja-9z", "chave-b"), login.ProjetoDoLogin);
            Assert.False(login.LoginServeAoCatalogo); // o token do usuário não vale no catálogo
        }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }

    [Theory]
    [InlineData("Projeto Errado", "chave", "minha-loja")]
    [InlineData("operacional-loja-9z", "", "minha-loja")]
    [InlineData("operacional-loja-9z", "chave", "Minha Loja")]
    public void Configuracao_operacional_invalida_e_recusada(string projeto, string chave, string loja) =>
        Assert.Throws<RegraDeNegocioException>(() =>
            new ConfiguracaoOperacionalStore(Path.Combine(Path.GetTempPath(), "nao-grava-" + Guid.NewGuid().ToString("N") + ".json"))
                .Salvar(new ConfiguracaoOperacional { ProjectId = projeto, ApiKey = chave, LojaId = loja }));

    private sealed class ProtetorFalso : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    // ---------- monitor com os dois destinos ----------

    [Fact]
    public async Task Monitor_envia_as_vendas_mesmo_sem_a_licenca_do_catalogo_e_mostra_nuvem_em_dia()
    {
        await Vender();
        var catalogo = new ProcessadorFila(_banco.Fabrica, new SincronizacaoRepositorio(_banco.Fabrica, () => _agora), new DestinoFalso(), () => _agora);
        var monitor = new MonitorSincronizacao(catalogo, () => true, () => false, Processador(), () => true);

        var antes = await monitor.LerEstadoAsync();
        Assert.Equal(NivelSincronizacao.Pendente, antes.Nivel);

        var e = await monitor.CicloAsync();

        Assert.Equal(NivelSincronizacao.EmDia, e.Nivel); // os produtos do catálogo não contam: esse destino não pode enviar
        Assert.Equal("Nuvem em dia", e.Texto);
        Assert.Single(_destino.Todos);
    }

    [Fact]
    public async Task Monitor_sem_nenhum_destino_pronto_mostra_sem_login_com_tudo_que_espera()
    {
        await Vender();
        var catalogo = new ProcessadorFila(_banco.Fabrica, new SincronizacaoRepositorio(_banco.Fabrica, () => _agora), new DestinoFalso(), () => _agora);
        var monitor = new MonitorSincronizacao(catalogo, () => true, () => false, Processador(), () => false);

        var e = await monitor.CicloAsync();

        Assert.Equal(NivelSincronizacao.SemLogin, e.Nivel);
        Assert.Empty(_destino.Todos);
    }
}
