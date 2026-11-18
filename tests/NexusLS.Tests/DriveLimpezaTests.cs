using System.Net;
using NexusLS.Core.Backup;
using NexusLS.Core.Google;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Tests;

public sealed class DriveLimpezaTests : IDisposable
{
    private sealed class ProtetorSimples : IProtetorSegredo
    {
        public string Proteger(string t) => "P:" + t;
        public string? Desproteger(string t) => t.StartsWith("P:") ? t[2..] : null;
    }

    private static readonly DateTime Agora = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "drivelimpeza-" + Guid.NewGuid().ToString("N"));
    private readonly GoogleStore _store;

    public DriveLimpezaTests()
    {
        Directory.CreateDirectory(_pasta);
        _store = new GoogleStore(Path.Combine(_pasta, "google.json"), new ProtetorSimples());
        _store.Salvar(new ConfiguracaoGoogle { ClientId = "1-a.apps.googleusercontent.com", ClientSecretProtegido = _store.Proteger("s"), RefreshTokenProtegido = _store.Proteger("r") });
    }

    public void Dispose() => Directory.Delete(_pasta, true);

    private static ServidorFalso ComToken() =>
        new ServidorFalso().Responde(HttpStatusCode.OK, "{\"access_token\":\"a\",\"expires_in\":3599}");

    private (GoogleDriveBackup G, DriveRest D, List<string> Log) Criar(ServidorFalso s)
    {
        var http = new HttpClient(s);
        var auth = new GoogleOAuth(http, _store);
        var rest = new DriveRest(http, auth);
        var log = new List<string>();
        return (new GoogleDriveBackup(_store, auth, rest) { RegistrarLog = log.Add }, rest, log);
    }

    private static ArquivoDrive Arq(string id, int diasAtras, MotivoBackup motivo = MotivoBackup.Automatico, long bytes = 1000)
    {
        var d = Agora.AddDays(-diasAtras);
        return new ArquivoDrive(id, RegrasBackup.NomeArquivo(d.ToLocalTime(), motivo), d, d, bytes);
    }

    // ---------- regra pura ----------

    [Fact]
    public void Regra_lista_vazia_e_um_unico_backup_nao_apagam_nada()
    {
        Assert.Empty(RegrasBackup.SelecionarDoDriveParaApagar(Array.Empty<ArquivoDrive>(), Agora));
        Assert.Empty(RegrasBackup.SelecionarDoDriveParaApagar(new[] { Arq("a", 400) }, Agora));
    }

    [Fact]
    public void Regra_todos_recentes_nada_e_apagado()
    {
        var todos = Enumerable.Range(1, 10).Select(i => Arq("b" + i, i)).ToList();
        Assert.Empty(RegrasBackup.SelecionarDoDriveParaApagar(todos, Agora, 30));
    }

    [Fact]
    public void Regra_sempre_mantem_os_3_mais_recentes_mesmo_todos_velhos()
    {
        var todos = new[] { Arq("a", 100), Arq("b", 200), Arq("c", 300), Arq("d", 400), Arq("e", 500) };
        var apagar = RegrasBackup.SelecionarDoDriveParaApagar(todos, Agora, 30).Select(x => x.Id).ToList();
        Assert.Equal(new[] { "d", "e" }, apagar.OrderBy(x => x));
        Assert.DoesNotContain("a", apagar);                                  // o mais recente nunca
        Assert.Empty(RegrasBackup.SelecionarDoDriveParaApagar(todos.Take(3), Agora, 1));
    }

    [Fact]
    public void Regra_respeita_dias_e_manter_recentes_e_nunca_apaga_seguranca_ou_nome_estranho()
    {
        var todos = new List<ArquivoDrive>
        {
            Arq("novo", 1), Arq("m10", 10), Arq("m20", 20), Arq("m40", 40), Arq("m50", 50),
            Arq("seg", 90, MotivoBackup.PreAtualizacao), Arq("m60", 60, MotivoBackup.Manual),
            new("x", "foto.png", Agora.AddDays(-999), Agora.AddDays(-999), 5)
        };
        var padrao = RegrasBackup.SelecionarDoDriveParaApagar(todos, Agora, 30).Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "m40", "m50", "m60" }, padrao);

        var manter4 = RegrasBackup.SelecionarDoDriveParaApagar(todos, Agora, 30, manterRecentes: 4).Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "m50", "m60" }, manter4);                       // os 4 mais novos (novo, m10, m20, m40) ficam

        Assert.Empty(RegrasBackup.SelecionarDoDriveParaApagar(todos, Agora, 30, manterRecentes: 6));   // só sobra a cópia de segurança, que nunca sai

        var curto = RegrasBackup.SelecionarDoDriveParaApagar(todos, Agora, 5).Select(x => x.Id).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "m40", "m50", "m60" }, curto);                  // 3 mais novos sempre ficam, mesmo fora do prazo
    }

    // ---------- Drive com servidor de mentira ----------

    [Fact]
    public async Task Lista_percorre_todas_as_paginas_e_le_os_campos()
    {
        var s = ComToken()
            .Responde(HttpStatusCode.OK, "{\"files\":[{\"id\":\"pasta-1\"}]}")
            .Responde(HttpStatusCode.OK, "{\"nextPageToken\":\"p2\",\"files\":[{\"id\":\"f1\",\"name\":\"nexus_2026-10-01_100000_auto.db\",\"createdTime\":\"2026-10-01T13:00:00.000Z\",\"modifiedTime\":\"2026-10-01T13:00:01.000Z\",\"size\":\"2048\"}]}")
            .Responde(HttpStatusCode.OK, "{\"files\":[{\"id\":\"f2\",\"name\":\"nexus_2026-10-02_100000_manual.db\",\"createdTime\":\"2026-10-02T13:00:00Z\",\"modifiedTime\":\"2026-10-02T13:00:00Z\"}]}");
        var (g, _, _) = Criar(s);

        var lista = await g.ListarBackupsAsync();

        Assert.Equal(2, lista.Count);
        Assert.Equal(2048, lista[0].Bytes);
        Assert.Equal(new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Utc), lista[0].CriadoUtc);
        Assert.Equal(0, lista[1].Bytes);
        Assert.Contains("pasta-1", Uri.UnescapeDataString(s.Pedidos[2].Url));
        Assert.Contains("pageToken=p2", s.Pedidos[3].Url);
        Assert.All(s.Pedidos.Skip(1), p => Assert.StartsWith("https://www.googleapis.com/drive/v3/", p.Url));
    }

    [Fact]
    public async Task Sem_a_pasta_no_drive_a_lista_e_vazia_e_nada_e_criado()
    {
        var s = ComToken().Responde(HttpStatusCode.OK, "{\"files\":[]}");
        var (g, _, _) = Criar(s);
        Assert.Empty(await g.ListarBackupsAsync());
        Assert.DoesNotContain(s.Pedidos, p => p.Metodo == HttpMethod.Post && p.Url.Contains("/files?fields=id"));
    }

    [Fact]
    public async Task Planejar_nao_apaga_nada()
    {
        var s = ComToken()
            .Responde(HttpStatusCode.OK, "{\"files\":[{\"id\":\"pasta-1\"}]}")
            .Responde(HttpStatusCode.OK, "{\"files\":[" + string.Join(",", new[] { 1, 2, 3, 50, 60 }.Select(d =>
            {
                var t = Agora.AddDays(-d);
                return $"{{\"id\":\"f{d}\",\"name\":\"{RegrasBackup.NomeArquivo(t.ToLocalTime(), MotivoBackup.Automatico)}\",\"createdTime\":\"{t:yyyy-MM-ddTHH:mm:ssZ}\",\"size\":\"1048576\"}}";
            })) + "]}");
        var (g, _, _) = Criar(s);

        var plano = await g.PlanejarLimpezaAsync(30, null, Agora);

        Assert.Equal(new[] { "f50", "f60" }, plano.Apagar.Select(a => a.Id).OrderBy(x => x));
        Assert.Equal(2 * 1048576, plano.BytesLiberados);
        Assert.Equal(3, plano.Mantidos);
        Assert.DoesNotContain(s.Pedidos, p => p.Metodo == HttpMethod.Delete);
    }

    [Fact]
    public async Task Apaga_confere_a_pasta_registra_no_log_e_trata_404_como_ja_apagado()
    {
        var s = ComToken()
            .Responde(HttpStatusCode.OK, "{\"files\":[{\"id\":\"pasta-1\"}]}")          // pasta
            .Responde(HttpStatusCode.OK, "{\"parents\":[\"pasta-1\"]}")                 // f1 confere
            .Responde(HttpStatusCode.NoContent, "")                                       // delete f1
            .Responde(HttpStatusCode.NotFound, "{}")                                      // f2 já sumiu (GET 404)
            .Responde(HttpStatusCode.OK, "{\"parents\":[\"pasta-1\"]}")                 // f3 confere
            .Responde(HttpStatusCode.NotFound, "{}");                                     // delete f3 -> 404
        var (g, _, log) = Criar(s);
        var a1 = Arq("f1", 50); var a2 = Arq("f2", 60); var a3 = Arq("f3", 70);

        var r = await g.ApagarAsync(new[] { a1, a2, a3 });

        Assert.Null(r.Erro);
        Assert.Equal(new[] { "f1" }, r.Apagados.Select(a => a.Id));
        Assert.Equal(new[] { "f2", "f3" }, r.JaApagados.Select(a => a.Id));
        Assert.Contains(s.Pedidos, p => p.Metodo == HttpMethod.Delete && p.Url.EndsWith("/files/f1"));
        Assert.Contains(log, l => l.Contains(a1.Nome) && l.StartsWith("Apagado do Drive"));
        Assert.Equal(3, log.Count);
    }

    [Fact]
    public async Task Arquivo_fora_da_pasta_do_nexus_nao_e_apagado()
    {
        var s = ComToken()
            .Responde(HttpStatusCode.OK, "{\"files\":[{\"id\":\"pasta-1\"}]}")
            .Responde(HttpStatusCode.OK, "{\"parents\":[\"outra-pasta\"]}");
        var (g, _, log) = Criar(s);

        var r = await g.ApagarAsync(new[] { Arq("f1", 50) });

        Assert.Empty(r.Apagados);
        Assert.Contains("só apaga arquivos que estão na pasta", r.Erro);
        Assert.DoesNotContain(s.Pedidos, p => p.Metodo == HttpMethod.Delete);
        Assert.Single(log);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "{}", "negou a permissão")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "problema no momento")]
    public async Task Erro_do_drive_ao_apagar_para_e_explica_em_portugues(HttpStatusCode status, string corpo, string trecho)
    {
        var s = ComToken()
            .Responde(HttpStatusCode.OK, "{\"files\":[{\"id\":\"pasta-1\"}]}")
            .Responde(HttpStatusCode.OK, "{\"parents\":[\"pasta-1\"]}")
            .Responde(status, corpo);
        var (g, _, _) = Criar(s);

        var r = await g.ApagarAsync(new[] { Arq("f1", 50), Arq("f2", 60) });

        Assert.Empty(r.Apagados);
        Assert.Contains(trecho, r.Erro);
        Assert.Equal(4, s.Pedidos.Count);                                     // token, pasta, get, delete: parou no primeiro erro
    }

    [Fact]
    public async Task Falha_de_rede_ao_listar_vira_mensagem_em_portugues()
    {
        var s = ComToken().Falha(new HttpRequestException("sem rede"));
        var (g, _, _) = Criar(s);
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => g.ListarBackupsAsync());
        Assert.Contains("Sem conexão", ex.Message);
    }

    [Fact]
    public async Task Sem_conexao_com_o_google_nada_e_chamado()
    {
        _store.Salvar(new ConfiguracaoGoogle());
        var s = new ServidorFalso();
        var (g, _, _) = Criar(s);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => g.ListarBackupsAsync());
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => g.ApagarAsync(new[] { Arq("f", 50) }));
        Assert.Empty(s.Pedidos);
    }
}
