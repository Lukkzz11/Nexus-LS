using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public sealed class ImportadorProdutosTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly ProdutoService _produtos;
    private readonly ImportadorProdutos _importador;

    public ImportadorProdutosTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _importador = new ImportadorProdutos(_produtos, new EstoqueService(_banco.Fabrica));
    }

    public void Dispose() => _banco.Dispose();

    private static string Csv(params string[] linhas) => string.Join("\r\n", linhas);

    [Fact]
    public void Entende_ponto_e_virgula_com_acentos_e_virgula_decimal()
    {
        var a = ImportadorProdutos.Analisar(Csv(
            "Nome;Código de barras;Referência;Categoria;Preço de venda;Preço de custo;Estoque;Estoque mínimo",
            "Camiseta Básica;789;CB1;Camisetas;59,90;25,00;12;3"));

        var p = Assert.Single(a.Produtos);
        Assert.Equal("Camiseta Básica", p.Dados.Nome);
        Assert.Equal("789", p.Dados.CodigoBarras);
        Assert.Equal(5990, p.Dados.PrecoVendaCentavos);
        Assert.Equal(2500, p.Dados.PrecoCustoCentavos);
        Assert.Equal(12, p.EstoqueSemTamanho);
        Assert.Equal(3, p.Dados.EstoqueMinimo);
        Assert.Empty(a.Problemas);
    }

    [Fact]
    public void Entende_virgula_como_separador_aspas_e_titulos_alternativos()
    {
        var a = ImportadorProdutos.Analisar(Csv(
            "Produto,EAN,Preco,Qtd",
            "\"Boné, aba reta\",555,\"40,00\",4"));

        var p = Assert.Single(a.Produtos);
        Assert.Equal("Boné, aba reta", p.Dados.Nome);
        Assert.Equal(4000, p.Dados.PrecoVendaCentavos);
        Assert.Equal(4, p.EstoqueSemTamanho);
    }

    [Fact]
    public void Linhas_do_mesmo_produto_viram_tamanhos()
    {
        var a = ImportadorProdutos.Analisar(Csv(
            "Nome;Preço;Tamanho;Estoque",
            "Camiseta;50;P;3",
            "Camiseta;50;M;5",
            "Camiseta;50;G;0"));

        var p = Assert.Single(a.Produtos);
        Assert.Equal(new[] { ("P", 3), ("M", 5), ("G", 0) }, p.Tamanhos);
        Assert.Equal(0, p.EstoqueSemTamanho);
    }

    [Fact]
    public void Linha_ruim_vai_para_os_problemas_e_nao_derruba_as_outras()
    {
        var a = ImportadorProdutos.Analisar(Csv(
            "Nome;Preço;Estoque",
            "Bom;10,00;1",
            ";10;1",
            "Sem preço;;1",
            "Estoque ruim;10;abc",
            "Negativo;10;-2",
            "",
            "Outro bom;5;0"));

        Assert.Equal(new[] { "Bom", "Outro bom" }, a.Produtos.Select(p => p.Dados.Nome));
        Assert.Equal(new[] { 3, 4, 5, 6 }, a.Problemas.Select(p => p.Linha));
    }

    [Fact]
    public void Linha_repetida_do_mesmo_tamanho_e_ignorada_e_avisada()
    {
        var a = ImportadorProdutos.Analisar(Csv("Nome;Preço;Tamanho;Estoque", "Camiseta;50;M;5", "Camiseta;50;M;9"));

        Assert.Equal(5, Assert.Single(Assert.Single(a.Produtos).Tamanhos).Estoque);
        Assert.Contains("repetida", Assert.Single(a.Problemas).Motivo);
    }

    [Fact]
    public void Arquivo_sem_coluna_de_nome_ou_preco_explica_o_que_falta()
    {
        Assert.Contains("nome", ImportadorProdutos.Analisar("Preço;Estoque\r\n10;1").ErroDoArquivo!);
        Assert.Contains("preço", ImportadorProdutos.Analisar("Nome;Estoque\r\nX;1").ErroDoArquivo!);
        Assert.Contains("vazio", ImportadorProdutos.Analisar("").ErroDoArquivo!);
        Assert.False(ImportadorProdutos.Analisar("Nome;Preço").PodeImportar); // só o cabeçalho
    }

    [Fact]
    public void Decodifica_utf8_com_bom_e_windows_1252()
    {
        var utf8 = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("Descrição")).ToArray();
        var latin = Encoding.Latin1.GetBytes("Descrição");

        Assert.Equal("Descrição", ImportadorProdutos.Decodificar(utf8));
        Assert.Equal("Descrição", ImportadorProdutos.Decodificar(latin));
    }

    [Fact]
    public async Task Importar_grava_produtos_tamanhos_e_estoque_com_movimentacao()
    {
        var a = ImportadorProdutos.Analisar(Csv(
            "Nome;Código;Preço;Custo;Estoque;Tamanho",
            "Boné;555;20,00;8,00;10;",
            "Camiseta;789;50,00;20,00;3;P",
            "Camiseta;789;50,00;20,00;5;M"));

        var r = await _importador.ImportarAsync(a);

        Assert.Equal(2, r.Importados);
        Assert.Empty(r.Ignorados);
        var bone = (await _produtos.BuscarAsync("Boné")).Single();
        Assert.Equal(10, bone.EstoqueAtual);
        var camiseta = (await _produtos.BuscarAsync("Camiseta")).Single();
        Assert.Equal(8, camiseta.EstoqueAtual);
        var grade = await _produtos.ListarTamanhosAsync(camiseta.Id);
        Assert.Equal(5, grade.Single(t => t.Tamanho == "M").Estoque);
        await using var db = _banco.CriarContexto();
        Assert.Equal(3, db.Movimentacoes.Count(m => m.Motivo == "Importação de CSV"));
    }

    [Fact]
    public async Task Produto_que_ja_existe_nao_e_alterado_e_o_resto_entra()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Boné", "555", 2000, 800, 1), 4);
        var a = ImportadorProdutos.Analisar(Csv("Nome;Código;Preço;Estoque", "Boné;555;99,00;50", "Meia;111;10;2"));

        var r = await _importador.ImportarAsync(a);

        Assert.Equal(1, r.Importados);
        Assert.Contains("código de barras", Assert.Single(r.Ignorados).Motivo);
        var bone = (await _produtos.BuscarAsync("Boné")).Single();
        Assert.Equal((2000L, 4), (bone.PrecoVendaCentavos, bone.EstoqueAtual)); // intacto
    }

    [Fact]
    public async Task Importar_duas_vezes_o_mesmo_arquivo_nao_duplica()
    {
        var a = ImportadorProdutos.Analisar(Csv("Nome;Preço;Estoque", "Meia;10;2", "Cinto;30;1"));

        await _importador.ImportarAsync(a);
        var segunda = await _importador.ImportarAsync(a);

        Assert.Equal(0, segunda.Importados);
        Assert.Equal(2, segunda.Ignorados.Count);
        Assert.Equal(2, (await _produtos.BuscarAsync(null)).Count);
    }
}
