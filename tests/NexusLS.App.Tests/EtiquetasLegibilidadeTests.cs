using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Tela de etiquetas: trava de legibilidade e modelos próprios.</summary>
public sealed class EtiquetasLegibilidadeTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-etq-" + Guid.NewGuid().ToString("N"));
    private readonly ProdutoService _produtos;
    private readonly ModeloEtiquetaStore _modelos;
    private readonly EtiquetasViewModel _vm;
    private int _impressoes;

    public EtiquetasLegibilidadeTests()
    {
        _produtos = new ProdutoService(_banco.Fabrica);
        _modelos = new ModeloEtiquetaStore(Path.Combine(_pasta, "etiquetas.json"));
        _vm = new EtiquetasViewModel(_produtos, _modelos);
        _vm.ImprimirEtiquetas = (_, _, _) => { _impressoes++; return true; };
    }

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, recursive: true);
    }

    private async Task AdicionarNaFilaAsync(string nome, string? codigo)
    {
        await _produtos.CadastrarAsync(new DadosProduto(nome, codigo, 1000, 500, 0), 5);
        _vm.Busca = nome;
        await _vm.EscolherPrimeiroCommand.ExecuteAsync(null);
        _vm.AdicionarCommand.Execute(null);
    }

    [Fact]
    public async Task Codigo_legivel_imprime_normalmente()
    {
        await AdicionarNaFilaAsync("Boné", "2000000000015");

        Assert.Equal("", _vm.AvisoLegibilidade);
        await _vm.ImprimirCommand.ExecuteAsync(null);

        Assert.Equal(1, _impressoes);
        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
    }

    [Fact]
    public async Task Codigo_ilegivel_no_papel_escolhido_trava_a_impressao_e_avisa()
    {
        await AdicionarNaFilaAsync("Colar", new string('A', 25));
        _vm.FormatoSelecionado = FormatoEtiqueta.Bobina40x25;

        Assert.Contains("Colar", _vm.AvisoLegibilidade);
        await _vm.ImprimirCommand.ExecuteAsync(null);

        Assert.Equal(0, _impressoes);
        Assert.True(_vm.MensagemEhErro);
        Assert.Contains("ilegível", _vm.Mensagem);
    }

    [Fact]
    public async Task Papel_mais_largo_resolve_o_aviso()
    {
        await AdicionarNaFilaAsync("Colar", new string('A', 25));
        _vm.FormatoSelecionado = FormatoEtiqueta.Bobina40x25;
        Assert.NotEqual("", _vm.AvisoLegibilidade);

        _vm.NovoModeloNome = "Larga";
        _vm.NovoModeloLargura = "100";
        _vm.NovoModeloAltura = "50";
        await _vm.SalvarModeloCommand.ExecuteAsync(null);

        Assert.Equal("", _vm.AvisoLegibilidade);
    }

    [Fact]
    public async Task Salvar_modelo_adiciona_na_lista_seleciona_e_persiste()
    {
        _vm.NovoModeloNome = "Joias";
        _vm.NovoModeloLargura = "60,5";
        _vm.NovoModeloAltura = "40";
        await _vm.SalvarModeloCommand.ExecuteAsync(null);

        Assert.False(_vm.MensagemEhErro, _vm.Mensagem);
        Assert.Equal("Joias (60,5 × 40 mm)".Replace(',', System.Globalization.CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator[0]),
            _vm.FormatoSelecionado.Nome);
        Assert.Equal(5, _vm.Formatos.Count);
        Assert.Single(new EtiquetasViewModel(_produtos, _modelos).Formatos, f => f.Nome.StartsWith("Joias"));
    }

    [Fact]
    public async Task Modelo_com_medidas_invalidas_mostra_erro()
    {
        _vm.NovoModeloNome = "Joias";
        _vm.NovoModeloLargura = "abc";
        _vm.NovoModeloAltura = "40";
        await _vm.SalvarModeloCommand.ExecuteAsync(null);

        Assert.True(_vm.MensagemEhErro);
        Assert.Equal(4, _vm.Formatos.Count);
    }

    [Fact]
    public async Task Remover_modelo_proprio_funciona_e_os_de_fabrica_nao_podem_ser_removidos()
    {
        Assert.False(_vm.PodeRemoverModelo);   // começa em um modelo de fábrica

        _vm.NovoModeloNome = "Joias";
        _vm.NovoModeloLargura = "60";
        _vm.NovoModeloAltura = "40";
        await _vm.SalvarModeloCommand.ExecuteAsync(null);
        Assert.True(_vm.PodeRemoverModelo);

        await _vm.RemoverModeloCommand.ExecuteAsync(null);

        Assert.Equal(4, _vm.Formatos.Count);
        Assert.False(_vm.PodeRemoverModelo);
        Assert.Empty(_modelos.Carregar());
    }
}
