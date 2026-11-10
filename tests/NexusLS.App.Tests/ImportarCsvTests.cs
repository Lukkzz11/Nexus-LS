using System.IO;
using System.Text;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>O botão "Importar CSV" da tela Estoque da loja.</summary>
public sealed class ImportarCsvTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-imp-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly EstoqueLojaViewModel _vm;

    public ImportarCsvTests()
    {
        Directory.CreateDirectory(_pasta);
        _produtos = new ProdutoService(_banco.Fabrica);
        _vm = new EstoqueLojaViewModel(_produtos, new EstoqueService(_banco.Fabrica));
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    private string Arquivo(string conteudo)
    {
        var caminho = Path.Combine(_pasta, "produtos.csv");
        File.WriteAllText(caminho, conteudo, new UTF8Encoding(true));
        return caminho;
    }

    [Fact]
    public async Task Confirmando_cadastra_os_produtos_e_atualiza_a_lista()
    {
        var caminho = Arquivo("Nome;Preço;Estoque\r\nMeia;10,00;4\r\nCinto;30,00;2");
        _vm.EscolherArquivoParaAbrir = () => caminho;
        string? perguntado = null;
        _vm.ConfirmarImportacao = texto => { perguntado = texto; return true; };

        await _vm.ImportarCsvCommand.ExecuteAsync(null);

        Assert.Contains("2 produto(s) serão comparados com o estoque", perguntado);
        Assert.Contains("2 novo(s)", _vm.Mensagem);
        Assert.False(_vm.MensagemEhErro);
        Assert.Equal(2, _vm.Produtos.Count);
        Assert.True(_vm.PodeImportar);
    }

    [Fact]
    public async Task Recusar_a_confirmacao_nao_cadastra_nada()
    {
        var caminho = Arquivo("Nome;Preço\r\nMeia;10");
        _vm.EscolherArquivoParaAbrir = () => caminho;
        _vm.ConfirmarImportacao = _ => false;

        await _vm.ImportarCsvCommand.ExecuteAsync(null);

        Assert.Contains("cancelada", _vm.Mensagem);
        Assert.Empty(await _produtos.BuscarAsync(null));
    }

    [Fact]
    public async Task Cancelar_a_escolha_do_arquivo_nao_faz_nada()
    {
        _vm.EscolherArquivoParaAbrir = () => null;

        await _vm.ImportarCsvCommand.ExecuteAsync(null);

        Assert.Equal("", _vm.Mensagem);
    }

    [Fact]
    public async Task Arquivo_sem_coluna_de_preco_mostra_o_erro_em_portugues()
    {
        _vm.EscolherArquivoParaAbrir = () => Arquivo("Nome;Estoque\r\nMeia;4");

        await _vm.ImportarCsvCommand.ExecuteAsync(null);

        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("preço de venda", _vm.Mensagem);
    }

    [Fact]
    public async Task Linhas_ruins_e_produtos_existentes_aparecem_no_relatorio_final()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Meia", "111", 1000, 400, 1), 1);
        _vm.EscolherArquivoParaAbrir = () => Arquivo("Nome;Código;Preço\r\nMeia;111;10\r\nCinto;;30\r\nSem preço;;");
        _vm.ConfirmarImportacao = _ => true;
        string? relatorio = null;
        _vm.InformarImportacao = t => relatorio = t;

        await _vm.ImportarCsvCommand.ExecuteAsync(null);

        Assert.Contains("Linha 4", relatorio);                 // sem preço
        Assert.Contains("sem mudança", _vm.Mensagem);          // Meia já existia igual: nada foi alterado
    }
}

/// <summary>O botão "Exportar CSV" da tela Estoque da loja.</summary>
public sealed class ExportarEstoqueTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-exp-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly EstoqueLojaViewModel _vm;

    public ExportarEstoqueTelaTests()
    {
        Directory.CreateDirectory(_pasta);
        _produtos = new ProdutoService(_banco.Fabrica);
        _vm = new EstoqueLojaViewModel(_produtos, new EstoqueService(_banco.Fabrica), relogio: () => new DateTime(2026, 11, 4, 9, 0, 0));
    }

    public void Dispose()
    {
        _banco.Dispose();
        try { Directory.Delete(_pasta, true); } catch (IOException) { }
    }

    [Fact]
    public async Task Exporta_o_estoque_no_arquivo_escolhido_com_nome_sugerido_pela_data()
    {
        await _produtos.CadastrarAsync(new DadosProduto("Meia", null, 1000, 500, 1), 3);
        var caminho = Path.Combine(_pasta, "saida.csv");
        string? sugerido = null;
        _vm.EscolherArquivoParaSalvar = nome => { sugerido = nome; return caminho; };

        await _vm.ExportarCsvCommand.ExecuteAsync(null);

        Assert.Equal("estoque-2026-11-04.csv", sugerido);
        Assert.False(_vm.MensagemEhErro);
        Assert.Contains("Meia", File.ReadAllText(caminho));
    }

    [Fact]
    public async Task Cancelar_a_caixa_nao_grava_nada_e_sem_produtos_avisa()
    {
        _vm.EscolherArquivoParaSalvar = _ => throw new InvalidOperationException("não deveria abrir a caixa");
        await _vm.ExportarCsvCommand.ExecuteAsync(null);
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("Não há produtos", _vm.Mensagem);

        await _produtos.CadastrarAsync(new DadosProduto("Meia", null, 1000, 500, 1), 3);
        _vm.EscolherArquivoParaSalvar = _ => null;
        await _vm.ExportarCsvCommand.ExecuteAsync(null);
        Assert.Empty(Directory.GetFiles(_pasta));
    }
}
