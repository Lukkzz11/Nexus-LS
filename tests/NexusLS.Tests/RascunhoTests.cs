using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class RascunhoStoreTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-teste-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_pasta, "rascunho.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    [Fact]
    public void Salva_e_carrega_o_mesmo_conteudo()
    {
        var store = new RascunhoStore(Arquivo);
        store.Salvar(new Rascunho
        {
            Tabela = TabelaPreco.Atacado,
            DescontoTexto = "10",
            ClienteId = 7,
            Itens = { new ItemRascunho { ProdutoId = 3, Tamanho = "M", Quantidade = 2, TotalEditadoCentavos = 5000 } }
        });

        var lido = store.Carregar();

        Assert.NotNull(lido);
        Assert.Equal(TabelaPreco.Atacado, lido!.Tabela);
        Assert.Equal("10", lido.DescontoTexto);
        Assert.Equal(7, lido.ClienteId);
        var item = Assert.Single(lido.Itens);
        Assert.Equal((3, "M", 2, 5000L), (item.ProdutoId, item.Tamanho, item.Quantidade, item.TotalEditadoCentavos));
    }

    [Fact]
    public void Sem_arquivo_nao_ha_rascunho()
    {
        Assert.Null(new RascunhoStore(Arquivo).Carregar());
    }

    [Fact]
    public void Arquivo_corrompido_vira_sem_rascunho_e_e_guardado_de_lado()
    {
        Directory.CreateDirectory(_pasta);
        File.WriteAllText(Arquivo, "{ isto nao e json");
        var store = new RascunhoStore(Arquivo);

        Assert.Null(store.Carregar());

        Assert.False(File.Exists(Arquivo));
        Assert.True(File.Exists(Arquivo + ".corrompido"));
    }

    [Fact]
    public void Descartar_apaga_o_arquivo_e_regravar_nao_deixa_temporario()
    {
        var store = new RascunhoStore(Arquivo);
        store.Salvar(new Rascunho());
        store.Salvar(new Rascunho()); // sobrescreve
        Assert.False(File.Exists(Arquivo + ".tmp"));

        store.Descartar();
        store.Descartar(); // descartar sem arquivo não dá erro

        Assert.False(File.Exists(Arquivo));
    }
}

public class RascunhoServiceTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-teste-" + Guid.NewGuid().ToString("N"));
    private string Arquivo => Path.Combine(_pasta, "rascunho.json");

    public void Dispose()
    {
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public ProdutoService Produtos { get; }
        public EstoqueService Estoque { get; }
        public ClienteService Clientes { get; }
        public RascunhoService Rascunho { get; }
        public int CamisetaId { get; private set; }
        public int BoneId { get; private set; }

        public Cenario(string arquivo)
        {
            Produtos = new ProdutoService(Banco.Fabrica);
            Estoque = new EstoqueService(Banco.Fabrica);
            Clientes = new ClienteService(Banco.Fabrica);
            Rascunho = new RascunhoService(new RascunhoStore(arquivo), Banco.Fabrica);
        }

        public static async Task<Cenario> CriarAsync(string arquivo)
        {
            var c = new Cenario(arquivo);
            c.CamisetaId = (await c.Produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1, PrecoAtacadoCentavos: 3500))).Id;
            await c.Produtos.AdicionarTamanhoAsync(c.CamisetaId, "M");
            await c.Estoque.AjustarAsync(c.CamisetaId, TipoMovimentacao.Entrada, 5, null, "M");
            c.BoneId = (await c.Produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 10)).Id;
            return c;
        }

        /// <summary>Monta um carrinho "como o PDV faria": produto e grade lidos do banco.</summary>
        public async Task<Carrinho> MontarAsync(params (int produtoId, string tamanho, int qtd)[] itens)
        {
            var carrinho = new Carrinho();
            foreach (var (produtoId, tamanho, qtd) in itens)
            {
                var produto = (await Produtos.BuscarAsync(null, incluirInativos: true)).Single(p => p.Id == produtoId);
                var grade = await Produtos.ListarTamanhosAsync(produtoId);
                carrinho.Adicionar(produto, grade.Single(t => t.Tamanho == tamanho), qtd);
            }
            return carrinho;
        }

        public void Dispose() => Banco.Dispose();
    }

    [Fact]
    public async Task Salva_e_restaura_itens_precos_editados_tabela_desconto_e_cliente()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        var maria = await c.Clientes.CadastrarAsync(new DadosCliente("Maria"));
        var carrinho = await c.MontarAsync((c.CamisetaId, "M", 2), (c.BoneId, Tamanhos.SemTamanho, 3));
        carrinho.MudarTabela(TabelaPreco.Atacado);
        carrinho.DefinirPrecoUnitario(carrinho.Linhas[0], 3000);
        carrinho.DefinirTotalLinha(carrinho.Linhas[1], 5000);

        c.Rascunho.Salvar(carrinho, "10", maria.Id);
        var restaurado = await c.Rascunho.RestaurarAsync();

        Assert.NotNull(restaurado);
        Assert.Empty(restaurado!.Avisos);
        Assert.Equal("10", restaurado.DescontoTexto);
        Assert.Equal(maria.Id, restaurado.ClienteId);
        var r = restaurado.Carrinho;
        Assert.Equal(TabelaPreco.Atacado, r.Tabela);
        Assert.Equal(2, r.Linhas.Count);
        Assert.Equal(("Camiseta", "M", 2, 3000L), (r.Linhas[0].Nome, r.Linhas[0].Tamanho, r.Linhas[0].Quantidade, r.Linhas[0].PrecoUnitarioCentavos));
        Assert.Equal(5000, r.Linhas[1].TotalCentavos);
        Assert.Equal(carrinho.SubtotalCentavos, r.SubtotalCentavos);
    }

    [Fact]
    public async Task Carrinho_vazio_apaga_o_rascunho()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        c.Rascunho.Salvar(await c.MontarAsync((c.BoneId, Tamanhos.SemTamanho, 1)), null, null);
        Assert.True(File.Exists(Arquivo));

        c.Rascunho.Salvar(new Carrinho(), null, null);

        Assert.False(File.Exists(Arquivo));
        Assert.Null(await c.Rascunho.RestaurarAsync());
    }

    [Fact]
    public async Task Sem_rascunho_nao_restaura_nada()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        Assert.Null(await c.Rascunho.RestaurarAsync());
    }

    [Fact]
    public async Task Produto_desativado_sai_do_carrinho_com_aviso_e_o_resto_fica()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        c.Rascunho.Salvar(await c.MontarAsync((c.CamisetaId, "M", 1), (c.BoneId, Tamanhos.SemTamanho, 1)), null, null);
        await c.Produtos.DesativarAsync(c.BoneId);

        var restaurado = await c.Rascunho.RestaurarAsync();

        var linha = Assert.Single(restaurado!.Carrinho.Linhas);
        Assert.Equal("Camiseta", linha.Nome);
        Assert.Contains(restaurado.Avisos, a => a.Contains("Boné") && a.Contains("desativado"));
    }

    [Fact]
    public async Task Estoque_que_diminuiu_reduz_a_quantidade_e_avisa()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        c.Rascunho.Salvar(await c.MontarAsync((c.CamisetaId, "M", 5)), null, null);
        await c.Estoque.AjustarAsync(c.CamisetaId, TipoMovimentacao.Perda, 3, "Vendido em outro lugar", "M"); // sobraram 2

        var restaurado = await c.Rascunho.RestaurarAsync();

        Assert.Equal(2, restaurado!.Carrinho.Linhas.Single().Quantidade);
        Assert.Contains(restaurado.Avisos, a => a.Contains("reduzida de 5 para 2"));
    }

    [Fact]
    public async Task Sem_estoque_ou_tamanho_removido_tira_a_linha_e_se_nada_sobra_nao_ha_rascunho()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        c.Rascunho.Salvar(await c.MontarAsync((c.CamisetaId, "M", 2)), null, null);
        await c.Estoque.AjustarAsync(c.CamisetaId, TipoMovimentacao.Perda, 5, "Zerou", "M");

        Assert.Null(await c.Rascunho.RestaurarAsync()); // nada pôde ser restaurado
        Assert.False(File.Exists(Arquivo));             // e o arquivo inútil foi apagado
    }

    [Fact]
    public async Task Cliente_desativado_e_removido_da_venda_com_aviso()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        var pedro = await c.Clientes.CadastrarAsync(new DadosCliente("Pedro"));
        c.Rascunho.Salvar(await c.MontarAsync((c.BoneId, Tamanhos.SemTamanho, 1)), null, pedro.Id);
        await c.Clientes.DesativarAsync(pedro.Id);

        var restaurado = await c.Rascunho.RestaurarAsync();

        Assert.Null(restaurado!.ClienteId);
        Assert.Contains(restaurado.Avisos, a => a.Contains("cliente"));
    }

    [Fact]
    public async Task Preco_de_tabela_e_atualizado_na_restauracao_mas_o_editado_permanece()
    {
        using var c = await Cenario.CriarAsync(Arquivo);
        var carrinho = await c.MontarAsync((c.BoneId, Tamanhos.SemTamanho, 1), (c.CamisetaId, "M", 1));
        carrinho.DefinirPrecoUnitario(carrinho.Linhas[1], 4200);
        c.Rascunho.Salvar(carrinho, null, null);
        await c.Produtos.EditarAsync(c.BoneId, new DadosProduto("Boné", null, 2500, 800, 1)); // preço subiu
        await c.Produtos.EditarAsync(c.CamisetaId, new DadosProduto("Camiseta", null, 6000, 2000, 1));

        var restaurado = await c.Rascunho.RestaurarAsync();

        Assert.Equal(2500, restaurado!.Carrinho.Linhas[0].PrecoUnitarioCentavos); // tabela nova
        Assert.Equal(4200, restaurado.Carrinho.Linhas[1].PrecoUnitarioCentavos);  // o que o operador digitou
    }
}
