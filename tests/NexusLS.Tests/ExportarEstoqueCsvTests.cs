using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

/// <summary>Exportar o estoque da loja em CSV (uma linha por tamanho, formato do Excel brasileiro).</summary>
public class ExportarEstoqueCsvTests
{
    private static string[] Linhas(string csv) => csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static Produto Camiseta() => new()
    {
        Id = 1, Nome = "Camiseta Básica", Referencia = "CB-01", Categoria = "Camisetas", CodigoBarras = "7891234567895",
        CodigoInterno = "INT-000001", PrecoVendaCentavos = 5990, PrecoAtacadoCentavos = 4500, PrecoCustoCentavos = 2500,
        EstoqueAtual = 7, EstoqueMinimo = 3,
        Tamanhos = { new ProdutoTamanho { Id = 1, Tamanho = "P", Estoque = 2 }, new ProdutoTamanho { Id = 2, Tamanho = "M", Estoque = 5 } }
    };

    [Fact]
    public void Cabecalho_e_uma_linha_por_tamanho_com_valores_no_formato_brasileiro()
    {
        var linhas = Linhas(ExportadorCsv.Estoque(new[] { Camiseta() }));

        Assert.Equal("Produto;Referência;Categoria;Código de barras;Código interno;Tamanho;Estoque;Estoque mínimo;Preço de varejo;Preço de atacado;Preço de custo;Situação", linhas[0]);
        Assert.Equal(3, linhas.Length);
        Assert.Equal("Camiseta Básica;CB-01;Camisetas;7891234567895;INT-000001;P;2;3;59,90;45,00;25,00;Normal", linhas[1]);
        Assert.Equal("Camiseta Básica;CB-01;Camisetas;7891234567895;INT-000001;M;5;3;59,90;45,00;25,00;Normal", linhas[2]);
    }

    [Fact]
    public void Produto_sem_grade_sai_numa_linha_com_o_estoque_total_e_sem_tamanho_nem_atacado()
    {
        var p = new Produto { Id = 2, Nome = "Meia", PrecoVendaCentavos = 1000, EstoqueAtual = 4, EstoqueMinimo = 1 };
        p.Tamanhos.Add(new ProdutoTamanho { Id = 9, Tamanho = Tamanhos.SemTamanho, Estoque = 4 });

        var linhas = Linhas(ExportadorCsv.Estoque(new[] { p }));

        Assert.Equal("Meia;;;;;;4;1;10,00;;0,00;Normal", linhas[1]);
    }

    [Fact]
    public void Situacao_marca_desativado_e_abaixo_do_minimo()
    {
        var baixo = new Produto { Id = 1, Nome = "Boné", EstoqueAtual = 1, EstoqueMinimo = 5 };
        var inativo = new Produto { Id = 2, Nome = "Cinto", Ativo = false, EstoqueAtual = 9 };

        var linhas = Linhas(ExportadorCsv.Estoque(new[] { baixo, inativo }));

        Assert.EndsWith("Abaixo do mínimo", linhas[1]);
        Assert.EndsWith("Desativado", linhas[2]);
    }

    [Fact]
    public void Nome_com_ponto_e_virgula_ou_aspas_fica_entre_aspas_e_ordena_por_nome()
    {
        var a = new Produto { Id = 1, Nome = "Zebra; \"top\"" };
        var b = new Produto { Id = 2, Nome = "Árvore" };

        var linhas = Linhas(ExportadorCsv.Estoque(new[] { a, b }));

        Assert.StartsWith("Árvore;", linhas[1]);
        Assert.StartsWith("\"Zebra; \"\"top\"\"\";", linhas[2]);
    }

    [Fact]
    public async Task Servico_lista_ativos_e_inativos_com_a_grade_e_o_arquivo_sai_em_utf8_com_bom()
    {
        using var banco = new BancoDeTeste();
        var servico = new ProdutoService(banco.Fabrica);
        var p = await servico.CadastrarAsync(new DadosProduto("Calça", null, 9990, 4000, 2), estoqueInicial: 0);
        await servico.AdicionarTamanhoAsync(p.Id, "38");
        var inativo = await servico.CadastrarAsync(new DadosProduto("Velho", null, 100, 50, 0), 0);
        await servico.DesativarAsync(inativo.Id);

        var todos = await servico.ListarComTamanhosAsync();
        var soAtivos = await servico.ListarComTamanhosAsync(incluirInativos: false);

        Assert.Equal(2, todos.Count);
        Assert.Single(soAtivos);
        Assert.Contains(todos.Single(x => x.Nome == "Calça").Tamanhos, t => t.Tamanho == "38");

        var caminho = Path.Combine(Path.GetTempPath(), "estoque-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            ExportadorCsv.Salvar(caminho, ExportadorCsv.Estoque(todos));
            var bytes = File.ReadAllBytes(caminho);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
            Assert.Contains("Calça", Encoding.UTF8.GetString(bytes));
        }
        finally { File.Delete(caminho); }
    }
}
