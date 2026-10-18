using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdvSimples.Core.Models;
using PdvSimples.Core.Services;

namespace PdvSimples.App.ViewModels;

/// <summary>Uma opção do combo de tipo de ajuste: o valor interno e o texto que o operador lê.</summary>
public record OpcaoAjuste(TipoMovimentacao Tipo, string Nome);

public partial class EstoqueViewModel : ViewModelBase
{
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private int _versaoBusca;
    private int _versaoHistorico;

    public EstoqueViewModel(ProdutoService produtos, EstoqueService estoque)
    {
        _produtos = produtos;
        _estoque = estoque;
        _tipoSelecionado = Opcoes[0];
        _ = CarregarAsync();
    }

    public IReadOnlyList<OpcaoAjuste> Opcoes { get; } = new[]
    {
        new OpcaoAjuste(TipoMovimentacao.Entrada, "Entrada (soma ao estoque)"),
        new OpcaoAjuste(TipoMovimentacao.Perda, "Perda (tira do estoque)"),
        new OpcaoAjuste(TipoMovimentacao.Correcao, "Correção (informar a contagem)")
    };

    public ObservableCollection<Produto> Produtos { get; } = new();
    public ObservableCollection<MovimentacaoEstoque> Movimentacoes { get; } = new();

    // ----- Filtros -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _somenteAbaixoDoMinimo;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAlerta))]
    [NotifyPropertyChangedFor(nameof(TextoAlerta))]
    private int _quantidadeAbaixoDoMinimo;

    // ----- Ajuste -----
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemSelecao))]
    [NotifyPropertyChangedFor(nameof(TextoProduto))]
    private Produto? _selecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RotuloQuantidade))]
    private OpcaoAjuste _tipoSelecionado;

    [ObservableProperty] private string _quantidadeTexto = "";
    [ObservableProperty] private string _motivo = "";

    public bool TemSelecao => Selecionado is not null;
    public bool TemAlerta => QuantidadeAbaixoDoMinimo > 0;
    public string TextoAlerta => $"Atenção: {QuantidadeAbaixoDoMinimo} produto(s) abaixo do estoque mínimo.";

    public string TextoProduto => Selecionado is null
        ? "Selecione um produto na lista."
        : $"{Selecionado.Nome} — estoque atual: {Selecionado.EstoqueAtual}";

    public string RotuloQuantidade =>
        TipoSelecionado.Tipo == TipoMovimentacao.Correcao ? "Quantidade contada (novo estoque)" : "Quantidade";

    partial void OnBuscaChanged(string value) => _ = CarregarAsync();

    partial void OnSomenteAbaixoDoMinimoChanged(bool value) => _ = CarregarAsync();

    partial void OnSelecionadoChanged(Produto? value)
    {
        if (value is not null) Mensagem = "";
        _ = CarregarHistoricoAsync();
    }

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versaoBusca;
        var idSelecionado = Selecionado?.Id;

        var lista = await _produtos.BuscarAsync(Busca);
        var abaixo = await _estoque.ListarAbaixoDoMinimoAsync();
        if (versao != _versaoBusca) return; // chegou uma busca mais nova

        if (SomenteAbaixoDoMinimo) lista = lista.Where(p => p.EstoqueBaixo).ToList();

        Produtos.Clear(); // a tela zera a seleção ao limpar; restauramos logo abaixo
        foreach (var p in lista) Produtos.Add(p);
        QuantidadeAbaixoDoMinimo = abaixo.Count;

        if (idSelecionado is int id) Selecionado = Produtos.FirstOrDefault(p => p.Id == id);
    });

    [RelayCommand]
    private Task AplicarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is null)
            throw new RegraDeNegocioException("Selecione um produto na lista.");
        if (!int.TryParse(QuantidadeTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var quantidade))
            throw new RegraDeNegocioException("Quantidade inválida. Use um número inteiro, sem sinal.");

        var tipo = TipoSelecionado.Tipo;
        var variacao = quantidade;

        if (tipo == TipoMovimentacao.Correcao)
        {
            // O operador informa o que CONTOU na prateleira; o sistema calcula a diferença.
            variacao = quantidade - Selecionado.EstoqueAtual;
            if (variacao == 0)
                throw new RegraDeNegocioException("A contagem é igual ao estoque atual. Nada a corrigir.");
        }

        var id = Selecionado.Id;
        var nome = Selecionado.Nome;
        var atualizado = await _estoque.AjustarAsync(id, tipo, variacao, Motivo);

        QuantidadeTexto = "";
        Motivo = "";
        await CarregarAsync();
        await CarregarHistoricoAsync();
        MostrarSucesso($"Estoque de \"{nome}\" atualizado: agora {atualizado.EstoqueAtual}.");
    });

    private async Task CarregarHistoricoAsync()
    {
        var versao = ++_versaoHistorico;
        var id = Selecionado?.Id;

        await ExecutarAsync(async () =>
        {
            var lista = id is int produtoId
                ? await _estoque.ListarMovimentacoesAsync(produtoId)
                : new List<MovimentacaoEstoque>();
            if (versao != _versaoHistorico) return;

            Movimentacoes.Clear();
            foreach (var m in lista) Movimentacoes.Add(m);
        });
    }
}
