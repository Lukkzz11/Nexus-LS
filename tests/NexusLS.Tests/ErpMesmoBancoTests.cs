using System.Net;
using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Operacional;
using NexusLS.Core.Vitrine;

namespace NexusLS.Tests;

/// <summary>
/// O Nexus usa o MESMO Firebase do ERP, mas só para ler. Estes testes garantem: o "Testar conexão" só lê, nenhuma
/// configuração de gravação aponta para o projeto do ERP, e a gravação no formato do ERP fica desligada por padrão
/// (e, quando ligada nos testes, vai só para um destino falso). Nenhum teste fala com a internet.
/// </summary>
public sealed class ErpMesmoBancoTests : IDisposable
{
    private const string LoginOk = "{\"idToken\":\"ID1\",\"refreshToken\":\"REF1\",\"expiresIn\":\"3600\",\"localId\":\"uid1\",\"email\":\"dono@loja.com\"}";
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-erp-" + Guid.NewGuid().ToString("N"));

    public ErpMesmoBancoTests() => Directory.CreateDirectory(_pasta);
    public void Dispose() { try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private ContaErp Conta(ServidorFalso servidor, bool configurar = true)
    {
        var store = new ConfiguracaoErpStore(Path.Combine(_pasta, "erp.json"));
        if (configurar) store.Salvar(new ConfiguracaoErp { ProjectId = "pdv---lifesurf", ApiKey = "CHAVE-SECRETA-DO-TESTE", EmpresaId = "lifesurf-principal" });
        return new ContaErp(store, new HttpClient(servidor), new ProtetorFalso(), Path.Combine(_pasta, "sessao_erp.json"));
    }

    [Fact]
    public async Task Testar_conexao_entra_le_um_produto_e_nao_faz_nenhuma_escrita()
    {
        var servidor = new ServidorFalso()
            .Responde(HttpStatusCode.OK, LoginOk)
            .Responde(HttpStatusCode.OK, "[{\"document\":{\"name\":\"projects/p/databases/(default)/documents/empresas/e/produtos/abc\",\"fields\":{\"nome\":{\"stringValue\":\"Camiseta\"}}},\"readTime\":\"2026-11-05T10:00:00Z\"}]");

        var r = await Conta(servidor).TestarConexaoAsync("dono@loja.com", "senha-digitada");

        Assert.Equal(1, r.ProdutosLidos);
        Assert.Contains("Conexão confirmada", r.Mensagem);
        Assert.DoesNotContain("CHAVE-SECRETA", r.Mensagem + r.Email + r.ProjectId);
        Assert.Equal(2, servidor.Pedidos.Count);
        Assert.All(servidor.Pedidos, p => Assert.Equal(HttpMethod.Post, p.Metodo)); // login e runQuery (consulta); nunca PATCH/PUT/DELETE
        Assert.Contains("accounts:signInWithPassword", servidor.Pedidos[0].Url);
        Assert.EndsWith("/documents/empresas/lifesurf-principal:runQuery", servidor.Pedidos[1].Url);
        Assert.Contains("\"limit\":1", servidor.Pedidos[1].Corpo);
        Assert.DoesNotContain("commit", servidor.Pedidos[1].Url);
        // a senha não fica guardada em nenhum arquivo
        foreach (var arquivo in Directory.GetFiles(_pasta)) Assert.DoesNotContain("senha-digitada", File.ReadAllText(arquivo));
    }

    [Fact]
    public async Task Testar_conexao_sem_produtos_ainda_confirma_a_leitura()
    {
        var servidor = new ServidorFalso().Responde(HttpStatusCode.OK, LoginOk).Responde(HttpStatusCode.OK, "[{\"readTime\":\"2026-11-05T10:00:00Z\"}]");
        var r = await Conta(servidor).TestarConexaoAsync("dono@loja.com", "x");
        Assert.Equal(0, r.ProdutosLidos);
        Assert.Contains("Login confirmado", r.Mensagem);
    }

    [Fact]
    public async Task Testar_conexao_traduz_permissao_negada_e_senha_errada()
    {
        var negada = new ServidorFalso().Responde(HttpStatusCode.OK, LoginOk)
            .Responde(HttpStatusCode.Forbidden, "{\"error\":{\"message\":\"Missing or insufficient permissions.\"}}");
        await Assert.ThrowsAsync<FalhaDestinoException>(() => Conta(negada).TestarConexaoAsync("a@b.com", "x"));

        var senhaErrada = new ServidorFalso().Responde(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"INVALID_LOGIN_CREDENTIALS\"}}");
        var ex = await Assert.ThrowsAsync<FalhaLoginException>(() => Conta(senhaErrada).TestarConexaoAsync("a@b.com", "errada"));
        Assert.Equal(TipoFalhaLogin.Credenciais, ex.Tipo);
        Assert.Single(senhaErrada.Pedidos); // não tentou ler nada
    }

    [Fact]
    public async Task Testar_conexao_sem_configuracao_pede_para_configurar_e_nao_chama_a_rede()
    {
        var servidor = new ServidorFalso();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Conta(servidor, configurar: false).TestarConexaoAsync("a@b.com", "x"));
        Assert.Empty(servidor.Pedidos);
    }

    [Fact]
    public void Gravacao_no_erp_vem_desligada_e_a_configuracao_antiga_continua_lendo()
    {
        Assert.False(new ConfiguracaoErp().GravacaoAtivada);
        var caminho = Path.Combine(_pasta, "antigo.json");
        File.WriteAllText(caminho, "{\"ProjectId\":\"pdv---lifesurf\",\"ApiKey\":\"k\",\"EmpresaId\":\"lifesurf-principal\"}");
        var c = new ConfiguracaoErpStore(caminho).Carregar();
        Assert.False(c.GravacaoAtivada);
        Assert.True(c.EstaCompleta);
    }

    [Fact]
    public void Nenhum_destino_de_gravacao_aceita_o_projeto_do_erp()
    {
        var http = new HttpClient(new ServidorFalso());
        var token = new TokenFixo();
        Assert.Throws<FalhaDestinoException>(() => new FirestoreRestDestino(http, "pdv---lifesurf", token));
        Assert.Throws<FalhaDestinoException>(() => new FirestoreOperacionalDestino(http, "pdv---lifesurf", "loja", token));
        Assert.Throws<FalhaDestinoException>(() => new PublicadorVitrineRest(http, "pdv---lifesurf", token));
        Assert.Throws<FalhaDestinoException>(() => new StorageRestEnviador(http, "pdv---lifesurf.firebasestorage.app", token));
        // outros projetos continuam funcionando
        _ = new FirestoreRestDestino(http, "minha-loja-1a2b3", token);
    }

    [Fact]
    public void Configuracoes_do_catalogo_e_da_nuvem_recusam_salvar_o_projeto_do_erp()
    {
        var firebase = new ConfiguracaoFirebaseStore(Path.Combine(_pasta, "firebase.json"), new ProtetorFalso());
        var ex = Assert.Throws<RegraDeNegocioException>(() =>
            firebase.Salvar(new ConfiguracaoFirebase { ProjectId = "pdv---lifesurf", ApiKey = "k", Email = "a@b.com" }, "s"));
        Assert.Contains("somente leitura", ex.Message);
        Assert.False(File.Exists(Path.Combine(_pasta, "firebase.json")));

        var operacional = new ConfiguracaoOperacionalStore(Path.Combine(_pasta, "operacional.json"));
        Assert.Throws<RegraDeNegocioException>(() =>
            operacional.Salvar(new ConfiguracaoOperacional { ProjectId = "PDV---lifesurf", ApiKey = "k", LojaId = "loja" }));
        Assert.False(File.Exists(Path.Combine(_pasta, "operacional.json")));
    }

    // ---------------------------------------------------------------- gravação no formato do ERP (destino falso)

    private sealed class DestinoErpFalso : IDestinoErp
    {
        public List<DocumentoGravacaoErp> Recebidos { get; } = new();
        public Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default) { Recebidos.AddRange(documentos); return Task.CompletedTask; }
    }

    private static Produto Camiseta() => new()
    {
        Guid = Guid.Parse("11111111-2222-3333-4444-555555555555"), Nome = "Camiseta Onda", Categoria = "Camisetas", Referencia = "R1",
        CodigoBarras = "789", PrecoVendaCentavos = 5990, PrecoAtacadoCentavos = 4500, Ativo = true
    };

    private static readonly DateTime Agora = new(2026, 11, 5, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Gravador_recusa_tudo_com_a_gravacao_desligada()
    {
        var destino = new DestinoErpFalso();
        var gravador = new GravadorErp(new ConfiguracaoErp { EmpresaId = "lifesurf-principal" }, destino);
        var doc = MontadorErp.Produto("lifesurf-principal", Camiseta(), Array.Empty<ProdutoTamanho>(), Agora);
        var ex = await Assert.ThrowsAsync<RegraDeNegocioException>(() => gravador.GravarAsync(new[] { doc }));
        Assert.Contains("desligada", ex.Message);
        Assert.Empty(destino.Recebidos);
    }

    [Fact]
    public async Task Gravador_ligado_so_aceita_documentos_da_empresa_configurada()
    {
        var destino = new DestinoErpFalso();
        var gravador = new GravadorErp(new ConfiguracaoErp { EmpresaId = "lifesurf-principal", GravacaoAtivada = true }, destino);
        var bom = MontadorErp.Produto("lifesurf-principal", Camiseta(), Array.Empty<ProdutoTamanho>(), Agora);
        await gravador.GravarAsync(new[] { bom });
        Assert.Single(destino.Recebidos);

        var outro = MontadorErp.Produto("delas-fashion", Camiseta(), Array.Empty<ProdutoTamanho>(), Agora);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => gravador.GravarAsync(new[] { outro }));
        var raiz = bom with { Caminho = "produtos/x" };
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => gravador.GravarAsync(new[] { raiz }));
        Assert.Single(destino.Recebidos);
    }

    [Fact]
    public void Produto_no_formato_do_erp_usa_reais_grade_como_mapa_e_so_toca_os_campos_do_nexus()
    {
        var tamanhos = new[] { new ProdutoTamanho { Tamanho = "P", Estoque = 3 }, new ProdutoTamanho { Tamanho = "M", Estoque = 5 } };

        var atualiza = MontadorErp.Produto("lifesurf-principal", Camiseta(), tamanhos, Agora, idNoErp: "erp123");
        Assert.Equal("empresas/lifesurf-principal/produtos/erp123", atualiza.Caminho);
        Assert.False(atualiza.Criar);
        Assert.Equal(59.9, atualiza.Campos["precoVarejo"]);
        Assert.Equal(45.0, atualiza.Campos["precoAtacado"]);
        Assert.Equal("lifesurf-principal", atualiza.Campos["empresaId"]); // o carimbo que as regras do ERP exigem
        Assert.DoesNotContain("gradeTamanhos", atualiza.Mascara);          // estoque fora por padrão: dois escritores se sobrescreveriam
        Assert.DoesNotContain("criadoEm", atualiza.Campos.Keys);
        Assert.DoesNotContain("descricao", atualiza.Mascara);              // o que é do ERP (descrição, cores, fotos) não é tocado

        var novo = MontadorErp.Produto("lifesurf-principal", Camiseta(), tamanhos, Agora, novo: true, incluirEstoque: true);
        Assert.Equal("empresas/lifesurf-principal/produtos/11111111-2222-3333-4444-555555555555", novo.Caminho);
        Assert.True(novo.Criar);
        var grade = Assert.IsType<Dictionary<string, object?>>(novo.Campos["gradeTamanhos"]);
        Assert.Equal(3, grade["P"]);
        Assert.Equal(8, novo.Campos["estoqueTotal"]);
        Assert.Equal(Agora, novo.Campos["criadoEm"]);
    }

    [Fact]
    public void Vitrine_vai_no_campo_vitrine_do_documento_da_empresa_com_mascara_de_dois_campos()
    {
        var doc = MontadorErp.Vitrine("lifesurf-principal", "{\"modelo\":\"classico\",\"loja\":{\"nome\":\"Life\"}}", Agora);
        Assert.Equal("empresas/lifesurf-principal", doc.Caminho);
        Assert.Equal(new[] { "vitrine", "atualizadoEm" }, doc.Mascara);
        Assert.Throws<RegraDeNegocioException>(() => MontadorErp.Vitrine("lifesurf-principal", "{quebrado", Agora));
    }
}
