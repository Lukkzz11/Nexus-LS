using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class MaisVendidosTests
{
    private static readonly DateTime D1 = new(2026, 10, 10, 10, 0, 0);

    private static ItemCarrinho I(int produtoId, int quantidade, string? tamanho = null, long? PrecoUnitarioCentavos = null) =>
        new(produtoId, quantidade, tamanho, PrecoUnitarioCentavos);

    private sealed class Cenario : IDisposable
    {
        public BancoDeTeste Banco { get; } = new();
        public DateTime Agora { get; set; } = D1;
        public VendaService Vendas { get; }
        public RelatorioService Relatorio { get; }
        public int CamisetaId { get; private set; }
        public int BoneId { get; private set; }
        public int CintoId { get; private set; }

        public Cenario()
        {
            Vendas = new VendaService(Banco.Fabrica, () => Agora);
            Relatorio = new RelatorioService(Banco.Fabrica);
        }

        public static async Task<Cenario> CriarAsync()
        {
            var c = new Cenario();
            var produtos = new ProdutoService(c.Banco.Fabrica);
            var estoque = new EstoqueService(c.Banco.Fabrica);
            c.CamisetaId = (await produtos.CadastrarAsync(new DadosProduto("Camiseta", null, 5000, 2000, 1))).Id;
            foreach (var t in new[] { "P", "M" })
            {
                await produtos.AdicionarTamanhoAsync(c.CamisetaId, t);
                await estoque.AjustarAsync(c.CamisetaId, TipoMovimentacao.Entrada, 100, null, t);
            }
            c.BoneId = (await produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 1), 100)).Id;
            c.CintoId = (await produtos.CadastrarAsync(new DadosProduto("Cinto", null, 9000, 3000, 1), 100)).Id;
            return c;
        }

        public Task<Venda> VenderAsync(params ItemCarrinho[] itens) =>
            Vendas.FinalizarAsync(new PedidoVenda(itens, 0, FormaPagamento.Pix));

        public void Dispose() => Banco.Dispose();
    }

    [Fact]
    public async Task Ordena_pela_quantidade_e_soma_todos_os_tamanhos_do_produto()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(I(c.CamisetaId, 2, "P"), I(c.BoneId, 5));
        await c.VenderAsync(I(c.CamisetaId, 4, "M"), I(c.CintoId, 1));

        var ranking = await c.Relatorio.MaisVendidosAsync(D1, D1);

        // Camiseta: 2 (P) + 4 (M) = 6; Boné: 5; Cinto: 1
        Assert.Equal(new[] { "Camiseta", "Boné", "Cinto" }, ranking.Select(r => r.Nome));
        Assert.Equal(6, ranking[0].Quantidade);
        Assert.Equal(new[] { 1, 2, 3 }, ranking.Select(r => r.Posicao));
    }

    [Fact]
    public async Task Soma_quantidade_faturamento_e_numero_de_vendas()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(I(c.BoneId, 3));
        await c.VenderAsync(I(c.BoneId, 2, PrecoUnitarioCentavos: 1500)); // preço editado: R$ 30,00

        var bone = Assert.Single(await c.Relatorio.MaisVendidosAsync(D1, D1));

        Assert.Equal((5, 6000L + 3000L, 2), (bone.Quantidade, bone.FaturamentoCentavos, bone.NumeroDeVendas));
    }

    [Fact]
    public async Task Vendas_canceladas_nao_entram_no_ranking()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(I(c.BoneId, 2));
        var cancelada = await c.VenderAsync(I(c.CintoId, 9));
        await c.Vendas.CancelarAsync(cancelada.Id);

        var ranking = await c.Relatorio.MaisVendidosAsync(D1, D1);

        Assert.Equal("Boné", Assert.Single(ranking).Nome);
    }

    [Fact]
    public async Task Periodo_inclui_o_primeiro_e_o_ultimo_dia_inteiros()
    {
        using var c = await Cenario.CriarAsync();
        c.Agora = new DateTime(2026, 10, 9, 23, 59, 0);  await c.VenderAsync(I(c.BoneId, 1));   // antes
        c.Agora = new DateTime(2026, 10, 10, 0, 0, 0);   await c.VenderAsync(I(c.BoneId, 1));   // primeiro dia, meia-noite
        c.Agora = new DateTime(2026, 10, 12, 23, 59, 0); await c.VenderAsync(I(c.BoneId, 1));   // último dia, fim do dia
        c.Agora = new DateTime(2026, 10, 13, 0, 0, 0);   await c.VenderAsync(I(c.BoneId, 1));   // depois

        var ranking = await c.Relatorio.MaisVendidosAsync(new DateTime(2026, 10, 10, 15, 0, 0), new DateTime(2026, 10, 12, 8, 0, 0));

        Assert.Equal(2, Assert.Single(ranking).Quantidade);
    }

    [Fact]
    public async Task Separar_por_tamanho_gera_uma_linha_para_cada_tamanho()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(I(c.CamisetaId, 2, "P"), I(c.CamisetaId, 5, "M"));

        var junto = await c.Relatorio.MaisVendidosAsync(D1, D1);
        var porTamanho = await c.Relatorio.MaisVendidosAsync(D1, D1, porTamanho: true);

        Assert.Equal(7, Assert.Single(junto).Quantidade);
        Assert.Equal(new[] { ("M", 5), ("P", 2) }, porTamanho.Select(r => (r.Tamanho!, r.Quantidade)));
    }

    [Fact]
    public async Task Limite_corta_o_ranking_e_empate_desempata_pelo_faturamento()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(I(c.BoneId, 2), I(c.CintoId, 2), I(c.CamisetaId, 1, "P")); // Bone e Cinto empatam em 2

        var ranking = await c.Relatorio.MaisVendidosAsync(D1, D1, limite: 2);

        Assert.Equal(new[] { "Cinto", "Boné" }, ranking.Select(r => r.Nome)); // o Cinto fatura mais
    }

    [Fact]
    public async Task Sem_vendas_no_periodo_o_ranking_vem_vazio_e_data_invertida_e_recusada()
    {
        using var c = await Cenario.CriarAsync();

        Assert.Empty(await c.Relatorio.MaisVendidosAsync(D1, D1));
        await Assert.ThrowsAsync<RegraDeNegocioException>(() => c.Relatorio.MaisVendidosAsync(D1.AddDays(2), D1));
    }

    [Fact]
    public async Task Usa_o_nome_mais_recente_do_produto()
    {
        using var c = await Cenario.CriarAsync();
        await c.VenderAsync(I(c.BoneId, 1));
        await new ProdutoService(c.Banco.Fabrica).EditarAsync(c.BoneId, new DadosProduto("Boné Novo", null, 2000, 800, 1));
        await c.VenderAsync(I(c.BoneId, 1));

        Assert.Equal("Boné Novo", Assert.Single(await c.Relatorio.MaisVendidosAsync(D1, D1)).Nome);
    }

    [Fact]
    public void Resumo_por_forma_soma_so_as_vendas_validas_na_ordem_das_formas()
    {
        var vendas = new[]
        {
            new Venda { FormaPagamento = FormaPagamento.Pix, TotalCentavos = 1000 },
            new Venda { FormaPagamento = FormaPagamento.Dinheiro, TotalCentavos = 2500 },
            new Venda { FormaPagamento = FormaPagamento.Pix, TotalCentavos = 500 },
            new Venda { FormaPagamento = FormaPagamento.Dinheiro, TotalCentavos = 9900, Cancelada = true },
            new Venda { FormaPagamento = FormaPagamento.Cheque, TotalCentavos = 300, Cancelada = true }
        };

        var resumo = RelatorioService.ResumoPorForma(vendas);

        Assert.Equal(
            new[] { (FormaPagamento.Dinheiro, 1, 2500L), (FormaPagamento.Pix, 2, 1500L) },
            resumo.Select(r => (r.Forma, r.Vendas, r.TotalCentavos)));
    }
}

public class ExportadorCsvTests
{
    private static Venda Venda(int id, Action<Venda>? ajustar = null)
    {
        var v = new Venda
        {
            Id = id, DataHora = new DateTime(2026, 10, 18, 14, 5, 0),
            FormaPagamento = FormaPagamento.Dinheiro, SubtotalCentavos = 125050, DescontoCentavos = 5050, TotalCentavos = 120000,
            ValorRecebidoCentavos = 150000, TrocoCentavos = 30000,
            Itens = { new ItemVenda { NomeProduto = "Camiseta", Tamanho = "M", PrecoUnitarioCentavos = 62525, Quantidade = 2, TotalCentavos = 125050 } }
        };
        ajustar?.Invoke(v);
        return v;
    }

    private static string[] Linhas(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void Vendas_tem_cabecalho_uma_linha_por_venda_e_numeros_no_formato_brasileiro()
    {
        var csv = ExportadorCsv.Vendas(new[] { Venda(7) });

        var linhas = Linhas(csv);
        Assert.Equal("Nº;Data;Cliente;Tabela;Pagamento;Parcelas;Subtotal;Desconto;Total;Recebido;Troco;Situação", linhas[0]);
        Assert.Equal("7;18/10/2026 14:05;;Varejo;Dinheiro;1;1250,50;50,50;1200,00;1500,00;300,00;Concluída", linhas[1]);
    }

    [Fact]
    public void Venda_cancelada_cliente_atacado_e_credito_aparecem_certos()
    {
        var csv = ExportadorCsv.Vendas(new[]
        {
            Venda(1, v => { v.Cancelada = true; v.TabelaPreco = TabelaPreco.Atacado; v.Cliente = new Cliente { Nome = "Maria" };
                            v.FormaPagamento = FormaPagamento.CartaoCredito; v.Parcelas = 3; v.ValorRecebidoCentavos = null; v.TrocoCentavos = 0; })
        });

        var linha = Linhas(csv)[1];
        Assert.Equal("1;18/10/2026 14:05;Maria;Atacado;Cartão crédito;3;1250,50;50,50;1200,00;;;Cancelada", linha);
    }

    [Fact]
    public void Ordena_por_data_e_numero()
    {
        var antiga = Venda(2, v => v.DataHora = new DateTime(2026, 10, 1, 9, 0, 0));
        var csv = ExportadorCsv.Vendas(new[] { Venda(1), antiga });

        var linhas = Linhas(csv);
        Assert.StartsWith("2;01/10/2026", linhas[1]);
        Assert.StartsWith("1;18/10/2026", linhas[2]);
    }

    [Fact]
    public void Itens_tem_uma_linha_por_item_e_sem_tamanho_fica_em_branco()
    {
        var venda = Venda(5, v => v.Itens.Add(new ItemVenda
        {
            NomeProduto = "Boné", Tamanho = Tamanhos.SemTamanho, PrecoUnitarioCentavos = 2000, Quantidade = 1, TotalCentavos = 2000
        }));

        var linhas = Linhas(ExportadorCsv.Itens(new[] { venda }));

        Assert.Equal("Venda;Data;Produto;Tamanho;Quantidade;Unitário;Total;Situação", linhas[0]);
        Assert.Equal("5;18/10/2026 14:05;Camiseta;M;2;625,25;1250,50;Concluída", linhas[1]);
        Assert.Equal("5;18/10/2026 14:05;Boné;;1;20,00;20,00;Concluída", linhas[2]);
    }

    [Fact]
    public void Texto_com_separador_aspas_ou_quebra_de_linha_ganha_aspas()
    {
        var venda = Venda(1, v => v.Itens[0].NomeProduto = "Camiseta; \"Premium\"\nAzul");

        var csv = ExportadorCsv.Itens(new[] { venda });

        Assert.Contains("\"Camiseta; \"\"Premium\"\"\nAzul\"", csv);
    }

    [Theory]
    [InlineData("=SOMA(A1:A9)")]
    [InlineData("+cmd")]
    [InlineData("@usuario")]
    [InlineData("-cmd")]
    public void Texto_que_parece_formula_ganha_apostrofo(string nome)
    {
        var venda = Venda(1, v => v.Itens[0].NomeProduto = nome);
        Assert.Contains(";'" + nome + ";", ExportadorCsv.Itens(new[] { venda }));
    }

    [Fact]
    public void Mais_vendidos_com_e_sem_coluna_de_tamanho()
    {
        var ranking = new[]
        {
            new ProdutoMaisVendido(1, 10, "Camiseta", "M", 5, 31250, 3),
            new ProdutoMaisVendido(2, 11, "Boné", Tamanhos.SemTamanho, 2, 4000, 1)
        };

        var com = Linhas(ExportadorCsv.MaisVendidos(ranking, porTamanho: true));
        Assert.Equal("Posição;Produto;Tamanho;Quantidade;Faturamento;Vendas", com[0]);
        Assert.Equal("1;Camiseta;M;5;312,50;3", com[1]);
        Assert.Equal("2;Boné;;2;40,00;1", com[2]);

        var sem = Linhas(ExportadorCsv.MaisVendidos(ranking, porTamanho: false));
        Assert.Equal("Posição;Produto;Quantidade;Faturamento;Vendas", sem[0]);
        Assert.Equal("1;Camiseta;5;312,50;3", sem[1]);
    }

    [Fact]
    public void Salvar_grava_utf8_com_bom_para_o_excel_ler_os_acentos()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "nexusls-csv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        try
        {
            var arquivo = Path.Combine(pasta, "teste.csv");

            ExportadorCsv.Salvar(arquivo, ExportadorCsv.Vendas(new[] { Venda(1) }));

            var bytes = File.ReadAllBytes(arquivo);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            Assert.Contains("Concluída", Encoding.UTF8.GetString(bytes));
        }
        finally
        {
            Directory.Delete(pasta, recursive: true);
        }
    }

    [Theory]
    [InlineData(0, "0,00")]
    [InlineData(5, "0,05")]
    [InlineData(123456, "1234,56")]
    [InlineData(100000000, "1000000,00")]
    public void Formato_simples_nao_tem_ponto_de_milhar(long centavos, string esperado) =>
        Assert.Equal(esperado, Dinheiro.FormatarSimples(centavos));
}
