using System.IO;
using System.Text;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>O botão "Relatório do dia (PDF)" da tela Caixa.</summary>
public sealed class RelatorioDiaCaixaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-relatorio-dia-" + Guid.NewGuid().ToString("N"));
    private readonly VendaService _vendas;
    private readonly ProdutoService _produtos;

    public RelatorioDiaCaixaTests()
    {
        Directory.CreateDirectory(_pasta);
        _vendas = new VendaService(_banco.Fabrica);
        _produtos = new ProdutoService(_banco.Fabrica);
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private VendasViewModel NovaTela(ConfiguracaoService? configuracao = null)
    {
        var cupons = new CupomService(_banco.Fabrica, new ConfiguracaoService(_banco.Fabrica), new ImpressoraDeMentira());
        return new VendasViewModel(_vendas, cupons, configuracao: configuracao);
    }

    private static async Task AteAsync(Func<bool> condicao, string quando)
    {
        for (var i = 0; i < 300 && !condicao(); i++) await Task.Delay(10);
        Assert.True(condicao(), "Tempo esgotado esperando: " + quando);
    }

    [Fact]
    public async Task Botao_do_relatorio_do_dia_grava_o_pdf_com_o_resumo()
    {
        var produto = (await _produtos.CadastrarAsync(new DadosProduto("Boné", null, 2000, 800, 0), 10)).Id;
        await _vendas.FinalizarAsync(new PedidoVenda(new[] { new ItemCarrinho(produto, 2, null) }, 0, FormaPagamento.Pix));
        var configuracao = new ConfiguracaoService(_banco.Fabrica);
        await configuracao.SalvarAsync(new DadosLoja("Life Surf"));

        var vm = NovaTela(configuracao);
        await AteAsync(() => vm.Vendas.Count == 1, "as vendas do dia");

        var destino = Path.Combine(_pasta, "relatorio.pdf");
        string? sugerido = null;
        vm.EscolherArquivoParaSalvar = nome => { sugerido = nome; return destino; };

        await vm.RelatorioDoDiaEmPdfCommand.ExecuteAsync(null);

        Assert.Equal($"relatorio-do-dia-{DateTime.Today:yyyy-MM-dd}.pdf", sugerido);
        Assert.True(File.Exists(destino));
        Assert.False(vm.MensagemEhErro);

        var bytes = File.ReadAllBytes(destino);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));

        // O PDF é texto em WinAnsi: os acentos saem em Latin-1, mas o conteúdo continua legível.
        var texto = Encoding.Latin1.GetString(bytes);
        Assert.Contains("LIFE SURF", texto);
        Assert.Contains("Pix", texto);
        Assert.Contains("R$ 40,00", texto);
    }

    [Fact]
    public async Task Sem_vendas_o_botao_avisa_e_nem_abre_a_caixa_de_salvar()
    {
        var vm = NovaTela();
        await Task.Delay(200); // deixa a carga inicial do dia terminar

        var chamou = false;
        vm.EscolherArquivoParaSalvar = _ => { chamou = true; return null; };

        await vm.RelatorioDoDiaEmPdfCommand.ExecuteAsync(null);

        Assert.False(chamou);
        Assert.True(vm.MensagemEhErro);
        Assert.Contains("vendas neste dia", vm.Mensagem);
    }
}
