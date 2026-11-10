using System.Text;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Tests;

/// <summary>O relatório do dia em PDF (tela Caixa): resumo, formas de pagamento e lista das vendas.</summary>
public class RelatorioDiaPdfTests
{
    private static Venda Venda(int id, int hora, FormaPagamento forma, long total, bool cancelada = false) => new()
    {
        Id = id,
        DataHora = new DateTime(2026, 11, 10, hora, 0, 0),
        FormaPagamento = forma,
        SubtotalCentavos = total,
        TotalCentavos = total,
        Operador = "Ana",
        Cancelada = cancelada,
        Itens =
        {
            new ItemVenda { NomeProduto = "Camiseta", Quantidade = 1, PrecoUnitarioCentavos = total, TotalCentavos = total }
        }
    };

    private static List<Venda> Dia() => new()
    {
        Venda(1, 10, FormaPagamento.Pix, 10000),
        Venda(2, 11, FormaPagamento.Dinheiro, 5000),
        Venda(3, 12, FormaPagamento.Pix, 4000, cancelada: true),
        Venda(4, 13, FormaPagamento.Pix, 6000)
    };

    private static string Texto(IEnumerable<string> linhas) => string.Join("\n", linhas);

    [Fact]
    public void Resumo_soma_so_as_vendas_validas_e_avisa_as_canceladas()
    {
        var texto = Texto(RelatorioDiaPdf.Linhas(Dia(), new DateTime(2026, 11, 10), "Loja do Zé", new DateTime(2026, 11, 10, 18, 30, 0)));

        Assert.Contains("LOJA DO ZÉ", texto);
        Assert.Contains("10/11/2026", texto);
        Assert.Contains("Total vendido:", texto);
        Assert.Contains("R$ 210,00", texto);
        Assert.Contains("Ticket médio:", texto);
        Assert.Contains("R$ 70,00", texto);
        Assert.Contains("Canceladas (não somadas):", texto);
        Assert.Contains("1", texto);
    }

    [Fact]
    public void Formas_de_pagamento_aparecem_com_quantidade_e_total()
    {
        var texto = Texto(RelatorioDiaPdf.Linhas(Dia(), new DateTime(2026, 11, 10), "Loja"));

        Assert.Contains("Pix  (2)", texto);
        Assert.Contains("R$ 160,00", texto);
        Assert.Contains("Dinheiro  (1)", texto);
        Assert.Contains("R$ 50,00", texto);
    }

    [Fact]
    public void Lista_traz_numero_hora_operador_e_marca_a_cancelada()
    {
        var texto = Texto(RelatorioDiaPdf.Linhas(Dia(), new DateTime(2026, 11, 10), "Loja"));

        Assert.Contains("#1", texto);
        Assert.Contains("10:00", texto);
        Assert.Contains("Ana", texto);
        Assert.Contains("(CANCELADA)", texto);
        Assert.Contains("R$ 40,00", texto);
    }

    [Fact]
    public void Nenhuma_linha_passa_da_largura_do_papel()
    {
        var linhas = RelatorioDiaPdf.Linhas(Dia(), new DateTime(2026, 11, 10), "Life Surf Confecções Comércio de Roupas e Acessórios");

        Assert.All(linhas, l => Assert.True(l.Length <= 78, $"\"{l}\" tem {l.Length} letras"));
    }

    [Fact]
    public void Dia_sem_venda_gera_relatorio_zerado()
    {
        var texto = Texto(RelatorioDiaPdf.Linhas(Array.Empty<Venda>(), new DateTime(2026, 11, 10), "Loja"));

        Assert.Contains("R$ 0,00", texto);
        Assert.DoesNotContain("Canceladas (não somadas):", texto);
    }

    [Fact]
    public void Pdf_comeca_com_o_cabecalho_e_o_nome_do_arquivo_tem_o_dia()
    {
        var bytes = RelatorioDiaPdf.Gerar(Dia(), new DateTime(2026, 11, 10), "Loja");

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal("relatorio-do-dia-2026-11-10.pdf", RelatorioDiaPdf.NomeDoArquivo(new DateTime(2026, 11, 10)));
    }
}
