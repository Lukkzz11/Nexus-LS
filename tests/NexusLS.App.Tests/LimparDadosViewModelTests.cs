using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class LimparDadosViewModelTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private int _perguntas;
    private int _reinicios;
    private bool _respostaDaPergunta = true;
    private bool _administrador = true;

    public void Dispose() => _banco.Dispose();

    private LimparDadosViewModel Criar() => new(
        new LimpezaService(_banco.Fabrica, () => Task.FromResult(@"C:\Backups\nexus_x_pre-limpeza.db")),
        () => _administrador,
        _ => { _perguntas++; return _respostaDaPergunta; },
        () => _reinicios++);

    private async Task<int> CadastrarProdutoAsync()
    {
        var p = await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 1), 3);
        return p.Id;
    }

    private int Produtos() { using var db = _banco.CriarContexto(); return db.Produtos.Count(); }

    [Fact]
    public async Task Sem_digitar_APAGAR_mostra_erro_e_nem_pergunta()
    {
        await CadastrarProdutoAsync();
        var vm = Criar();
        vm.ApagarTudo = true;
        vm.Confirmacao = "apagar";

        await vm.ApagarCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Contains("APAGAR", vm.Mensagem);
        Assert.Equal(0, _perguntas);
        Assert.Equal(0, _reinicios);
        Assert.Equal(1, Produtos());
    }

    [Fact]
    public async Task Recusar_a_pergunta_final_nao_apaga_nem_reinicia()
    {
        await CadastrarProdutoAsync();
        var vm = Criar();
        vm.ApagarTudo = true;
        vm.Confirmacao = "APAGAR";
        _respostaDaPergunta = false;

        await vm.ApagarCommand.ExecuteAsync(null);

        Assert.Equal(1, _perguntas);
        Assert.Equal(0, _reinicios);
        Assert.Equal(1, Produtos());
    }

    [Fact]
    public async Task Confirmando_apaga_mostra_o_backup_e_reinicia()
    {
        await CadastrarProdutoAsync();
        var vm = Criar();
        vm.ApagarTudo = true;
        vm.Confirmacao = "APAGAR";

        await vm.ApagarCommand.ExecuteAsync(null);

        Assert.False(vm.MensagemEhErro);
        Assert.Contains("nexus_x_pre-limpeza.db", vm.Mensagem);
        Assert.Equal(0, Produtos());
        Assert.Equal(1, _reinicios);
        Assert.Equal("", vm.Confirmacao); // a digitação não fica guardada para a próxima vez
    }

    [Fact]
    public async Task Nivel_padrao_so_apaga_o_movimento_e_guarda_os_produtos()
    {
        await CadastrarProdutoAsync();
        var vm = Criar();
        Assert.Equal(NivelLimpeza.Movimento, vm.Nivel);
        vm.Confirmacao = "APAGAR";

        await vm.ApagarCommand.ExecuteAsync(null);

        Assert.Equal(1, Produtos());
        Assert.Equal(1, _reinicios);
    }

    [Fact]
    public async Task Quem_nao_e_administrador_nao_ve_o_bloco_e_nao_consegue_apagar()
    {
        await CadastrarProdutoAsync();
        _administrador = false;
        var vm = Criar();
        vm.ApagarTudo = true;
        vm.Confirmacao = "APAGAR";

        Assert.False(vm.Visivel);
        await vm.ApagarCommand.ExecuteAsync(null);

        Assert.True(vm.MensagemEhErro);
        Assert.Equal(0, _perguntas);
        Assert.Equal(1, Produtos());
    }
}
