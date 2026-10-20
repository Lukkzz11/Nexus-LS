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

        Assert.Contains("2 produto(s) serão cadastrados", perguntado);
        Assert.Contains("2 produto(s) importado(s)", _vm.Mensagem);
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

        Assert.Contains("1 produto(s) importado(s)", _vm.Mensagem);
        Assert.Contains("Linha 4", relatorio);                 // sem preço
        Assert.Contains("Meia", relatorio);                    // já existia
    }
}
