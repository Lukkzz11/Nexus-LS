using System.Text;
using System.Text.RegularExpressions;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.Tests;

public class NotaPdfTests
{
    private sealed class SemImpressora : IImpressoraCupom
    {
        public Task ImprimirAsync(string texto, NexusLS.Core.Models.ConfiguracaoLoja loja) => Task.CompletedTask;
    }

    [Fact]
    public void Pdf_tem_cabecalho_texto_acentuado_e_tabela_de_posicoes_correta()
    {
        var bytes = NotaPdf.Gerar(new[] { "Cliente: José (atacado)", @"Total \ R$ 10,00" });
        var texto = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", texto);
        Assert.EndsWith("%%EOF\n", texto);
        Assert.Contains(@"(Cliente: José \(atacado\)) Tj", texto);
        Assert.Contains(@"(Total \\ R$ 10,00) Tj", texto);

        // cada posição da tabela xref aponta exatamente para "N 0 obj"
        var inicio = int.Parse(Regex.Match(texto, @"startxref\n(\d+)").Groups[1].Value);
        Assert.StartsWith("xref", texto[inicio..]);
        var posicoes = Regex.Matches(texto[inicio..], @"(\d{10}) 00000 n").Select(m => int.Parse(m.Groups[1].Value)).ToList();
        Assert.True(posicoes.Count >= 5);
        for (var i = 0; i < posicoes.Count; i++)
            Assert.StartsWith($"{i + 1} 0 obj", texto[posicoes[i]..]);
    }

    [Fact]
    public void Nota_longa_vira_varias_paginas()
    {
        var bytes = NotaPdf.Gerar(Enumerable.Range(1, 130).Select(i => $"linha {i}"));
        var texto = Encoding.Latin1.GetString(bytes);
        Assert.Matches(@"/Count 3 ", texto);
    }

    [Fact]
    public async Task Nota_da_venda_traz_os_itens_e_o_total()
    {
        using var banco = new BancoDeTeste();
        var produtos = new ProdutoService(banco.Fabrica);
        var item = await produtos.CadastrarAsync(new NexusLS.Core.Services.DadosProduto("Boné", null, 10000, 4000, 1), 5);
        var carrinho = new Carrinho();
        carrinho.Adicionar((await produtos.BuscarAsync("Boné")).Single(), (await produtos.ListarTamanhosAsync(item.Id)).Single(), 1);
        var venda = await new VendaService(banco.Fabrica).FinalizarAsync(
            new SelecaoPagamento(10000).ParaPedido(carrinho, new DescontoResolvido(0, null, 0)) with { ChaveUnica = Guid.NewGuid() });

        var cupons = new CupomService(banco.Fabrica, new ConfiguracaoService(banco.Fabrica), new SemImpressora());
        var texto = Encoding.Latin1.GetString(await cupons.GerarPdfAsync(venda.Id));

        Assert.StartsWith("%PDF", texto);
        Assert.Contains("Boné", texto);
        Assert.Contains("TOTAL", texto);
    }
}
