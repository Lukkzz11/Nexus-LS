using NexusLS.Core.Importacao.Erp;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class ReferenciaTamanhoEEnvioAutomaticoTests
{
    private sealed class DestinoFalso : IDestinoErp
    {
        public List<IReadOnlyList<DocumentoGravacaoErp>> Chamadas { get; } = new();
        public Task GravarAsync(IReadOnlyList<DocumentoGravacaoErp> documentos, CancellationToken ct = default)
        {
            Chamadas.Add(documentos);
            return Task.CompletedTask;
        }
        public IEnumerable<DocumentoGravacaoErp> Todos => Chamadas.SelectMany(c => c);
    }

    private static ConfiguracaoErp Config(bool ligada = true) =>
        new() { ProjectId = "projeto-teste", ApiKey = "x", EmpresaId = "empresa-teste", GravacaoAtivada = ligada };

    [Fact]
    public async Task Vender_sem_baixa_so_nos_produtos_escolhidos()
    {
        using var banco = new BancoDeTeste();
        var s = new ProdutoService(banco.Fabrica);
        var a = await s.CadastrarAsync(new DadosProduto("A", null, 100, 50, 0));
        var b = await s.CadastrarAsync(new DadosProduto("B", null, 100, 50, 0));
        var c = await s.CadastrarAsync(new DadosProduto("C", null, 100, 50, 0));

        Assert.Equal(2, await s.DefinirVenderSemBaixaAsync(new[] { a.Id, c.Id }, true));
        Assert.Equal(0, await s.DefinirVenderSemBaixaAsync(new[] { a.Id }, true)); // já estava

        Assert.True((await s.ObterAsync(a.Id))!.VenderSemBaixa);
        Assert.False((await s.ObterAsync(b.Id))!.VenderSemBaixa);
        Assert.True((await s.ObterAsync(c.Id))!.VenderSemBaixa);

        await s.DefinirVenderSemBaixaAsync(new[] { a.Id }, false);
        Assert.False((await s.ObterAsync(a.Id))!.VenderSemBaixa);
    }

    [Fact]
    public async Task Tamanho_ganha_codigo_da_referencia_mais_o_numero_e_o_numero_nunca_e_reaproveitado()
    {
        using var banco = new BancoDeTeste();
        var s = new ProdutoService(banco.Fabrica);
        var p = await s.CadastrarAsync(new DadosProduto("Camisa", "5555", 5000, 2000, 0, Referencia: "5555"));
        await s.AdicionarGradeAsync(p.Id, new[] { "P", "M", "G" });

        var grade = await s.ListarTamanhosAsync(p.Id);
        Assert.Equal("55551", grade.Single(t => t.Tamanho == "P").CodigoBarras);
        Assert.Equal("55552", grade.Single(t => t.Tamanho == "M").CodigoBarras);
        Assert.Equal("55553", grade.Single(t => t.Tamanho == "G").CodigoBarras);
        Assert.Null(grade.Single(t => t.EhSemTamanho).CodigoBarras);

        await s.RemoverTamanhoAsync(p.Id, "M");
        await s.AdicionarTamanhoAsync(p.Id, "GG");
        grade = await s.ListarTamanhosAsync(p.Id);
        Assert.Equal("55553", grade.Single(t => t.Tamanho == "G").CodigoBarras); // não mudou
        Assert.Equal("55554", grade.Single(t => t.Tamanho == "GG").CodigoBarras); // número novo
    }

    [Fact]
    public void Ean13_nao_recebe_numero_de_tamanho()
    {
        Assert.Equal("7891234567895", CodigoBarrasTamanho.Montar("7891234567895", 2));
        Assert.Equal("55552", CodigoBarrasTamanho.Montar("5555", 2));
        Assert.Equal("5555", CodigoBarrasTamanho.Montar("5555", 0));
    }

    [Fact]
    public async Task Codigo_lido_com_numero_do_tamanho_acha_produto_e_tamanho_e_o_codigo_exato_nao_e_confundido()
    {
        using var banco = new BancoDeTeste();
        var s = new ProdutoService(banco.Fabrica);
        var p = await s.CadastrarAsync(new DadosProduto("Camisa", "5555", 5000, 2000, 0));
        await s.AdicionarGradeAsync(p.Id, new[] { "P", "M" });

        var achou = await s.ObterPorCodigoDeTamanhoAsync("55552");
        Assert.NotNull(achou);
        Assert.Equal(p.Id, achou!.Value.Produto.Id);
        Assert.Equal("M", achou.Value.Tamanho.Tamanho);

        Assert.Null(await s.ObterPorCodigoDeTamanhoAsync("5555"));   // é o código do produto
        Assert.Null(await s.ObterPorCodigoDeTamanhoAsync("55559"));  // não existe o tamanho 9
        Assert.Null(await s.ObterPorCodigoDeTamanhoAsync("abc"));
    }

    [Fact]
    public void Proxima_referencia_segue_o_modelo_pelas_duas_primeiras_casas()
    {
        var existentes = new[] { "1402", "1403", "1404", "1509", "1509X", "1510", "0828", "0829" };
        Assert.Equal("1405", ProdutoService.ProximaReferencia("1403", existentes));
        Assert.Equal("1511", ProdutoService.ProximaReferencia("1509", existentes));
        Assert.Equal("0830", ProdutoService.ProximaReferencia("0828", existentes)); // zero à esquerda preservado
        Assert.Throws<RegraDeNegocioException>(() => ProdutoService.ProximaReferencia("1509X", existentes));
    }

    [Fact]
    public async Task Sugerir_referencia_segue_o_modelo_da_categoria()
    {
        using var banco = new BancoDeTeste();
        var s = new ProdutoService(banco.Fabrica);
        Assert.Equal("1401", await s.SugerirReferenciaAsync("bermuda"));   // categoria sem produtos: começa no modelo
        Assert.Equal("0801", await s.SugerirReferenciaAsync("Short"));
        await s.CadastrarAsync(new DadosProduto("Bermuda A", "1403", 1000, 500, 0, Referencia: "1403", Categoria: "Bermuda"));
        Assert.Equal("1404", await s.SugerirReferenciaAsync("Bermuda"));
        Assert.Equal(13, (await s.SugerirReferenciaAsync("Categoria nova")).Length); // sem modelo: código interno EAN-13
    }

    [Fact]
    public async Task Modelo_da_referencia_copia_dados_e_tamanhos_e_sugere_a_proxima()
    {
        using var banco = new BancoDeTeste();
        var s = new ProdutoService(banco.Fabrica);
        var b = await s.CadastrarAsync(new DadosProduto("Bermuda Jeans", "1403", 12000, 5000, 2, Referencia: "1403", Categoria: "Bermuda", Cores: "Azul"));
        await s.AdicionarGradeAsync(b.Id, new[] { "38", "40" });

        var modelo = await s.ModeloDaReferenciaAsync("1403");
        Assert.Equal("1404", modelo.NovaReferencia);
        Assert.Equal(new[] { "38", "40" }, modelo.Tamanhos);
        Assert.Equal("Bermuda", modelo.Base.Categoria);
        Assert.Equal(12000, modelo.Base.PrecoVendaCentavos);
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => s.ModeloDaReferenciaAsync("9999"));
    }

    [Fact]
    public async Task Ajuste_manual_de_estoque_vai_como_diferenca_so_depois_da_primeira_vez_e_uma_unica_vez()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var estoque = new EstoqueService(banco.Fabrica);
        var p = await produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1, Referencia: "REF1"), 10);
        await produtos.AdicionarTamanhoAsync(p.Id, "M");

        // 1º envio: cria o produto no ERP já com o saldo; nenhum ajuste antigo é reenviado
        var primeiro = new DestinoFalso();
        await new EnviadorErp(banco.Fabrica, Config(), primeiro).EnviarAsync();
        Assert.DoesNotContain(primeiro.Todos, d => d.Incrementos is not null);

        await estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, 5, "Chegou mais", "M");

        var segundo = new DestinoFalso();
        await new EnviadorErp(banco.Fabrica, Config(), segundo).EnviarAsync();
        var inc = segundo.Todos.Single(d => d.Incrementos is not null);
        Assert.Equal(5, inc.Incrementos!["estoqueTotal"]);
        Assert.Equal(5, inc.Incrementos["gradeTamanhos.M"]);
        Assert.Contains(segundo.Todos, d => d.Caminho.Contains("/movimentacoes_estoque/nx-mov-"));

        var terceiro = new DestinoFalso();
        await new EnviadorErp(banco.Fabrica, Config(), terceiro).EnviarAsync();
        Assert.Empty(terceiro.Chamadas);
    }

    [Fact]
    public async Task Envio_automatico_so_envia_quando_ha_alteracao_e_a_gravacao_esta_ligada()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        await produtos.CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1, Referencia: "REF1"), 3);

        var destino = new DestinoFalso();
        long alteracoes = 1;
        var ligada = true;
        var auto = new EnvioErpAutomatico(() => Config(ligada),
            _ => Task.FromResult<EnviadorErp?>(new EnviadorErp(banco.Fabrica, Config(ligada), destino)), () => alteracoes);

        ligada = false;
        Assert.Null(await auto.TentarAsync());                  // desligado: nada
        Assert.Empty(destino.Chamadas);

        ligada = true;
        var r = await auto.TentarAsync();
        Assert.Equal(1, r!.Produtos);
        var chamadas = destino.Chamadas.Count;

        Assert.Null(await auto.TentarAsync());                  // nada mudou desde o último envio
        Assert.Equal(chamadas, destino.Chamadas.Count);

        alteracoes++;                                           // algo foi gravado no banco
        Assert.NotNull(await auto.TentarAsync());
    }

    [Fact]
    public async Task Gravar_dados_do_negocio_sobe_o_contador_de_alteracoes()
    {
        using var banco = new BancoDeTeste();
        var antes = NexusLS.Core.Data.PdvDbContext.Alteracoes;
        await new ProdutoService(banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1));
        var depois = NexusLS.Core.Data.PdvDbContext.Alteracoes;
        Assert.True(depois > antes);

    }

    [Fact]
    public void Resolver_prefere_ean13_depois_referencia_e_usa_a_ordem_cadastrada()
    {
        var produto = new Produto
        {
            Nome = "Camisa",
            Referencia = "5555",
            CodigoInterno = "INT-000001",
            Tamanhos = new List<ProdutoTamanho>
            {
                new() { Tamanho = Tamanhos.SemTamanho, Ordem = 0 },
                new() { Tamanho = "P", Ordem = 7 },
                new() { Tamanho = "M", Ordem = 9 }
            }
        };

        Assert.Equal("55557", CodigoBarrasTamanho.Resolver(produto, "P"));
        Assert.Equal("55559", CodigoBarrasTamanho.Resolver(produto, "M"));
        Assert.Equal("5555", CodigoBarrasTamanho.Resolver(produto, Tamanhos.SemTamanho));
        Assert.Equal("5555", CodigoBarrasTamanho.Resolver(produto, null));

        produto.CodigoBarras = "7891234567895"; // EAN-13 identifica o produto inteiro: não recebe sufixo
        Assert.Equal("7891234567895", CodigoBarrasTamanho.Resolver(produto, "P"));
    }

    [Fact]
    public void Resolver_usa_codigo_curto_quando_nao_ha_referencia_e_codigo_interno_como_ultimo_recurso()
    {
        var produto = new Produto { Nome = "Camisa", CodigoBarras = "12345", CodigoInterno = "INT-000001" };
        Assert.Equal("123452", CodigoBarrasTamanho.Resolver(produto, "2"));

        produto.CodigoBarras = null;
        Assert.Equal("INT-0000012", CodigoBarrasTamanho.Resolver(produto, "2"));
    }

    [Fact]
    public void Validar_aponta_codigo_vazio_acento_e_barras_finas_demais()
    {
        Assert.Contains(CodigoBarrasTamanho.Validar(null, 50), e => e.Contains("sem código"));
        Assert.Contains(CodigoBarrasTamanho.Validar("CAMISÉ", 50), e => e.Contains("acento"));
        Assert.Contains(CodigoBarrasTamanho.Validar("55551", 20), e => e.Contains("0,25"));
        Assert.Empty(CodigoBarrasTamanho.Validar("55551", 50));
    }

    [Fact]
    public void Ordem_generica_segue_a_sequencia_natural_e_sem_codigo_devolve_nulo()
    {
        Assert.Equal(1, CodigoBarrasTamanho.ObterOrdemGenerica("PP"));
        Assert.Equal(2, CodigoBarrasTamanho.ObterOrdemGenerica("P"));
        Assert.Equal(3, CodigoBarrasTamanho.ObterOrdemGenerica("M"));
        Assert.Equal(4, CodigoBarrasTamanho.ObterOrdemGenerica("G"));
        Assert.Equal(0, CodigoBarrasTamanho.ObterOrdemGenerica(Tamanhos.SemTamanho));

        Assert.Null(CodigoBarrasTamanho.Resolver(new Produto { Nome = "Sem codigo" }, "P"));
    }

    [Fact]
    public void Ordem_zero_no_cadastro_nao_ganha_sufixo()
    {
        var produto = new Produto
        {
            Nome = "Camisa",
            Referencia = "5555",
            Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = "P", Ordem = 0 } }
        };

        Assert.Equal(0, CodigoBarrasTamanho.ObterOrdem(produto, "P"));
        Assert.Equal("5555", CodigoBarrasTamanho.Resolver(produto, "P"));
    }

    [Fact]
    public async Task Etiqueta_e_leitor_do_PDV_concordam_quando_a_referencia_difere_do_codigo_curto()
    {
        using var banco = new BancoDeTeste();
        var s = new ProdutoService(banco.Fabrica);
        var p = await s.CadastrarAsync(new DadosProduto("Camisa", "12345", 5000, 2000, 0, Referencia: "5555"));
        await s.AdicionarGradeAsync(p.Id, new[] { "P", "M" });

        var grade = await s.ListarTamanhosAsync(p.Id);
        Assert.Equal("55551", grade.Single(t => t.Tamanho == "P").CodigoBarras);

        var achou = await s.ObterPorCodigoDeTamanhoAsync("55551");
        Assert.Equal("P", achou!.Value.Tamanho.Tamanho);

        // Etiqueta antiga (código curto + ordem) continua sendo reconhecida.
        Assert.Equal("M", (await s.ObterPorCodigoDeTamanhoAsync("123452"))!.Value.Tamanho.Tamanho);
    }

    [Fact]
    public void Campo_zpl_escapa_caracteres_de_comando()
    {
        Assert.Equal("^FH^FDA_5EB_7EC_5F^FS", NexusLS.Core.Etiquetas.CampoZpl.Montar("A^B~C_"));
    }
}
