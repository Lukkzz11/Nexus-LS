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
}
