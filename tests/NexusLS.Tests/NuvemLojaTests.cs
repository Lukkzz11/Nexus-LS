using System.Net;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Text.Json.Nodes;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Core.Sincronizacao.Firebase;
using NexusLS.Core.Sincronizacao.Nuvem;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>"Compartilhar com a nuvem" e "Receber da nuvem" contra um Firestore simulado.</summary>
public sealed class NuvemLojaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly FirestoreNuvemFalso _nuvem = new();
    private readonly string _arquivoCache = Path.Combine(Path.GetTempPath(), "nexus_nuvem_" + Guid.NewGuid().ToString("N") + ".json");
    private readonly DateTime _agora = new(2026, 11, 10, 15, 0, 0, DateTimeKind.Utc);
    private readonly DateTime _hoje = new(2026, 11, 10);
    private bool _admin = true;
    private bool _pode = true;
    private string _lojaId = "loja-centro";
    private string _lojaNome = "Loja Centro";

    public void Dispose()
    {
        _banco.Dispose();
        foreach (var f in new[] { _arquivoCache, _arquivoCache + ".tmp" }) try { File.Delete(f); } catch (IOException) { }
    }

    private ClienteNuvemLoja Cliente(string empresa = "empresa1") => new(new HttpClient(_nuvem), "projeto-teste", empresa, new TokenDeTeste());

    private ServicoNuvemLoja Servico(Func<(string, string)>? loja = null, PrecoLojaService? precos = null) =>
        new(_banco.Fabrica, () => Cliente(), new CacheNuvemLoja(_arquivoCache), loja ?? (() => (_lojaId, _lojaNome)),
            precos ?? new PrecoLojaService(_banco.Fabrica, () => _agora), () => _admin, () => _pode, "1.0-teste", () => _agora, () => _hoje);

    private async Task<(Produto camiseta, Produto bone)> VenderAsync()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var vendas = new VendaService(_banco.Fabrica, () => _hoje.AddHours(10));
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica", "7891234567895", 5990, 2000, 2, "CAM-100", 3990), 0);
        await produtos.AdicionarTamanhoAsync(camiseta.Id, "M");
        await estoque.AjustarAsync(camiseta.Id, TipoMovimentacao.Entrada, 20, null, "M");
        var bone = await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "7890000000555", 2990, 800, 1, "BON-020"), 30);
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 2, "M"), new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix));
        await vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 4) }, 0, FormaPagamento.Dinheiro, 20000));
        return (camiseta, bone);
    }

    private static string Caminho(string empresa, string lojaId, string resto) => $"empresas/{empresa}/lojas/{lojaId}/{resto}";

    // ===================================================================== enviar
    [Fact]
    public async Task Envio_do_fechamento_nao_depende_da_permissao_e_sem_empresa_nao_envia()
    {
        await VenderAsync();
        _pode = false;
        var servico = Servico();

        Assert.Null(await servico.EnviarNoFechamentoAsync());
        Assert.Empty(_nuvem.CaminhosGravados);

        servico.SalvarEmpresa("empresa1");
        var r = await servico.EnviarNoFechamentoAsync();

        Assert.Equal(1, r!.Dias);
        Assert.Equal(2, r.Produtos);
    }

    [Fact]
    public async Task Compartilhar_envia_dia_estoque_e_loja_e_so_em_caminhos_de_loja()
    {
        await VenderAsync();
        var r = await Servico().CompartilharAsync();

        Assert.Equal(1, r.Dias);
        Assert.Equal(2, r.Produtos);
        Assert.True(_nuvem.Docs.ContainsKey("empresas/empresa1/lojas/loja-centro"));
        var dia = SerializadorPacoteLoja.DiaDeCampos(_nuvem.Docs[Caminho("empresa1", "loja-centro", "dias/2026-11-10")]);
        Assert.Equal(2, dia.NumeroDeVendas);
        Assert.Equal(2 * 5990 + 2990 + 4 * 2990, dia.FaturamentoCentavos);
        // nunca fora de lojas/precos: nada em produtos, clientes, vendas... do ERP
        Assert.All(_nuvem.CaminhosGravados, c => Assert.StartsWith("empresas/empresa1/lojas/loja-centro", c));
        Assert.DoesNotContain(_nuvem.CaminhosGravados, c => c.Contains("/produtos/") || c.Contains("/vendas/") || c.Contains("/clientes/"));
    }

    [Fact]
    public async Task Reenviar_e_idempotente_substitui_o_dia_e_nao_repete_estoque_sem_mudanca()
    {
        await VenderAsync();
        var servico = Servico();
        await servico.CompartilharAsync();
        var antes = _nuvem.Docs.ToDictionary(d => d.Key, d => d.Value.ToJsonString());

        var r2 = await servico.CompartilharAsync();

        Assert.Equal(antes.Count, _nuvem.Docs.Count);                       // nada duplicado
        Assert.Equal(0, r2.Produtos);                                       // estoque igual: não reenviou
        Assert.Equal(1, r2.Dias);
        foreach (var (k, v) in antes) Assert.Equal(v, _nuvem.Docs[k].ToJsonString());

        // vende mais: o dia é SUBSTITUÍDO (um documento só), e o estoque que mudou é reenviado
        var produtos = new ProdutoService(_banco.Fabrica);
        var bone = (await produtos.BuscarAsync("Boné"))[0];
        await new VendaService(_banco.Fabrica, () => _hoje.AddHours(11)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix));
        var r3 = await servico.CompartilharAsync();
        Assert.Equal(1, r3.Produtos);
        Assert.Equal(antes.Count, _nuvem.Docs.Count);
        Assert.Equal(3, SerializadorPacoteLoja.DiaDeCampos(_nuvem.Docs[Caminho("empresa1", "loja-centro", "dias/2026-11-10")]).NumeroDeVendas);
    }

    [Fact]
    public async Task Envio_respeita_o_limite_de_lote()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        for (var i = 0; i < 250; i++) await produtos.CadastrarAsync(new DadosProduto("Produto " + i, null, 1000 + i, 500, 0, "R" + i), 5);
        await Servico().CompartilharAsync();

        Assert.All(_nuvem.TamanhosDeCommit, n => Assert.True(n <= ClienteNuvemLoja.LimiteLoteEscrita));
        Assert.Equal(250, _nuvem.Docs.Keys.Count(k => k.Contains("/estoque/")));
        Assert.True(_nuvem.TamanhosDeCommit.Count >= 4);
    }

    [Fact]
    public async Task Sem_internet_nao_envia_nada_nem_muda_o_estado_local()
    {
        await VenderAsync();
        _nuvem.SemInternet = true;
        var ex = await Assert.ThrowsAsync<FalhaNuvemException>(() => Servico().CompartilharAsync());

        Assert.True(ex.SemConexao);
        Assert.Contains("internet", ex.Message);
        Assert.Empty(_nuvem.Docs);
        Assert.Null(new CacheNuvemLoja(_arquivoCache).Carregar().UltimoEnvioUtc);
    }

    [Fact]
    public async Task Falha_no_meio_nao_marca_como_enviado_e_o_proximo_clique_completa()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        for (var i = 0; i < 150; i++) await produtos.CadastrarAsync(new DadosProduto("Produto " + i, null, 1000, 500, 0, "R" + i), 5);
        _nuvem.FalharNoCommit = 2;

        await Assert.ThrowsAsync<FalhaNuvemException>(() => Servico().CompartilharAsync());
        var estado = new CacheNuvemLoja(_arquivoCache).Carregar();
        Assert.Null(estado.UltimoEnvioUtc);
        Assert.Empty(estado.HashesEstoque);
        Assert.False(_nuvem.Docs.ContainsKey("empresas/empresa1/lojas/loja-centro"));   // o documento da loja só vai depois do resto

        _nuvem.FalharNoCommit = 0;
        await Servico().CompartilharAsync();
        Assert.Equal(150, _nuvem.Docs.Keys.Count(k => k.Contains("/estoque/")));
        Assert.True(_nuvem.Docs.ContainsKey("empresas/empresa1/lojas/loja-centro"));
    }

    [Fact]
    public async Task Sem_permissao_na_nuvem_mostra_mensagem_clara_e_sem_permissao_no_programa_nem_tenta()
    {
        await VenderAsync();
        _nuvem.ForcarStatus = HttpStatusCode.Forbidden;
        var ex = await Assert.ThrowsAsync<FalhaNuvemException>(() => Servico().CompartilharAsync());
        Assert.Equal(TipoFalhaNuvem.SemPermissao, ex.Tipo);
        Assert.Contains("regras do Firestore", ex.Message);

        _nuvem.ForcarStatus = null;
        _nuvem.Pedidos.Clear();
        _pode = false;
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Servico().CompartilharAsync());
        Assert.Empty(_nuvem.Pedidos);
    }

    [Fact]
    public async Task Outro_computador_com_o_mesmo_codigo_e_detectado_e_so_segue_se_confirmar()
    {
        await VenderAsync();
        // outro computador já gravou a "mesma" loja (banco copiado), com nome e hora diferentes
        var intruso = MontadorPacoteLoja.Loja("loja-centro", "Loja Shopping", "1.0", _agora.AddHours(-3));
        _nuvem.Docs["empresas/empresa1/lojas/loja-centro"] = SerializadorPacoteLoja.ParaCampos(intruso);

        var ex = await Assert.ThrowsAsync<LojaIdDuplicadoException>(() => Servico().CompartilharAsync());
        Assert.Contains("Loja Shopping", ex.Message);
        Assert.Empty(_nuvem.CaminhosGravados);

        await Servico().CompartilharAsync(ignorarConflito: true);   // confirmou
        // depois disso, o próprio computador reconhece o documento como seu
        await Servico().CompartilharAsync();
        // e um terceiro envio de OUTRO computador faz o aviso voltar
        _nuvem.Docs["empresas/empresa1/lojas/loja-centro"] = SerializadorPacoteLoja.ParaCampos(MontadorPacoteLoja.Loja("loja-centro", "Loja Centro", "1.0", _agora.AddMinutes(30)));
        await Assert.ThrowsAsync<LojaIdDuplicadoException>(() => Servico().CompartilharAsync());
    }

    [Fact]
    public async Task Primeiro_envio_com_o_mesmo_nome_na_nuvem_nao_e_tratado_como_duplicado()
    {
        _nuvem.Docs["empresas/empresa1/lojas/loja-centro"] = SerializadorPacoteLoja.ParaCampos(MontadorPacoteLoja.Loja("loja-centro", "Loja Centro", "0.9", _agora.AddDays(-1)));
        await Servico().CompartilharAsync();   // reinstalação da mesma loja: segue
        Assert.Equal("1.0-teste", SerializadorPacoteLoja.LojaDeCampos(_nuvem.Docs["empresas/empresa1/lojas/loja-centro"]).VersaoNexus);
    }

    [Fact]
    public async Task Cliente_recusa_o_projeto_do_erp_e_caminhos_fora_de_lojas()
    {
        Assert.Throws<FalhaNuvemException>(() => new ClienteNuvemLoja(new HttpClient(_nuvem), "pdv---lifesurf", "empresa1", new TokenDeTeste()));
        var c = Cliente();
        foreach (var ruim in new[] { "empresas/empresa1/produtos/abc", "empresas/empresa1/vendas/x", "empresas/outra/lojas/a", "empresas/empresa1/clientes/z", "empresas/empresa1/precos/p" })
            await Assert.ThrowsAsync<FalhaNuvemException>(() => c.GravarAsync(new[] { new DocumentoNuvem(ruim, new JsonObject()) }));
        Assert.Empty(_nuvem.Pedidos);
    }

    // ===================================================================== comparativo / cache
    [Fact]
    public async Task Administrador_ve_todas_as_lojas_e_usuario_de_loja_so_a_propria()
    {
        await VenderAsync();
        await Servico().CompartilharAsync();
        // outra loja, já na nuvem
        var dia2 = new ResumoDiaNuvem(_hoje, 100000, 4, 8, 0, 0, 0, 0, new Dictionary<string, long>(), Array.Empty<ItemDiaNuvem>(), _agora);
        _nuvem.Docs["empresas/empresa1/lojas/loja-praia"] = SerializadorPacoteLoja.ParaCampos(MontadorPacoteLoja.Loja("loja-praia", "Loja Praia", "1.0", _agora));
        _nuvem.Docs[Caminho("empresa1", "loja-praia", "dias/2026-11-10")] = SerializadorPacoteLoja.ParaCampos(dia2);

        var todas = await Servico().LerLojasAsync(_hoje.AddDays(-6), _hoje);
        Assert.False(todas.DoCache);
        Assert.Equal(2, todas.Lojas.Count);
        var praia = todas.Lojas.Single(l => l.LojaId == "loja-praia");
        Assert.Equal(100000, praia.FaturamentoCentavos);
        Assert.Equal(25000, praia.TicketMedioCentavos);
        Assert.True(todas.Lojas.Single(l => l.LojaId == "loja-centro").EstaLoja);

        _admin = false;
        _nuvem.Pedidos.Clear();
        var propria = await Servico().LerLojasAsync(_hoje.AddDays(-6), _hoje);
        Assert.Single(propria.Lojas);
        Assert.Equal("loja-centro", propria.Lojas[0].LojaId);
        Assert.DoesNotContain(_nuvem.Pedidos, p => p.Contains("pageSize"));   // nem tenta listar as lojas
    }

    [Fact]
    public async Task Sem_internet_mostra_o_ultimo_dado_baixado_com_a_data()
    {
        await VenderAsync();
        await Servico().CompartilharAsync();
        var online = await Servico().LerLojasAsync(_hoje.AddDays(-6), _hoje);

        _nuvem.SemInternet = true;
        var offline = await Servico().LerLojasAsync(_hoje.AddDays(-6), _hoje);

        Assert.True(offline.DoCache);
        Assert.Equal(online.Lojas[0].FaturamentoCentavos, offline.Lojas[0].FaturamentoCentavos);
        Assert.Equal(_agora, offline.DadosDeUtc);
        Assert.Contains("último dado baixado", offline.Aviso);
        Assert.Contains(_agora.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), offline.Aviso);
    }

    [Fact]
    public async Task Sem_internet_e_sem_nada_baixado_avisa_e_nao_quebra()
    {
        _nuvem.SemInternet = true;
        var r = await Servico().LerLojasAsync(_hoje.AddDays(-6), _hoje);
        Assert.True(r.DoCache);
        Assert.Empty(r.Lojas);
        Assert.Contains("Ainda não há nenhum dado baixado", r.Aviso);
    }

    [Fact]
    public async Task Arquivo_de_cache_estragado_vira_estado_vazio()
    {
        File.WriteAllText(_arquivoCache, "{ isto não é json");
        Assert.Equal("", new CacheNuvemLoja(_arquivoCache).Carregar().EmpresaId);
        _nuvem.SemInternet = true;
        var r = await Servico().LerLojasAsync(_hoje.AddDays(-6), _hoje);
        Assert.Empty(r.Lojas);
    }

    // ===================================================================== preço por loja
    [Fact]
    public async Task Preco_por_loja_definido_pelo_admin_e_recebido_vale_no_pdv_sem_mexer_no_cadastro()
    {
        var (camiseta, bone) = await VenderAsync();
        var precos = new PrecoLojaService(_banco.Fabrica, () => _agora);
        var admin = Servico(precos: precos);

        await admin.DefinirPrecoAsync(camiseta.Guid, "loja-centro", 4990, 3500, "Lucas");
        await admin.DefinirPrecoAsync(camiseta.Guid, "loja-praia", 6990, null, "Lucas");   // outra loja: não deve chegar aqui
        Assert.Contains("empresas/empresa1/precos/" + camiseta.Guid.ToString("D") + "/lojas/loja-centro", _nuvem.Docs.Keys);

        _admin = false;   // agora é o usuário da loja
        var r = await Servico(precos: precos).ReceberPrecosAsync();
        Assert.Equal(1, r.Aplicados);

        // o carrinho do PDV usa o preço da loja
        var carrinho = new Carrinho { PrecoDaLoja = precos.PrecoDe };
        await using (var db = _banco.CriarContexto())
        {
            var p = db.Produtos.Include(x => x.Tamanhos).First(x => x.Id == camiseta.Id);
            var linha = carrinho.Adicionar(p, p.Tamanhos.First(t => t.Tamanho == "M"), 1);
            Assert.Equal(4990, linha.PrecoUnitarioCentavos);
            Assert.Equal(3500, linha.PrecoAtacadoCentavos);
        }

        // e a venda gravada também
        var venda = await new VendaService(_banco.Fabrica, () => _hoje.AddHours(12)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 1, "M") }, 0, FormaPagamento.Pix));
        Assert.Equal(4990, venda.TotalCentavos);
        var atacado = await new VendaService(_banco.Fabrica, () => _hoje.AddHours(12)).FinalizarAsync(
            new PedidoVenda(new[] { new ItemCarrinho(camiseta.Id, 1, "M") }, 0, FormaPagamento.Pix, TabelaPreco: TabelaPreco.Atacado));
        Assert.Equal(3500, atacado.TotalCentavos);

        // produto sem preço de loja continua com o preço do cadastro
        var v2 = await new VendaService(_banco.Fabrica, () => _hoje.AddHours(12)).FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(bone.Id, 1) }, 0, FormaPagamento.Pix));
        Assert.Equal(2990, v2.TotalCentavos);

        // o cadastro (que sincroniza com o ERP) e o que vai ao ERP não mudaram
        await using var db2 = _banco.CriarContexto();
        var cad = db2.Produtos.First(x => x.Id == camiseta.Id);
        Assert.Equal(5990, cad.PrecoVendaCentavos);
        Assert.Equal(3990, cad.PrecoAtacadoCentavos);
        Assert.DoesNotContain("PrecoLoja", string.Join(",", FirestoreJson.CamposDoErp));
    }

    [Fact]
    public async Task Receber_precos_remove_o_que_saiu_da_nuvem_e_falha_de_rede_nao_muda_nada()
    {
        var (camiseta, _) = await VenderAsync();
        var precos = new PrecoLojaService(_banco.Fabrica, () => _agora);
        var s = Servico(precos: precos);
        await s.DefinirPrecoAsync(camiseta.Guid, "loja-centro", 4990, null, "Lucas");
        await s.ReceberPrecosAsync();
        Assert.Single(await precos.ListarAsync());

        _nuvem.SemInternet = true;
        await Assert.ThrowsAsync<FalhaNuvemException>(() => s.ReceberPrecosAsync());
        Assert.Single(await precos.ListarAsync());           // continua com o último recebido

        _nuvem.SemInternet = false;
        _nuvem.Docs.Remove("empresas/empresa1/precos/" + camiseta.Guid.ToString("D") + "/lojas/loja-centro");
        var r = await s.ReceberPrecosAsync();
        Assert.Equal(1, r.Removidos);
        Assert.Empty(await precos.ListarAsync());
    }

    [Fact]
    public async Task Usuario_de_loja_nao_define_nem_le_precos_de_outras_lojas()
    {
        var (camiseta, _) = await VenderAsync();
        _admin = false;
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Servico().DefinirPrecoAsync(camiseta.Guid, "loja-centro", 4990, null, "x"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => Servico().LerPrecosDoProdutoAsync(camiseta.Guid, new[] { "loja-praia" }));
        Assert.Empty(_nuvem.CaminhosGravados);
    }

    [Fact]
    public async Task Preco_invalido_e_recusado_e_o_admin_le_o_preco_de_cada_loja()
    {
        var (camiseta, _) = await VenderAsync();
        var s = Servico();
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.DefinirPrecoAsync(camiseta.Guid, "loja-centro", 0, null, "x"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.DefinirPrecoAsync(camiseta.Guid, "loja-centro", 1000, 2000, "x"));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.DefinirPrecoAsync(camiseta.Guid, "../erp", 1000, null, "x"));
        await s.DefinirPrecoAsync(camiseta.Guid, "loja-praia", 7000, 5000, "Lucas");
        var mapa = await s.LerPrecosDoProdutoAsync(camiseta.Guid, new[] { "loja-praia", "loja-centro" });
        Assert.Single(mapa);
        Assert.Equal(7000, mapa["loja-praia"].PrecoVarejoCentavos);
        Assert.Equal(5000, mapa["loja-praia"].PrecoAtacadoCentavos);
    }
}
