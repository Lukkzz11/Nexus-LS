using System.Text;
using NexusLS.Core.Gaveta;
using NexusLS.Core.Models;
using NexusLS.Core.Pdf;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class DocumentosPdfTests
{
    private static ConfiguracaoLoja Loja() => new()
    {
        NomeLoja = "Life Surf", RazaoSocial = "Life Surf Confecções LTDA", Documento = "11.222.333/0001-81",
        Endereco = "Rua A", EnderecoNumero = "10", Bairro = "Centro", CidadeUf = "Caruaru - PE", Telefone = "(81) 99999-0000"
    };

    private static Venda Venda() => new()
    {
        Id = 42, DataHora = new DateTime(2026, 11, 19, 15, 30, 0), FormaPagamento = FormaPagamento.Dinheiro,
        SubtotalCentavos = 25000, DescontoCentavos = 2500, TotalCentavos = 22500, ValorRecebidoCentavos = 25000, TrocoCentavos = 2500,
        Operador = "Lucas",
        Itens =
        {
            new ItemVenda { NomeProduto = "Camiseta Surf", Tamanho = "M", Quantidade = 2, PrecoUnitarioCentavos = 10000, TotalCentavos = 20000 },
            new ItemVenda { NomeProduto = "Boné", Tamanho = Tamanhos.SemTamanho, Quantidade = 1, PrecoUnitarioCentavos = 5000, TotalCentavos = 5000 }
        }
    };

    [Fact]
    public void Comprovante_traz_loja_itens_total_e_pede_assinatura()
    {
        var pdf = DocumentosPdf.ComprovanteVenda(Venda(), Loja());
        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        var t = PdfTexto.Extrair(pdf);
        Assert.Contains("Life Surf Confecções LTDA", t);
        Assert.Contains("Comprovante de venda nº 42", t);
        Assert.Contains("Camiseta Surf", t);
        Assert.Contains("TOTAL", t);
        Assert.Contains("Assinatura do cliente", t);
        Assert.Contains("Declaro que conferi", t);
        Assert.Contains("Página 1 de 1", t);
    }

    [Fact]
    public void Comprovante_sem_pedido_de_assinatura_nao_tem_o_campo()
    {
        var t = PdfTexto.Extrair(DocumentosPdf.ComprovanteVenda(Venda(), Loja(), pedirAssinatura: false));
        Assert.DoesNotContain("Assinatura do cliente", t);
    }

    [Fact]
    public void Relatorio_grande_vira_varias_paginas_com_numeracao()
    {
        var linhas = Enumerable.Range(1, 220).Select(i => new[] { i.ToString(), "Produto " + i, "R$ 10,00" });
        var pdf = DocumentosPdf.Relatorio("Relatório de produtos", "Novembro/2026", Loja(),
            new[] { new ColunaPdf("Nº", 1), new ColunaPdf("Produto", 4), new ColunaPdf("Preço", 2, AlinhamentoPdf.Direita) }, linhas);
        var t = PdfTexto.Extrair(pdf);
        Assert.Matches(@"Página 1 de [4-9]", t);
        Assert.Contains("Produto 220", t);
        Assert.Contains("Responsável", t);
    }

    [Fact]
    public void Relatorio_do_dia_tem_resumo_formas_e_assinatura()
    {
        var t = PdfTexto.Extrair(DocumentosPdf.RelatorioDia(new[] { Venda() }, new DateTime(2026, 11, 19), Loja()));
        Assert.Contains("Relatório do dia", t);
        Assert.Contains("TICKET MÉDIO", t);
        Assert.Contains("Conferido por", t);
    }

    [Fact]
    public void Texto_longo_quebra_sem_estourar_e_acento_e_parentese_nao_quebram_o_arquivo()
    {
        var pdf = new PdfDocumento("Teste (ação)", new EmpresaPdf("Loja (X)"))
            .Paragrafo(string.Join(" ", Enumerable.Repeat("palavra-comprida-de-teste", 60))).Gerar();
        var t = PdfTexto.Extrair(pdf);
        Assert.Contains("Teste (ação)", t);
        Assert.Contains("palavra-comprida-de-teste", t);
    }

    [Fact]
    public void Medida_de_texto_negrito_e_maior_que_normal() =>
        Assert.True(PdfDocumento.Medir("Nexus LS Relatório", true, 10) > PdfDocumento.Medir("Nexus LS Relatório", false, 10));
}

public class GavetaServiceTests : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nx-gaveta-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_pasta, true); } catch { } }

    private GavetaService Gaveta() => new(Path.Combine(_pasta, "anexos"), () => new DateTime(2026, 11, 19));

    [Fact]
    public void Salva_por_categoria_e_mes_e_nunca_sobrescreve()
    {
        var g = Gaveta();
        var a = g.Salvar(CategoriaGaveta.Cupons, "venda-1.pdf", new byte[] { 1 });
        var b = g.Salvar(CategoriaGaveta.Cupons, "venda-1.pdf", new byte[] { 2 });
        Assert.NotEqual(a, b);
        Assert.Contains(Path.Combine("Cupons", "2026-11"), a);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(a));
        Assert.Equal(2, g.Listar(CategoriaGaveta.Cupons).Count);
        Assert.Empty(g.Listar(CategoriaGaveta.Relatorios));
    }

    [Fact]
    public void Nome_com_caminho_ou_tipo_proibido_e_tratado()
    {
        var g = Gaveta();
        var salvo = g.Salvar(CategoriaGaveta.Outros, @"..\..\fora.pdf", new byte[] { 1 });
        Assert.StartsWith(g.Pasta, salvo);
        Assert.Throws<RegraDeNegocioException>(() => g.Salvar(CategoriaGaveta.Outros, "virus.exe", new byte[] { 1 }));
    }

    [Fact]
    public void Busca_filtra_pelo_nome_e_excluir_so_vale_dentro_da_gaveta()
    {
        var g = Gaveta();
        var a = g.Salvar(CategoriaGaveta.NotasFiscais, "nfe-silva.pdf", new byte[] { 1 });
        g.Salvar(CategoriaGaveta.NotasFiscais, "nfe-souza.pdf", new byte[] { 1 });
        Assert.Single(g.Listar(null, "silva"));
        g.Excluir(a);
        Assert.False(File.Exists(a));
        var fora = Path.Combine(_pasta, "fora.txt");
        File.WriteAllText(fora, "x");
        Assert.Throws<RegraDeNegocioException>(() => g.Excluir(fora));
        Assert.True(File.Exists(fora));
    }
}

public class CsvParaPdfTests
{
    [Fact]
    public void Planilha_do_nexus_vira_relatorio_com_titulo_periodo_e_colunas()
    {
        var csv = "\uFEFFProduto;Pecas;Valor\r\n\"Camiseta; Surf\";3;150,00\r\nBoné;1;50,00\r\n";
        var (titulo, periodo) = CsvParaPdf.TituloDoArquivo("mais-vendidos-2026-11-01_a_2026-11-19.pdf");
        Assert.Equal("Relatório — Mais vendidos", titulo);
        Assert.Equal("01/11/2026 a 19/11/2026", periodo);

        var t = PdfTexto.Extrair(CsvParaPdf.Gerar(csv, titulo, periodo, new ConfiguracaoLoja { NomeLoja = "Loja" }));
        Assert.Contains("Camiseta; Surf", t);
        Assert.Contains("150,00", t);
        Assert.Contains("Conferido por", t);
    }

    [Fact]
    public void Leitor_trata_aspas_duplas_e_apostrofo_de_protecao()
    {
        var l = CsvParaPdf.Ler("A;B\r\n\"diz \"\"oi\"\"\";'=cmd\r\n");
        Assert.Equal("diz \"oi\"", l[1][0]);
        Assert.Equal("=cmd", l[1][1]);
    }

    [Fact]
    public void Salvar_com_extensao_pdf_gera_pdf_e_avisa_a_gaveta()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "nx-csvpdf-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        string? avisado = null;
        CsvParaPdf.AoGerar = (nome, _) => avisado = nome;
        try
        {
            var destino = Path.Combine(pasta, "relatorio-2026-11-19.pdf");
            ExportadorCsv.Salvar(destino, "A;B\r\n1;2\r\n");
            Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(destino), 0, 4));
            Assert.Equal("relatorio-2026-11-19.pdf", avisado);
            var csv = Path.Combine(pasta, "x.csv");
            ExportadorCsv.Salvar(csv, "A;B\r\n1;2\r\n");
            Assert.Contains("A;B", File.ReadAllText(csv));
        }
        finally { CsvParaPdf.AoGerar = null; Directory.Delete(pasta, true); }
    }
}
