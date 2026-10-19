using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Servidor de mentira: responde na ordem combinada e guarda cada pedido recebido.</summary>
internal sealed class ServidorFalso : HttpMessageHandler
{
    public record Pedido(HttpMethod Metodo, string Url, string? Autorizacao, string Corpo);

    private readonly Queue<Func<HttpResponseMessage>> _respostas = new();
    public List<Pedido> Pedidos { get; } = new();
    public Func<Pedido, HttpResponseMessage?>? Regra { get; set; }

    public ServidorFalso Responde(HttpStatusCode status, string corpo = "{}")
    {
        _respostas.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        return this;
    }

    public ServidorFalso Falha(Exception ex)
    {
        _respostas.Enqueue(() => throw ex);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var corpo = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        var pedido = new Pedido(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), corpo);
        Pedidos.Add(pedido);
        var daRegra = Regra?.Invoke(pedido);
        if (daRegra is not null) return daRegra;
        if (_respostas.Count == 0) throw new InvalidOperationException("O teste não preparou resposta para: " + request.RequestUri);
        return _respostas.Dequeue()();
    }

    public static HttpResponseMessage Json(HttpStatusCode status, string corpo) =>
        new(status) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
}

internal sealed class TokenFixo : IProvedorToken
{
    public int Invalidacoes { get; private set; }
    public string Atual { get; set; } = "token-1";
    public Task<string> ObterAsync(CancellationToken ct = default) => Task.FromResult(Atual);
    public void Invalidar()
    {
        Invalidacoes++;
        Atual = "token-" + (Invalidacoes + 1);
    }
}

/// <summary>Protetor de mentira para os testes (inverte o texto): garante que o arquivo nunca guarda a senha como foi digitada.</summary>
internal sealed class ProtetorFalso : IProtetorSegredo
{
    public string Proteger(string textoClaro) => "PROT:" + new string(textoClaro.Reverse().ToArray());
    public string? Desproteger(string protegido) =>
        protegido.StartsWith("PROT:") ? new string(protegido[5..].Reverse().ToArray()) : null;
}

public class FirebaseAuthRestTests
{
    private const string LoginOk = "{\"idToken\":\"ID1\",\"refreshToken\":\"REF1\",\"expiresIn\":\"3600\"}";
    private DateTime _agora = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    private FirebaseAuthRest Criar(ServidorFalso servidor, string? senha = "segredo") =>
        new(new HttpClient(servidor), "CHAVE", "loja@exemplo.com", () => senha, () => _agora);

    [Fact]
    public async Task Entra_com_email_e_senha_e_guarda_o_token_na_memoria()
    {
        var servidor = new ServidorFalso().Responde(HttpStatusCode.OK, LoginOk);
        var auth = Criar(servidor);

        Assert.Equal("ID1", await auth.ObterAsync());
        Assert.Equal("ID1", await auth.ObterAsync()); // segunda vez não pergunta de novo

        var pedido = Assert.Single(servidor.Pedidos);
        Assert.Contains("accounts:signInWithPassword?key=CHAVE", pedido.Url);
        var corpo = JsonNode.Parse(pedido.Corpo)!;
        Assert.Equal("loja@exemplo.com", corpo["email"]!.GetValue<string>());
        Assert.Equal("segredo", corpo["password"]!.GetValue<string>());
        Assert.True(corpo["returnSecureToken"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Perto_de_vencer_renova_com_o_refresh_token_sem_pedir_a_senha()
    {
        var servidor = new ServidorFalso()
            .Responde(HttpStatusCode.OK, LoginOk)
            .Responde(HttpStatusCode.OK, "{\"id_token\":\"ID2\",\"refresh_token\":\"REF2\",\"expires_in\":\"3600\"}");
        var auth = Criar(servidor);
        await auth.ObterAsync();

        _agora = _agora.AddMinutes(59); // falta menos que a folga de 2 minutos

        Assert.Equal("ID2", await auth.ObterAsync());
        var renovacao = servidor.Pedidos[1];
        Assert.Contains("securetoken.googleapis.com/v1/token?key=CHAVE", renovacao.Url);
        Assert.Contains("grant_type=refresh_token", renovacao.Corpo);
        Assert.Contains("refresh_token=REF1", renovacao.Corpo);
        Assert.DoesNotContain("segredo", renovacao.Corpo);
    }

    [Fact]
    public async Task Se_a_renovacao_falhar_faz_login_de_novo()
    {
        var servidor = new ServidorFalso()
            .Responde(HttpStatusCode.OK, LoginOk)
            .Responde(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"TOKEN_EXPIRED\"}}")
            .Responde(HttpStatusCode.OK, "{\"idToken\":\"ID3\",\"refreshToken\":\"REF3\",\"expiresIn\":\"3600\"}");
        var auth = Criar(servidor);
        await auth.ObterAsync();
        _agora = _agora.AddHours(2);

        Assert.Equal("ID3", await auth.ObterAsync());
        Assert.Equal(3, servidor.Pedidos.Count);
    }

    [Fact]
    public async Task Invalidar_forca_um_token_novo()
    {
        var servidor = new ServidorFalso()
            .Responde(HttpStatusCode.OK, LoginOk)
            .Responde(HttpStatusCode.OK, "{\"id_token\":\"ID2\",\"refresh_token\":\"REF2\",\"expires_in\":\"3600\"}");
        var auth = Criar(servidor);
        await auth.ObterAsync();

        auth.Invalidar();

        Assert.Equal("ID2", await auth.ObterAsync());
    }

    [Theory]
    [InlineData("INVALID_LOGIN_CREDENTIALS", "E-mail ou senha do Firebase incorretos.")]
    [InlineData("EMAIL_NOT_FOUND", "E-mail ou senha do Firebase incorretos.")]
    [InlineData("INVALID_PASSWORD", "E-mail ou senha do Firebase incorretos.")]
    [InlineData("USER_DISABLED", "O usuário do Firebase está desativado.")]
    [InlineData("TOO_MANY_ATTEMPTS_TRY_LATER : Access to this account has been temporarily disabled", "Muitas tentativas de login. Aguarde alguns minutos e tente de novo.")]
    [InlineData("API key not valid. Please pass a valid API key.", "A chave da API do Firebase não é válida para este projeto.")]
    [InlineData("OPERATION_NOT_ALLOWED", "O login por e-mail e senha não está ligado no Firebase Authentication deste projeto.")]
    public async Task Erros_de_login_viram_mensagens_em_portugues_que_param_a_sincronizacao(string codigo, string esperado)
    {
        var servidor = new ServidorFalso().Responde(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"" + codigo + "\"}}");

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => Criar(servidor).ObterAsync());

        Assert.Equal(esperado, ex.Message);
        Assert.Equal(GravidadeFalha.Interrompe, ex.Gravidade);
        Assert.DoesNotContain("segredo", ex.Message);
    }

    [Fact]
    public async Task Sem_senha_salva_nem_tenta_falar_com_o_servidor()
    {
        var servidor = new ServidorFalso();

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => Criar(servidor, senha: null).ObterAsync());

        Assert.Contains("Falta a senha", ex.Message);
        Assert.Empty(servidor.Pedidos);
    }

    [Fact]
    public async Task Sem_internet_vira_mensagem_clara()
    {
        var servidor = new ServidorFalso().Falha(new HttpRequestException("falha de DNS"));

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => Criar(servidor).ObterAsync());

        Assert.Contains("Sem conexão com a internet", ex.Message);
    }
}

public class FirestoreJsonTests
{
    private static OperacaoCatalogo Operacao(bool ativo = true, string? categoria = "Roupas", string? codigo = "789")
    {
        var p = new ProdutoPublicavel(Guid.Parse("AAAAAAAA-BBBB-4CCC-8DDD-EEEEEEEEEEEE"), "Camiseta", categoria, null, codigo, 5990, false, ativo,
            new[] { new TamanhoPublicavel("M", 3), new TamanhoPublicavel("P", 2) });
        return new OperacaoCatalogo(new ItemPlano(p, "hash123"), ativo ? TipoOperacao.Gravar : TipoOperacao.Desativar);
    }

    private static readonly DateTime Agora = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Documento_do_produto_tem_o_id_do_guid_em_minusculas_e_so_os_campos_do_plano()
    {
        var escrita = FirestoreJson.Produto("loja-1a2b3", Operacao(), Agora);
        var update = escrita["update"]!;
        var campos = update["fields"]!.AsObject();

        Assert.Equal("projects/loja-1a2b3/databases/(default)/documents/produtos/aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee", update["name"]!.GetValue<string>());
        Assert.Equal("Camiseta", campos["nome"]!["stringValue"]!.GetValue<string>());
        Assert.Equal("5990", campos["precoVendaCentavos"]!["integerValue"]!.GetValue<string>()); // a API pede inteiro como texto
        Assert.True(campos["ativo"]!["booleanValue"]!.GetValue<bool>());
        Assert.Equal("5", campos["estoqueTotal"]!["integerValue"]!.GetValue<string>());
        Assert.Equal("hash123", campos["hashErp"]!["stringValue"]!.GetValue<string>());
        Assert.Equal("nexus-ls", campos["origem"]!["stringValue"]!.GetValue<string>());
        Assert.Equal("2026-10-19T12:00:00.0000000Z", campos["sincronizadoEm"]!["timestampValue"]!.GetValue<string>());

        var tamanhos = campos["tamanhos"]!["arrayValue"]!["values"]!.AsArray();
        Assert.Equal(2, tamanhos.Count);
        Assert.Equal("M", tamanhos[0]!["mapValue"]!["fields"]!["tamanho"]!["stringValue"]!.GetValue<string>());
        Assert.Equal("3", tamanhos[0]!["mapValue"]!["fields"]!["estoque"]!["integerValue"]!.GetValue<string>());
    }

    [Fact]
    public void Nenhum_dado_comercial_sensivel_vai_para_a_nuvem()
    {
        var json = FirestoreJson.Produto("loja-1a2b3", Operacao(), Agora).ToJsonString();

        foreach (var proibido in new[] { "custo", "Custo", "atacado", "Atacado", "minimo", "cliente", "venda\"", "divida" })
            Assert.DoesNotContain(proibido, json);
    }

    [Fact]
    public void A_mascara_lista_so_os_campos_do_erp_para_nao_apagar_foto_nem_descricao_da_vitrine()
    {
        var mascara = FirestoreJson.Produto("loja-1a2b3", Operacao(), Agora)["updateMask"]!["fieldPaths"]!.AsArray()
            .Select(n => n!.GetValue<string>()).ToList();

        Assert.Equal(FirestoreJson.CamposDoErp.OrderBy(x => x), mascara.OrderBy(x => x));
        foreach (var daVitrine in new[] { "fotos", "descricaoOnline", "destaque", "descricao" })
            Assert.DoesNotContain(daVitrine, mascara);
    }

    [Fact]
    public void Campo_opcional_vazio_sai_do_documento_mas_continua_na_mascara_para_ser_removido_no_destino()
    {
        var escrita = FirestoreJson.Produto("loja-1a2b3", Operacao(categoria: null, codigo: null), Agora);
        var campos = escrita["update"]!["fields"]!.AsObject();

        Assert.False(campos.ContainsKey("categoria"));
        Assert.False(campos.ContainsKey("codigoBarras"));
        var mascara = escrita["updateMask"]!["fieldPaths"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
        Assert.Contains("categoria", mascara);
        Assert.Contains("codigoBarras", mascara);
    }

    [Fact]
    public void Produto_desativado_vai_com_ativo_falso_e_nunca_como_exclusao()
    {
        var escrita = FirestoreJson.Produto("loja-1a2b3", Operacao(ativo: false), Agora);

        Assert.False(escrita["update"]!["fields"]!["ativo"]!["booleanValue"]!.GetValue<bool>());
        Assert.Null(escrita["delete"]); // a API de commit teria "delete": nada é apagado
    }

    [Fact]
    public void Meta_do_catalogo_leva_a_data_e_o_total()
    {
        var escrita = FirestoreJson.MetaCatalogo("loja-1a2b3", Agora, 42);

        Assert.EndsWith("/documents/meta/catalogo", escrita["update"]!["name"]!.GetValue<string>());
        Assert.Equal("42", escrita["update"]!["fields"]!["totalProdutosAtivos"]!["integerValue"]!.GetValue<string>());
    }
}

public class FirestoreRestDestinoTests
{
    private static OperacaoCatalogo Op(int n) => new(
        new ItemPlano(new ProdutoPublicavel(Guid.NewGuid(), $"Produto {n}", null, null, null, 1000, false, true,
            new[] { new TamanhoPublicavel("Sem tamanho", n) }), $"hash{n}"),
        TipoOperacao.Gravar);

    private static (FirestoreRestDestino Destino, ServidorFalso Servidor, TokenFixo Token) Criar()
    {
        var servidor = new ServidorFalso();
        var token = new TokenFixo();
        return (new FirestoreRestDestino(new HttpClient(servidor), "loja-1a2b3", token, () => new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)), servidor, token);
    }

    [Fact]
    public async Task Lote_vira_um_unico_commit_com_todas_as_escritas_e_o_token_no_cabecalho()
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(HttpStatusCode.OK, "{}");

        await destino.EnviarLoteAsync(new[] { Op(1), Op(2), Op(3) });

        var pedido = Assert.Single(servidor.Pedidos);
        Assert.Equal(HttpMethod.Post, pedido.Metodo);
        Assert.Equal("https://firestore.googleapis.com/v1/projects/loja-1a2b3/databases/(default)/documents:commit", pedido.Url);
        Assert.Equal("Bearer token-1", pedido.Autorizacao);
        Assert.Equal(3, JsonNode.Parse(pedido.Corpo)!["writes"]!.AsArray().Count);
    }

    [Fact]
    public async Task Lote_vazio_nao_chama_o_servidor()
    {
        var (destino, servidor, _) = Criar();

        await destino.EnviarLoteAsync(Array.Empty<OperacaoCatalogo>());

        Assert.Empty(servidor.Pedidos);
    }

    [Fact]
    public async Task Token_recusado_faz_login_novo_e_tenta_so_mais_uma_vez()
    {
        var (destino, servidor, token) = Criar();
        servidor.Responde(HttpStatusCode.Unauthorized, "{}").Responde(HttpStatusCode.OK, "{}");

        await destino.EnviarLoteAsync(new[] { Op(1) });

        Assert.Equal(2, servidor.Pedidos.Count);
        Assert.Equal("Bearer token-1", servidor.Pedidos[0].Autorizacao);
        Assert.Equal("Bearer token-2", servidor.Pedidos[1].Autorizacao);
        Assert.Equal(1, token.Invalidacoes);
    }

    [Fact]
    public async Task Duas_recusas_seguidas_de_token_param_com_mensagem_de_login()
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(HttpStatusCode.Unauthorized).Responde(HttpStatusCode.Unauthorized);

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { Op(1) }));

        Assert.Contains("login do Firebase foi recusado", ex.Message);
        Assert.Equal(GravidadeFalha.Interrompe, ex.Gravidade);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, GravidadeFalha.Interrompe, "Sem permissão para gravar")]
    [InlineData(HttpStatusCode.TooManyRequests, GravidadeFalha.Interrompe, "excesso de uso")]
    [InlineData(HttpStatusCode.Conflict, GravidadeFalha.Interrompe, "ocupado")]
    [InlineData(HttpStatusCode.InternalServerError, GravidadeFalha.Interrompe, "com problema")]
    [InlineData(HttpStatusCode.ServiceUnavailable, GravidadeFalha.Interrompe, "com problema")]
    [InlineData(HttpStatusCode.NotFound, GravidadeFalha.Interrompe, "projeto Firebase não foi encontrado")]
    [InlineData(HttpStatusCode.BadRequest, GravidadeFalha.DadosRecusados, "recusou")]
    public async Task Cada_erro_do_servidor_vira_a_gravidade_e_a_mensagem_certas(HttpStatusCode status, GravidadeFalha gravidade, string trecho)
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(status, "{\"error\":{\"message\":\"detalhe do servidor\"}}");

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { Op(1) }));

        Assert.Equal(gravidade, ex.Gravidade);
        Assert.Contains(trecho, ex.Message);
    }

    [Fact]
    public async Task Banco_firestore_nao_criado_tem_mensagem_propria()
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"The database (default) does not exist for project loja-1a2b3\"}}");

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { Op(1) }));

        Assert.Contains("banco Firestore ainda não foi criado", ex.Message);
    }

    [Fact]
    public async Task Sem_internet_e_tempo_esgotado_viram_mensagem_clara_e_param()
    {
        var (destino, servidor, _) = Criar();
        servidor.Falha(new HttpRequestException("sem rede")).Falha(new TaskCanceledException("timeout"));

        var a = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { Op(1) }));
        var b = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.EnviarLoteAsync(new[] { Op(1) }));

        Assert.Contains("Sem conexão com a internet", a.Message);
        Assert.Contains("demorou demais", b.Message);
        Assert.Equal(GravidadeFalha.Interrompe, b.Gravidade);
    }

    [Fact]
    public async Task Cancelar_nao_vira_falha_de_rede()
    {
        var (destino, servidor, _) = Criar();
        using var cancelamento = new CancellationTokenSource();
        cancelamento.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => destino.EnviarLoteAsync(new[] { Op(1) }, cancelamento.Token));
        Assert.Empty(servidor.Pedidos);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NotFound)] // o documento meta ainda não existe: tudo bem
    public async Task Verificar_aceita_200_e_404_do_documento_meta(HttpStatusCode status)
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(status, "{\"error\":{\"message\":\"Document not found\"}}");

        await destino.VerificarAsync();

        var pedido = Assert.Single(servidor.Pedidos);
        Assert.Equal(HttpMethod.Get, pedido.Metodo);
        Assert.EndsWith("/documents/meta/catalogo", pedido.Url);
    }

    [Fact]
    public async Task Verificar_sem_permissao_ou_sem_banco_avisa_antes_de_enviar_qualquer_coisa()
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(HttpStatusCode.Forbidden)
            .Responde(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"The database (default) does not exist\"}}");

        var permissao = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.VerificarAsync());
        var semBanco = await Assert.ThrowsAsync<FalhaDestinoException>(() => destino.VerificarAsync());

        Assert.Contains("Sem permissão", permissao.Message);
        Assert.Contains("ainda não foi criado", semBanco.Message);
    }

    [Fact]
    public async Task Registrar_atualizacao_grava_o_documento_meta()
    {
        var (destino, servidor, _) = Criar();
        servidor.Responde(HttpStatusCode.OK, "{}");

        await destino.RegistrarAtualizacaoAsync(new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc), 7);

        var escritas = JsonNode.Parse(Assert.Single(servidor.Pedidos).Corpo)!["writes"]!.AsArray();
        Assert.EndsWith("/documents/meta/catalogo", Assert.Single(escritas)!["update"]!["name"]!.GetValue<string>());
    }
}

public class ConfiguracaoFirebaseTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-fb-" + Guid.NewGuid().ToString("N"));
    private readonly ConfiguracaoFirebaseStore _store;

    public ConfiguracaoFirebaseTests()
    {
        Directory.CreateDirectory(_pasta);
        _store = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), new ProtetorFalso());
    }

    public void Dispose() { try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private static ConfiguracaoFirebase Valida() => new() { ProjectId = "loja-1a2b3", ApiKey = "AIzaChave", Email = "loja@exemplo.com" };

    [Fact]
    public void A_senha_nunca_fica_no_arquivo_como_foi_digitada()
    {
        _store.Salvar(Valida(), "minha-senha-secreta");

        var arquivo = File.ReadAllText(Path.Combine(_pasta, "firebase.json"));
        Assert.DoesNotContain("minha-senha-secreta", arquivo);
        Assert.Contains("PROT:", arquivo);
        Assert.Equal("minha-senha-secreta", _store.ObterSenha());
    }

    [Fact]
    public void Salvar_sem_senha_nova_mantem_a_que_ja_estava()
    {
        _store.Salvar(Valida(), "primeira");
        var mudou = Valida();
        mudou.Email = "outro@exemplo.com";

        _store.Salvar(mudou, senhaNova: "");

        Assert.Equal("primeira", _store.ObterSenha());
        Assert.Equal("outro@exemplo.com", _store.Carregar().Email);
        Assert.True(_store.Carregar().EstaCompleta);
    }

    [Fact]
    public void Sem_senha_a_configuracao_nao_esta_completa()
    {
        _store.Salvar(Valida(), null);

        Assert.False(_store.Carregar().EstaCompleta);
        Assert.Null(_store.ObterSenha());
    }

    [Theory]
    [InlineData("", "chave", "a@b.com")]
    [InlineData("Loja Maiuscula", "chave", "a@b.com")]
    [InlineData("abc", "chave", "a@b.com")]
    [InlineData("loja-1a2b3", " ", "a@b.com")]
    [InlineData("loja-1a2b3", "chave", "sem-arroba")]
    public void Dados_invalidos_sao_recusados_e_nada_e_gravado(string projeto, string chave, string email)
    {
        var cfg = new ConfiguracaoFirebase { ProjectId = projeto, ApiKey = chave, Email = email };

        Assert.Throws<RegraDeNegocioException>(() => _store.Salvar(cfg, "senha"));
        Assert.False(File.Exists(Path.Combine(_pasta, "firebase.json")));
    }

    [Fact]
    public void Arquivo_estragado_vira_configuracao_vazia_e_limpar_apaga()
    {
        File.WriteAllText(Path.Combine(_pasta, "firebase.json"), "{ quebrado");
        Assert.False(_store.Carregar().EstaCompleta);

        _store.Salvar(Valida(), "x");
        _store.Limpar();
        Assert.False(File.Exists(Path.Combine(_pasta, "firebase.json")));
    }

    [Fact]
    public void Espacos_nas_pontas_sao_aparados()
    {
        var cfg = new ConfiguracaoFirebase { ProjectId = "  loja-1a2b3 ", ApiKey = " AIzaChave ", Email = " loja@exemplo.com " };

        _store.Salvar(cfg, "s");

        var lida = _store.Carregar();
        Assert.Equal("loja-1a2b3", lida.ProjectId);
        Assert.Equal("AIzaChave", lida.ApiKey);
        Assert.Equal("loja@exemplo.com", lida.Email);
    }
}

/// <summary>Do banco local ao "Firestore" de mentira: o caminho inteiro, sem internet.</summary>
public sealed class SincronizacaoPontaAPontaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly SincronizacaoRepositorio _repo;

    public SincronizacaoPontaAPontaTests() => _repo = new SincronizacaoRepositorio(_banco.Fabrica);

    public void Dispose() => _banco.Dispose();

    [Fact]
    public async Task Tres_produtos_viram_um_commit_com_tres_escritas_e_depois_nada_a_enviar()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        for (var i = 1; i <= 3; i++) await produtos.CadastrarAsync(new DadosProduto($"Produto {i}", $"C{i}", 1000, 400, 1), 5);
        var servidor = new ServidorFalso();
        servidor.Regra = p => p.Metodo == HttpMethod.Get
            ? ServidorFalso.Json(HttpStatusCode.NotFound, "{}")
            : ServidorFalso.Json(HttpStatusCode.OK, "{}");
        var sincronizador = new SincronizadorCatalogo(_repo, new FirestoreRestDestino(new HttpClient(servidor), "loja-1a2b3", new TokenFixo()));

        var preparo = await sincronizador.PrepararAsync();
        var resultado = await sincronizador.ExecutarAsync(preparo.Plano!);

        Assert.Equal(ResultadoSincronizacao.Sucesso, resultado.Log.Resultado);
        var commits = servidor.Pedidos.Where(p => p.Url.EndsWith(":commit")).ToList();
        Assert.Equal(2, commits.Count); // os 3 produtos + a anotação meta/catalogo
        Assert.Equal(3, JsonNode.Parse(commits[0].Corpo)!["writes"]!.AsArray().Count);

        var depois = await sincronizador.PrepararAsync();
        Assert.False(depois.Plano!.TemQueEnviar);
    }

    [Fact]
    public async Task Lote_recusado_com_400_e_refeito_um_a_um_e_so_o_culpado_fica_de_fora()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var a = await produtos.CadastrarAsync(new DadosProduto("Bom 1", "1", 1000, 400, 1), 5);
        var ruim = await produtos.CadastrarAsync(new DadosProduto("Ruim", "2", 1000, 400, 1), 5);
        await produtos.CadastrarAsync(new DadosProduto("Bom 2", "3", 1000, 400, 1), 5);
        var servidor = new ServidorFalso();
        servidor.Regra = p =>
        {
            if (p.Metodo == HttpMethod.Get) return ServidorFalso.Json(HttpStatusCode.NotFound, "{}");
            var escritas = JsonNode.Parse(p.Corpo)!["writes"]!.AsArray();
            var temRuim = escritas.Any(e => e!["update"]!["name"]!.GetValue<string>().EndsWith(ruim.Guid.ToString().ToLowerInvariant()));
            return temRuim
                ? ServidorFalso.Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"documento inválido\"}}")
                : ServidorFalso.Json(HttpStatusCode.OK, "{}");
        };
        var sincronizador = new SincronizadorCatalogo(_repo, new FirestoreRestDestino(new HttpClient(servidor), "loja-1a2b3", new TokenFixo()));

        var preparo = await sincronizador.PrepararAsync();
        var resultado = await sincronizador.ExecutarAsync(preparo.Plano!);

        Assert.Equal(ResultadoSincronizacao.Parcial, resultado.Log.Resultado);
        Assert.Equal(2, resultado.Log.Criados);
        Assert.Equal("Ruim: o Firestore recusou: documento inválido", Assert.Single(resultado.FalhasPorProduto));
        Assert.Single((await _repo.PlanejarAsync()).Novos);
    }

    [Fact]
    public async Task Sem_permissao_o_botao_avisa_antes_de_enviar_e_nada_e_anotado()
    {
        await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("A", "1", 1000, 400, 1), 5);
        var servidor = new ServidorFalso().Responde(HttpStatusCode.Forbidden);
        var sincronizador = new SincronizadorCatalogo(_repo, new FirestoreRestDestino(new HttpClient(servidor), "loja-1a2b3", new TokenFixo()));

        var preparo = await sincronizador.PrepararAsync("Caixa");

        Assert.Null(preparo.Plano);
        Assert.Contains("Sem permissão", preparo.Erro);
        Assert.Single(servidor.Pedidos); // só a verificação; nenhum commit
        Assert.Empty(await _repo.ObterEstadoAsync());
        Assert.Equal(ResultadoSincronizacao.Falhou, Assert.Single(await _repo.UltimosLogsAsync()).Resultado);
    }
}

public sealed class DestinoConfiguravelTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-dc-" + Guid.NewGuid().ToString("N"));
    private readonly ConfiguracaoFirebaseStore _store;
    private readonly ServidorFalso _servidor = new();

    public DestinoConfiguravelTests()
    {
        Directory.CreateDirectory(_pasta);
        _store = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), new ProtetorFalso());
        _servidor.Regra = p =>
            p.Url.Contains("signInWithPassword")
                ? ServidorFalso.Json(HttpStatusCode.OK, "{\"idToken\":\"T-" + p.Url.GetHashCode() + "\",\"refreshToken\":\"R\",\"expiresIn\":\"3600\"}")
                : ServidorFalso.Json(HttpStatusCode.NotFound, "{}");
    }

    public void Dispose() { try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private DestinoFirestoreConfiguravel Criar() => new(_store, new HttpClient(_servidor));

    [Fact]
    public async Task Sem_configuracao_avisa_em_portugues_sem_tentar_a_rede()
    {
        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => Criar().VerificarAsync());

        Assert.Contains("ainda não foi configurado", ex.Message);
        Assert.Empty(_servidor.Pedidos);
    }

    [Fact]
    public async Task Sem_senha_salva_pede_a_senha()
    {
        _store.Salvar(new ConfiguracaoFirebase { ProjectId = "loja-1a2b3", ApiKey = "K", Email = "a@b.com" }, null);

        var ex = await Assert.ThrowsAsync<FalhaDestinoException>(() => Criar().VerificarAsync());

        Assert.Contains("Falta a senha", ex.Message);
    }

    [Fact]
    public async Task Usa_o_projeto_salvo_e_troca_de_projeto_sem_reiniciar()
    {
        _store.Salvar(new ConfiguracaoFirebase { ProjectId = "loja-aaaaa", ApiKey = "K", Email = "a@b.com" }, "senha");
        var destino = Criar();

        await destino.VerificarAsync();
        _store.Salvar(new ConfiguracaoFirebase { ProjectId = "loja-bbbbb", ApiKey = "K", Email = "a@b.com" }, "");
        await destino.VerificarAsync();

        var leituras = _servidor.Pedidos.Where(p => p.Url.Contains("firestore.googleapis.com")).Select(p => p.Url).ToList();
        Assert.Contains("projects/loja-aaaaa/", leituras[0]);
        Assert.Contains("projects/loja-bbbbb/", leituras[1]);
    }

    [Fact]
    public async Task Enquanto_nada_muda_reaproveita_o_login()
    {
        _store.Salvar(new ConfiguracaoFirebase { ProjectId = "loja-aaaaa", ApiKey = "K", Email = "a@b.com" }, "senha");
        var destino = Criar();

        await destino.VerificarAsync();
        await destino.VerificarAsync();

        Assert.Single(_servidor.Pedidos, p => p.Url.Contains("signInWithPassword"));
    }
}
