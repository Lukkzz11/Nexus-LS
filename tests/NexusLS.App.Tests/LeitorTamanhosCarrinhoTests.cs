using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Ida e volta do código de barras do tamanho (BarTender gera → PDV lê) e o comportamento de leitor no carrinho:
/// vários tamanhos do mesmo produto acumulam numa entrada só (1435 — P×1, M×1).
///
/// Regra de desempate do PDV ao ler um código só de dígitos:
///  1) código IDÊNTICO ao de um produto ativo (código de barras, código interno ou referência) vence sempre
///     (a ref. 10192 ganha da ref. 1019 tamanho 2, que também imprime 10192);
///  2) senão, vale a MAIOR base (referência/código curto) que seja prefixo do código e cujo resto seja o número de um
///     tamanho existente (143512 → ref. 14351 tam. 2, se existir; senão ref. 1435 tam. 12);
///  3) o número do tamanho não começa com zero; EAN-13 identifica o produto inteiro e nunca leva sufixo.
/// </summary>
public sealed class LeitorTamanhosCarrinhoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-leitor-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private readonly PdvViewModel _vm;

    public LeitorTamanhosCarrinhoTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _estoque = new EstoqueService(_banco.Fabrica);
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        var cupons = new CupomService(_banco.Fabrica, configuracao, new ImpressoraDeMentira());
        var rascunho = new RascunhoService(new RascunhoStore(Path.Combine(_pasta, "rascunho.json")), _banco.Fabrica);
        _vm = new PdvViewModel(_produtos, new VendaService(_banco.Fabrica), rascunho, new ClienteService(_banco.Fabrica),
            cupons, TimeSpan.Zero, configuracao);
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private static readonly string[] DozeTamanhos = { "36", "38", "40", "42", "44", "46", "48", "50", "52", "54", "56", "58" };

    private async Task<Produto> CriarAsync(string nome, string? codigoBarras, string? referencia, params string[] tamanhos)
    {
        var p = await _produtos.CadastrarAsync(new DadosProduto(nome, codigoBarras, 5990, 2000, 0, referencia));
        if (tamanhos.Length > 0) await _produtos.AdicionarGradeAsync(p.Id, tamanhos);
        return p;
    }

    private async Task<Produto> ComGradeAsync(int id)
    {
        var p = (await _produtos.ObterAsync(id))!;
        p.Tamanhos = await _produtos.ListarTamanhosAsync(id);
        return p;
    }

    private async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), $"Tempo esgotado esperando: {quando}. Etapa={_vm.Etapa}, mensagem=\"{_vm.Mensagem}\"");
    }

    /// <summary>O leitor "digita" o código e manda Enter; o PDV abre a quantidade; o segundo Enter (quantidade 1) confirma.</summary>
    private async Task BiparAsync(string codigo)
    {
        _vm.Busca = codigo;
        _vm.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa is EtapaPdv.Quantidade or EtapaPdv.Tamanho, $"bipar {codigo}");
        if (_vm.Etapa == EtapaPdv.Tamanho)   // código do produto inteiro (sem número de tamanho): o operador confirma o tamanho
        {
            _vm.ProcessarTecla(ComandoTecla.Enter);
            await AteAsync(() => _vm.Etapa == EtapaPdv.Quantidade, $"tamanho de {codigo}");
        }
        _vm.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Busca, $"confirmar {codigo}");
    }

    // ===================== (a) ida e volta: gerar → ler =====================

    [Theory]
    [InlineData("1435")]
    [InlineData("14355")]
    [InlineData("5555")]
    [InlineData("99")]
    public async Task Cada_tamanho_gerado_pelo_BarTender_abre_a_referencia_e_o_tamanho_certos(string referencia)
    {
        // Outra referência na loja, para provar que o código não cai no produto errado.
        await CriarAsync("Outra", null, "7777", "P", "M");
        var produto = await CriarAsync("Camiseta", null, referencia, DozeTamanhos);
        var carregado = await ComGradeAsync(produto.Id);
        var config = new ConfiguracaoEtiqueta { MostrarCodigoBarras = true };

        foreach (var tamanho in DozeTamanhos)
        {
            var ordem = Array.IndexOf(DozeTamanhos, tamanho) + 1;
            var esperado = referencia + ordem;                              // referência + número do tamanho (1 a 2 dígitos)

            var codigoFila = new ItemFila(carregado, 1, tamanho).CodigoBarras;
            var codigoDesenho = CodigoBarrasTamanho.Resolver(carregado, tamanho);
            Assert.Equal(esperado, codigoFila);
            Assert.Equal(esperado, codigoDesenho);
            Assert.Contains(esperado, DesenhoEtiquetaService.GerarZpl(carregado, config, 1, tamanho));

            var lido = await _produtos.ObterPorCodigoDeTamanhoAsync(esperado);
            Assert.NotNull(lido);
            Assert.Equal(produto.Id, lido!.Value.Produto.Id);
            Assert.Equal(tamanho, lido.Value.Tamanho.Tamanho);
        }
    }

    [Fact]
    public async Task Etiquetas_usa_o_mesmo_codigo_do_BarTender_para_cada_tamanho_e_para_sem_tamanho()
    {
        var produto = await CriarAsync("Camisa", "12345", "1435", "P", "M");
        var carregado = await ComGradeAsync(produto.Id);
        var grade = await _produtos.ListarTamanhosAsync(produto.Id);

        // o que a tela de etiquetas monta para cada tamanho é o mesmo que o BarTender imprime
        foreach (var t in grade.Where(t => !t.EhSemTamanho))
            Assert.Equal(CodigoBarrasTamanho.Resolver(carregado, t.Tamanho), t.CodigoBarras);
        // e o "sem tamanho" sai pela referência (antes caía no código de barras curto)
        Assert.Equal("1435", CodigoBarrasTamanho.Resolver(carregado, Tamanhos.SemTamanho));
        Assert.Equal("1435", CodigoBarrasTamanho.BaseDoCodigo(carregado));
    }

    [Fact]
    public async Task Ean13_identifica_o_produto_inteiro_e_nunca_recebe_numero_de_tamanho()
    {
        var produto = await CriarAsync("Camiseta EAN", "7891234567895", "1435", "P", "M");
        var carregado = await ComGradeAsync(produto.Id);

        Assert.Equal("7891234567895", new ItemFila(carregado, 1, "P").CodigoBarras);
        Assert.Equal("7891234567895", new ItemFila(carregado, 1, "M").CodigoBarras);

        // lido no PDV: não é "código de tamanho"; abre o produto e o operador escolhe o tamanho
        Assert.Null(await _produtos.ObterPorCodigoDeTamanhoAsync("7891234567895"));
        Assert.Equal(produto.Id, (await _produtos.EscolherParaVendaAsync("7891234567895"))!.Id);
    }

    [Fact]
    public async Task Desempate_codigo_identico_a_uma_referencia_vence_a_referencia_com_tamanho()
    {
        var curta = await CriarAsync("Ref 1019", null, "1019", "P", "M");          // tamanho 2 imprime 10192
        var longa = await CriarAsync("Ref 10192", null, "10192");                 // sem tamanho: imprime 10192

        Assert.Equal("10192", CodigoBarrasTamanho.Resolver(await ComGradeAsync(curta.Id), "M"));
        Assert.Equal("10192", CodigoBarrasTamanho.Resolver(await ComGradeAsync(longa.Id), Tamanhos.SemTamanho));

        Assert.Null(await _produtos.ObterPorCodigoDeTamanhoAsync("10192"));       // idêntico ao de um produto: sem tamanho
        Assert.Equal(longa.Id, (await _produtos.EscolherParaVendaAsync("10192"))!.Id);
        // o tamanho 1 da ref. 1019 (10191) continua sem ambiguidade
        Assert.Equal(curta.Id, (await _produtos.ObterPorCodigoDeTamanhoAsync("10191"))!.Value.Produto.Id);
    }

    [Fact]
    public async Task Desempate_a_base_mais_longa_ganha_e_quando_ela_nao_tem_o_tamanho_vale_a_mais_curta()
    {
        var curta = await CriarAsync("Ref 1435", null, "1435", DozeTamanhos);     // tamanho 12 → 143512
        var longa = await CriarAsync("Ref 14351", null, "14351", "P", "M");       // tamanho 2 → 143512

        var lido = await _produtos.ObterPorCodigoDeTamanhoAsync("143512");
        Assert.Equal(longa.Id, lido!.Value.Produto.Id);
        Assert.Equal("M", lido.Value.Tamanho.Tamanho);

        // 143511 (tam. 11 da 1435): a longa tem o tamanho 1 → "14351"+"1"; ela ganha
        Assert.Equal(longa.Id, (await _produtos.ObterPorCodigoDeTamanhoAsync("143511"))!.Value.Produto.Id);
        // 143510: a longa não tem tamanho 0; vale a 1435 tamanho 10
        var dez = await _produtos.ObterPorCodigoDeTamanhoAsync("143510");
        Assert.Equal(curta.Id, dez!.Value.Produto.Id);
        Assert.Equal("54", dez.Value.Tamanho.Tamanho);
    }

    [Fact]
    public async Task Numero_do_tamanho_nao_comeca_com_zero_e_tamanho_inexistente_nao_acha()
    {
        var p = await CriarAsync("Camiseta", null, "1019", "P", "M");
        Assert.Null(await _produtos.ObterPorCodigoDeTamanhoAsync("101902"));      // zero à esquerda
        Assert.Null(await _produtos.ObterPorCodigoDeTamanhoAsync("10199"));       // não existe o tamanho 9
        Assert.Equal("M", (await _produtos.ObterPorCodigoDeTamanhoAsync("10192"))!.Value.Tamanho.Tamanho);
        Assert.Equal(p.Id, (await _produtos.ObterPorCodigoDeTamanhoAsync("10191"))!.Value.Produto.Id);
    }

    // ===================== (b) carrinho do leitor =====================

    private async Task<Produto> Camiseta1435Async(int estoqueP = 5, int estoqueM = 5)
    {
        var p = await CriarAsync("Camiseta Básica", null, "1435", "P", "M");
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, estoqueP, null, "P");
        await _estoque.AjustarAsync(p.Id, TipoMovimentacao.Entrada, estoqueM, null, "M");
        return p;
    }

    [Fact]
    public async Task Bipar_tamanhos_diferentes_da_mesma_referencia_acumula_numa_entrada_com_a_grade()
    {
        await Camiseta1435Async();

        await BiparAsync("14351"); // P
        await BiparAsync("14352"); // M

        Assert.Equal(2, _vm.Itens.Count);                       // uma linha por tamanho, mas uma entrada de produto só
        Assert.True(_vm.Itens[0].MostrarCabecalho);
        Assert.False(_vm.Itens[1].MostrarCabecalho);
        Assert.Contains("Camiseta Básica", _vm.Itens[0].CabecalhoGrupo);
        Assert.Contains("ref. 1435", _vm.Itens[0].CabecalhoGrupo);
        Assert.Contains("P×1, M×1", _vm.Itens[0].CabecalhoGrupo);
        Assert.Equal(11980, _vm.Itens[0].TotalGrupoCentavos);

        await BiparAsync("14351"); // P de novo: soma +1, sem linha nova
        Assert.Equal(2, _vm.Itens.Count);
        Assert.Equal(2, _vm.Itens[0].Quantidade);
        Assert.Contains("P×2, M×1", _vm.Itens[0].CabecalhoGrupo);
        Assert.Equal(5990 * 3, _vm.Itens[0].TotalGrupoCentavos);
        Assert.Equal(Dinheiro.Formatar(5990 * 3), _vm.SubtotalTexto);
    }

    [Fact]
    public async Task Produtos_diferentes_ficam_em_grupos_separados_mesmo_bipados_intercalados()
    {
        await Camiseta1435Async();
        var bone = await CriarAsync("Boné", null, "2000");
        await _estoque.AjustarAsync(bone.Id, TipoMovimentacao.Entrada, 5, null);

        await BiparAsync("14351");
        await BiparAsync("2000");
        await BiparAsync("14352"); // volta à camiseta: entra junto das outras linhas dela, não depois do boné

        Assert.Equal(new[] { "P", "M", Tamanhos.SemTamanho }, _vm.Itens.Select(i => i.Linha.Tamanho).ToArray());
        Assert.True(_vm.Itens[0].MostrarCabecalho);
        Assert.False(_vm.Itens[2].MostrarCabecalho);   // boné sem tamanho: sem cabeçalho de grade
        Assert.Equal("Boné", _vm.Itens[2].RotuloLinha);
    }

    [Fact]
    public async Task Preco_e_subtotal_corretos_por_tamanho_e_remover_ajustar_um_tamanho_especifico()
    {
        await Camiseta1435Async();
        await BiparAsync("14351");
        await BiparAsync("14352");
        await BiparAsync("14352");

        var p = _vm.Itens.Single(i => i.Linha.Tamanho == "P");
        var m = _vm.Itens.Single(i => i.Linha.Tamanho == "M");
        Assert.Equal(5990, p.TotalCentavos);
        Assert.Equal(11980, m.TotalCentavos);

        // ajustar só o M para 3
        _vm.ProcessarTecla(ComandoTecla.Esc);
        m.Quantidade = 3;
        await AteAsync(() => m.Linha.Quantidade == 3 && _vm.SubtotalTexto == Dinheiro.Formatar(5990 * 4), "quantidade do M");
        Assert.Equal(1, p.Linha.Quantidade);

        // remover só o P: o M continua com a grade dele
        _vm.ItemSelecionado = p;
        _vm.RemoverSelecionadoCommand.Execute(null);
        await AteAsync(() => _vm.Itens.Count == 1, "remover P");
        Assert.True(_vm.Itens[0].Linha.Tamanho == "M");
        Assert.Equal(3, _vm.Itens[0].Quantidade);
        Assert.Equal(Dinheiro.Formatar(5990 * 3), _vm.SubtotalTexto);
    }

    [Fact]
    public async Task Estoque_por_tamanho_e_respeitado_e_avisa_quando_falta()
    {
        await Camiseta1435Async(estoqueP: 5, estoqueM: 1);
        await BiparAsync("14352"); // o único M

        // segundo M: esgotado, avisa e não cria nem soma
        _vm.Busca = "14352";
        _vm.ProcessarTecla(ComandoTecla.Enter);
        await AteAsync(() => _vm.Etapa == EtapaPdv.Tamanho && _vm.Mensagem.Length > 0, "aviso de M esgotado");
        Assert.Contains("esgotado", _vm.Mensagem, StringComparison.OrdinalIgnoreCase);
        Assert.Single(_vm.Itens);
        Assert.Equal(1, _vm.Itens[0].Quantidade);
        _vm.ProcessarTecla(ComandoTecla.Esc);

        // o P continua disponível
        await BiparAsync("14351");
        Assert.Equal(2, _vm.Itens.Count);

        // e na grade, subir a quantidade além do estoque é recusado com aviso
        var p = _vm.Itens.Single(i => i.Linha.Tamanho == "P");
        p.Quantidade = 9;
        await AteAsync(() => _vm.Mensagem.Contains("Estoque insuficiente"), "aviso de estoque");
        Assert.Equal(1, p.Linha.Quantidade);
    }

    [Fact]
    public async Task Venda_gravada_tem_uma_linha_por_tamanho_com_o_total_certo()
    {
        var produto = await Camiseta1435Async();
        await BiparAsync("14351");
        await BiparAsync("14352");

        var pedido = new Carrinho();
        var grade = await _produtos.ListarTamanhosAsync(produto.Id);
        pedido.Adicionar(produto, grade.Single(t => t.Tamanho == "P"), 1);
        pedido.Adicionar(produto, grade.Single(t => t.Tamanho == "M"), 1);
        var grupos = pedido.Grupos();
        Assert.Single(grupos);
        Assert.Equal("P×1, M×1", grupos[0].Grade);
        Assert.Equal(11980, grupos[0].TotalCentavos);
        Assert.Equal(2, pedido.ParaPedido().Count);               // gravação continua uma linha por tamanho (sem mudar o schema)
    }
}
