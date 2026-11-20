using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public sealed record OpcaoModeloPadronizacao(
    string Titulo,
    string Subtitulo,
    string OrigemBadge,
    bool EhDoUsuario,
    ModeloEtiquetaDesign Modelo,
    Produto? ProdutoOrigem = null)
{
    public string Dimensoes => $"{Modelo.LarguraMm:0.#} × {Modelo.AlturaMm:0.#} mm";
}

/// <summary>
/// Tela "Padronizar Etiqueta": permite escolher um modelo salvo/feito pelo operador
/// (ou uma predefinição) e aplicá-lo a todas as etiquetas do catálogo, respeitando
/// dinamicamente o Tamanho, a Referência original de cada produto e o Código de Barras (Ref + Tamanho).
/// </summary>
public partial class PadronizarEtiquetaViewModel : ObservableObject
{
    private readonly string _nomeLoja;

    public ObservableCollection<OpcaoModeloPadronizacao> OpcoesModelos { get; } = new();

    public ObservableCollection<string> TamanhosAmostra { get; } = new();

    [ObservableProperty]
    private OpcaoModeloPadronizacao? _opcaoSelecionada;

    [ObservableProperty]
    private bool _aplicarEmTodos = true;

    [ObservableProperty]
    private bool _aplicarApenasFiltrados;

    [ObservableProperty]
    private Produto? _produtoAmostra;

    [ObservableProperty]
    private string _tamanhoAmostraSelecionado = "42";

    [ObservableProperty]
    private BitmapSource? _previaImagem;

    [ObservableProperty]
    private string _previaNomeProduto = "";

    [ObservableProperty]
    private string _previaReferencia = "";

    [ObservableProperty]
    private string _previaTamanho = "";

    [ObservableProperty]
    private string _previaCodigoBarras = "";

    [ObservableProperty]
    private string _previaDimensoes = "";

    public int TotalTodosProdutos { get; }

    public int TotalProdutosFiltrados { get; }

    public bool PodeFiltrados => TotalProdutosFiltrados > 0 && TotalProdutosFiltrados < TotalTodosProdutos;

    public string TextoEscopoTodos => $"Todos os produtos do catálogo ({TotalTodosProdutos} produtos)";

    public string TextoEscopoFiltrados => $"Apenas os produtos filtrados na lista ({TotalProdutosFiltrados} produtos)";

    public PadronizarEtiquetaViewModel(
        ModeloEtiquetaDesign? modeloEstudioAtual,
        IReadOnlyList<Produto> todosProdutos,
        int totalFiltrados,
        Produto? produtoAmostra = null,
        string nomeLoja = "NEXUS LS")
    {
        _nomeLoja = string.IsNullOrWhiteSpace(nomeLoja) ? "NEXUS LS" : nomeLoja;
        TotalTodosProdutos = todosProdutos?.Count ?? 0;
        TotalProdutosFiltrados = totalFiltrados > 0 ? totalFiltrados : TotalTodosProdutos;

        var mapaProdutos = (todosProdutos ?? Array.Empty<Produto>())
            .ToDictionary(p => p.Id);

        // 1. Modelos salvos pelo usuário nos produtos (ex.: etiqueta do 1402 que ele montou e salvou)
        var salvos = GerenciadorEtiquetasProdutoService.ListarModelosPersonalizadosSalvos(mapaProdutos);
        foreach (var salvo in salvos)
        {
            mapaProdutos.TryGetValue(salvo.ProdutoId, out var prodOrigem);
            OpcoesModelos.Add(new OpcaoModeloPadronizacao(
                salvo.Titulo,
                salvo.Subtitulo,
                "SALVO POR VOCÊ",
                true,
                ClonarModelo(salvo.Modelo),
                prodOrigem));
        }

        // 2. Modelo aberto atualmente no Estúdio
        if (modeloEstudioAtual is not null && modeloEstudioAtual.Elementos.Count > 0)
        {
            OpcoesModelos.Add(new OpcaoModeloPadronizacao(
                $"Modelo Atual do Estúdio ({modeloEstudioAtual.Nome})",
                $"Em edição no Estúdio · {modeloEstudioAtual.LarguraMm:0.#} × {modeloEstudioAtual.AlturaMm:0.#} mm",
                "ESTÚDIO ATUAL",
                true,
                ClonarModelo(modeloEstudioAtual),
                produtoAmostra));
        }

        // 3. Predefinições prontas (destacando a Tag Vestuário Tamanho + Ref + Código em primeiro lugar)
        var presets = PredefinicaoEtiqueta.ObterCatalogoCompleto()
            .OrderByDescending(p => p.Nome.Contains("Tag Vestuário", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var preset in presets)
        {
            var m = preset.Fabrica();
            if (m.Elementos.Count == 0) continue;

            OpcoesModelos.Add(new OpcaoModeloPadronizacao(
                preset.Nome,
                $"{preset.Categoria} · {m.LarguraMm:0.#} × {m.AlturaMm:0.#} mm",
                "MODELO PRONTO",
                false,
                m));
        }

        // Produto de amostra para visualizar os tamanhos, referência e código de barras ao vivo
        _produtoAmostra = produtoAmostra
            ?? todosProdutos?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Referencia))
            ?? todosProdutos?.FirstOrDefault()
            ?? new Produto
            {
                Id = 45,
                Nome = "BERMUDA CARGO",
                Referencia = "1402",
                Categoria = "Bermuda",
                PrecoVendaCentavos = 7000,
                Tamanhos = new List<ProdutoTamanho>
                {
                    new() { Tamanho = "42", Ordem = 1, Estoque = 6 },
                    new() { Tamanho = "44", Ordem = 2, Estoque = 8 },
                    new() { Tamanho = "46", Ordem = 3, Estoque = 8 },
                    new() { Tamanho = "48", Ordem = 4, Estoque = 12 },
                    new() { Tamanho = "50", Ordem = 5, Estoque = 2 }
                }
            };

        CarregarTamanhosAmostra(_produtoAmostra);
        OpcaoSelecionada = OpcoesModelos.FirstOrDefault();
        AtualizarPrevia();
    }

    private void CarregarTamanhosAmostra(Produto produto)
    {
        TamanhosAmostra.Clear();

        var reais = (produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Where(t => !string.IsNullOrWhiteSpace(t.Tamanho) &&
                        !string.Equals(t.Tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Ordem > 0 ? t.Ordem : int.MaxValue)
            .Select(t => t.Tamanho.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (reais.Count == 0)
            reais.AddRange(new[] { "42", "44", "46", "48", "50" });

        foreach (var t in reais)
            TamanhosAmostra.Add(t);

        TamanhoAmostraSelecionado = TamanhosAmostra[0];
    }

    partial void OnOpcaoSelecionadaChanged(OpcaoModeloPadronizacao? value) => AtualizarPrevia();

    partial void OnTamanhoAmostraSelecionadoChanged(string value) => AtualizarPrevia();

    partial void OnAplicarEmTodosChanged(bool value)
    {
        if (value && AplicarApenasFiltrados)
            AplicarApenasFiltrados = false;
    }

    partial void OnAplicarApenasFiltradosChanged(bool value)
    {
        if (value && AplicarEmTodos)
            AplicarEmTodos = false;
    }

    [RelayCommand]
    public void SelecionarTamanhoAmostra(string? tamanho)
    {
        if (!string.IsNullOrWhiteSpace(tamanho))
            TamanhoAmostraSelecionado = tamanho;
    }

    [RelayCommand]
    public void CarregarDeArquivo()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Escolher Modelo de Etiqueta Salvo (.json)",
            Filter = "Modelo de Etiqueta (*.json)|*.json"
        };

        if (ofd.ShowDialog() == true)
        {
            CarregarModeloDeCaminho(ofd.FileName);
        }
    }

    public bool CarregarModeloDeCaminho(string caminho)
    {
        try
        {
            if (!File.Exists(caminho)) return false;
            var carregado = ModeloEtiquetaDesign.Carregar(caminho);
            var opcao = new OpcaoModeloPadronizacao(
                $"{carregado.Nome} ({Path.GetFileName(caminho)})",
                $"Carregado de arquivo · {carregado.LarguraMm:0.#} × {carregado.AlturaMm:0.#} mm",
                "ARQUIVO SALVO",
                true,
                carregado,
                ProdutoAmostra);

            OpcoesModelos.Insert(0, opcao);
            OpcaoSelecionada = opcao;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Devolve uma cópia do modelo selecionado já com as regras inteligentes aplicadas:
    /// Tamanho dinâmico, Referência vinculada sem alterar o cadastro, e Código de Barras = Referência + Número interno do tamanho.
    /// </summary>
    public ModeloEtiquetaDesign? ObterModeloPadronizado()
    {
        if (OpcaoSelecionada is null) return null;

        var clone = ClonarModelo(OpcaoSelecionada.Modelo);
        DesignerViewModel.PadronizarModeloParaGrade(
            clone,
            OpcaoSelecionada.ProdutoOrigem ?? ProdutoAmostra,
            TamanhosAmostra);
        return clone;
    }

    public void AtualizarPrevia()
    {
        var modelo = ObterModeloPadronizado();
        if (modelo is null || ProdutoAmostra is null)
        {
            PreviaImagem = null;
            return;
        }

        var tam = string.IsNullOrWhiteSpace(TamanhoAmostraSelecionado) ? "42" : TamanhoAmostraSelecionado;
        PreviaNomeProduto = ProdutoAmostra.Nome;
        PreviaReferencia = !string.IsNullOrWhiteSpace(ProdutoAmostra.Referencia) ? ProdutoAmostra.Referencia! : "1402";
        PreviaTamanho = tam;
        PreviaCodigoBarras = CodigoBarrasTamanho.Resolver(ProdutoAmostra, tam) ?? $"{PreviaReferencia}1";
        PreviaDimensoes = $"{modelo.LarguraMm:0.#} × {modelo.AlturaMm:0.#} mm";

        try
        {
            PreviaImagem = EditorDesenhoService.RenderizarBitmap(modelo, ProdutoAmostra, tam, _nomeLoja, escalaDpi: 2.5);
        }
        catch
        {
            PreviaImagem = null;
        }
    }

    public static ModeloEtiquetaDesign ClonarModelo(ModeloEtiquetaDesign origem)
    {
        var json = JsonSerializer.Serialize(origem, ModeloEtiquetaDesign.JsonOpts);
        return JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, ModeloEtiquetaDesign.JsonOpts)
               ?? ModeloEtiquetaDesign.CriarPadrao50x30();
    }
}
