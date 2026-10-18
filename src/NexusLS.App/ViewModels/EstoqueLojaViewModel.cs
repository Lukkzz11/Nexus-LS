using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma opção do combo de tipo de ajuste: o valor interno e o texto que o operador lê.</summary>
public record OpcaoAjuste(TipoMovimentacao Tipo, string Nome);

/// <summary>
/// Estoque da loja: numa tela só, a lista de produtos, o cadastro (nome, preços, mínimo), a grade de tamanhos
/// com o estoque de cada um e o ajuste de estoque com motivo. Todo ajuste gera uma movimentação no histórico.
/// </summary>
public partial class EstoqueLojaViewModel : ViewModelBase
{
    private readonly ProdutoService _produtos;
    private readonly EstoqueService _estoque;
    private int _versaoBusca;
    private bool _silenciar; // true enquanto a seleção da lista é refeita pelo programa (não pelo operador)

    public EstoqueLojaViewModel(ProdutoService produtos, EstoqueService estoque)
    {
        _produtos = produtos;
        _estoque = estoque;
        _tipoSelecionado = Opcoes[0];
        Novo();
        _ = BuscarAsync();
    }

    public IReadOnlyList<OpcaoAjuste> Opcoes { get; } = new[]
    {
        new OpcaoAjuste(TipoMovimentacao.Entrada, "Entrada (soma ao estoque)"),
        new OpcaoAjuste(TipoMovimentacao.Perda, "Perda (tira do estoque)"),
        new OpcaoAjuste(TipoMovimentacao.Correcao, "Correção (informar a contagem)")
    };

    public ObservableCollection<Produto> Produtos { get; } = new();
    public ObservableCollection<ProdutoTamanho> Grade { get; } = new();
    public ObservableCollection<MovimentacaoEstoque> Movimentacoes { get; } = new();

    // ----- Lista e filtros -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private bool _somenteAbaixoDoMinimo;
    [ObservableProperty] private Produto? _selecionado;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemAlerta))]
    [NotifyPropertyChangedFor(nameof(TextoAlerta))]
    private int _quantidadeAbaixoDoMinimo;

    public bool TemAlerta => QuantidadeAbaixoDoMinimo > 0;
    public string TextoAlerta => $"Atenção: {QuantidadeAbaixoDoMinimo} produto(s) abaixo do estoque mínimo.";

    // ----- Cadastro do produto (tudo como texto: é o que o operador digita) -----
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _codigoBarras = "";
    [ObservableProperty] private string _referencia = "";
    [ObservableProperty] private string _precoVenda = "";
    [ObservableProperty] private string _precoAtacado = "";
    [ObservableProperty] private string _precoCusto = "";
    [ObservableProperty] private string _estoqueMinimo = "0";
    [ObservableProperty] private string _estoqueInicial = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaEditando))]
    [NotifyPropertyChangedFor(nameof(EhNovo))]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    private int? _idEditando; // null = cadastrando um produto novo

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoBotaoAtivo))]
    private bool _produtoAtivo = true;

    [ObservableProperty] private string _nomeEditando = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstoqueTotalTexto))]
    private int _estoqueTotalEditando;

    public bool EstaEditando => IdEditando is not null;
    public bool EhNovo => !EstaEditando;
    public string TituloFormulario => EstaEditando ? "Editar produto" : "Novo produto";
    public string TextoBotaoAtivo => ProdutoAtivo ? "Desativar" : "Reativar";
    public string EstoqueTotalTexto => $"Estoque total: {EstoqueTotalEditando}";

    // ----- Grade de tamanhos e ajuste -----
    [ObservableProperty] private string _novoTamanho = "";
    [ObservableProperty] private ProdutoTamanho? _tamanhoSelecionado;
    [ObservableProperty] private OpcaoAjuste _tipoSelecionado;
    [ObservableProperty] private string _quantidadeTexto = "";
    [ObservableProperty] private string _motivo = "";

    public string TextoTamanhoAjuste => TamanhoSelecionado is null
        ? "Escolha um tamanho na lista acima."
        : TamanhoSelecionado.EhSemTamanho
            ? $"Ajustando: Sem tamanho (estoque {TamanhoSelecionado.Estoque})"
            : $"Ajustando o tamanho {TamanhoSelecionado.Tamanho} (estoque {TamanhoSelecionado.Estoque})";

    public bool PodeRemoverTamanho => TamanhoSelecionado is { EhSemTamanho: false };

    public string RotuloQuantidade =>
        TipoSelecionado.Tipo == TipoMovimentacao.Correcao ? "Quantidade contada (novo estoque)" : "Quantidade";

    partial void OnBuscaChanged(string value) => _ = BuscarAsync();
    partial void OnMostrarInativosChanged(bool value) => _ = BuscarAsync();
    partial void OnSomenteAbaixoDoMinimoChanged(bool value) => _ = BuscarAsync();

    partial void OnTipoSelecionadoChanged(OpcaoAjuste value) => OnPropertyChanged(nameof(RotuloQuantidade));

    partial void OnTamanhoSelecionadoChanged(ProdutoTamanho? value)
    {
        OnPropertyChanged(nameof(TextoTamanhoAjuste));
        OnPropertyChanged(nameof(PodeRemoverTamanho));
    }

    /// <summary>O operador escolheu um produto na lista: abre o cadastro, a grade e o histórico dele.</summary>
    partial void OnSelecionadoChanged(Produto? value)
    {
        if (_silenciar || value is null) return; // a lista foi recarregada: não apaga o que está sendo editado
        _ = ExecutarAsync(() => AbrirProdutoAsync(value));
    }

    // ================= Lista =================

    [RelayCommand]
    private Task BuscarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versaoBusca; // se o operador digita rápido, só a busca mais recente atualiza a lista
        var lista = await _produtos.BuscarAsync(Busca, MostrarInativos);
        var abaixo = await _estoque.ListarAbaixoDoMinimoAsync();
        if (versao != _versaoBusca) return;

        if (SomenteAbaixoDoMinimo) lista = lista.Where(p => p.EstoqueBaixo).ToList();

        Produtos.Clear();
        foreach (var p in lista) Produtos.Add(p);
        QuantidadeAbaixoDoMinimo = abaixo.Count;

        if (IdEditando is int id) Reselecionar(id);
    });

    private void Reselecionar(int id)
    {
        _silenciar = true;
        try { Selecionado = Produtos.FirstOrDefault(p => p.Id == id); }
        finally { _silenciar = false; }
    }

    // ================= Cadastro do produto =================

    [RelayCommand]
    private void Novo()
    {
        _silenciar = true;
        try { Selecionado = null; }
        finally { _silenciar = false; }

        IdEditando = null;
        NomeEditando = "";
        Nome = "";
        CodigoBarras = "";
        Referencia = "";
        PrecoVenda = "";
        PrecoAtacado = "";
        PrecoCusto = "";
        EstoqueMinimo = "0";
        EstoqueInicial = "0";
        ProdutoAtivo = true;
        EstoqueTotalEditando = 0;
        NovoTamanho = "";
        QuantidadeTexto = "";
        Motivo = "";
        Grade.Clear();
        Movimentacoes.Clear();
        TamanhoSelecionado = null;
        Mensagem = "";
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var (dados, estoqueInicial) = LerFormulario();

        if (IdEditando is int id)
        {
            await _produtos.EditarAsync(id, dados);
            await BuscarAsync();
            await AtualizarDetalhesAsync(id);
            MostrarSucesso("Produto atualizado.");
        }
        else
        {
            var novo = await _produtos.CadastrarAsync(dados, estoqueInicial);
            await BuscarAsync();
            await AbrirProdutoAsync(novo);
            MostrarSucesso("Produto cadastrado. Na aba \"Tamanhos e estoque\" você cadastra a grade de tamanhos.");
        }
    });

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;

        var estavaAtivo = ProdutoAtivo;
        if (estavaAtivo) await _produtos.DesativarAsync(id);
        else await _produtos.ReativarAsync(id);

        Novo();
        await BuscarAsync();
        MostrarSucesso(estavaAtivo ? "Produto desativado." : "Produto reativado.");
    });

    private (DadosProduto dados, int estoqueInicial) LerFormulario()
    {
        if (!Dinheiro.TentarConverter(PrecoVenda, out var venda))
            throw new RegraDeNegocioException("Preço de venda inválido. Exemplo: 12,50");

        long custo = 0;
        if (!string.IsNullOrWhiteSpace(PrecoCusto) && !Dinheiro.TentarConverter(PrecoCusto, out custo))
            throw new RegraDeNegocioException("Preço de custo inválido. Exemplo: 8,00");

        long atacado = 0; // vazio = sem preço de atacado (vale o de varejo)
        if (!string.IsNullOrWhiteSpace(PrecoAtacado) && !Dinheiro.TentarConverter(PrecoAtacado, out atacado))
            throw new RegraDeNegocioException("Preço de atacado inválido. Exemplo: 9,00");

        var minimo = LerInteiro(EstoqueMinimo, "Estoque mínimo");
        var inicial = EhNovo ? LerInteiro(EstoqueInicial, "Estoque inicial") : 0;

        return (new DadosProduto(Nome, CodigoBarras, venda, custo, minimo, Referencia, atacado), inicial);
    }

    private static int LerInteiro(string texto, string campo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor))
            throw new RegraDeNegocioException($"{campo} inválido. Use um número inteiro, sem sinal.");
        return valor;
    }

    /// <summary>Preenche o cadastro com o produto escolhido e carrega a grade e o histórico dele.</summary>
    private async Task AbrirProdutoAsync(Produto produto)
    {
        IdEditando = produto.Id;
        NomeEditando = produto.Nome;
        Nome = produto.Nome;
        CodigoBarras = produto.CodigoBarras ?? "";
        Referencia = produto.Referencia ?? "";
        PrecoVenda = Dinheiro.FormatarParaEdicao(produto.PrecoVendaCentavos);
        PrecoAtacado = produto.PrecoAtacadoCentavos > 0 ? Dinheiro.FormatarParaEdicao(produto.PrecoAtacadoCentavos) : "";
        PrecoCusto = Dinheiro.FormatarParaEdicao(produto.PrecoCustoCentavos);
        EstoqueMinimo = produto.EstoqueMinimo.ToString(CultureInfo.InvariantCulture);
        ProdutoAtivo = produto.Ativo;
        NovoTamanho = "";
        QuantidadeTexto = "";
        Motivo = "";
        Mensagem = "";

        await AtualizarDetalhesAsync(produto.Id, escolherTamanhoPadrao: true);
        Reselecionar(produto.Id);
    }

    // ================= Grade de tamanhos =================

    [RelayCommand]
    private Task AdicionarTamanhoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id)
            throw new RegraDeNegocioException("Salve o produto antes de cadastrar os tamanhos.");

        var criado = await _produtos.AdicionarTamanhoAsync(id, NovoTamanho);
        NovoTamanho = "";
        await AtualizarDetalhesAsync(id, nomeParaSelecionar: criado.Tamanho);
        MostrarSucesso($"Tamanho {criado.Tamanho} adicionado. Agora lance o estoque dele em \"Ajuste de estoque\".");
    });

    [RelayCommand]
    private Task RemoverTamanhoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id || TamanhoSelecionado is not { EhSemTamanho: false } tamanho)
            throw new RegraDeNegocioException("Escolha, na lista, o tamanho que quer remover (\"Sem tamanho\" não pode sair).");

        await _produtos.RemoverTamanhoAsync(id, tamanho.Tamanho);
        await AtualizarDetalhesAsync(id);
        MostrarSucesso($"Tamanho {tamanho.Tamanho} removido.");
    });

    // ================= Ajuste de estoque =================

    /// <summary>
    /// Aplica o ajuste no tamanho escolhido. Na Correção o operador informa o que CONTOU; o sistema
    /// calcula a diferença. Todo ajuste vira uma linha no histórico, com o motivo.
    /// </summary>
    [RelayCommand]
    private Task AplicarAjusteAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id)
            throw new RegraDeNegocioException("Escolha um produto na lista.");
        if (TamanhoSelecionado is not { } tamanho)
            throw new RegraDeNegocioException("Escolha, na lista de tamanhos, qual tamanho será ajustado.");
        if (!int.TryParse(QuantidadeTexto, NumberStyles.None, CultureInfo.InvariantCulture, out var quantidade))
            throw new RegraDeNegocioException("Quantidade inválida. Use um número inteiro, sem sinal.");

        var tipo = TipoSelecionado.Tipo;
        var variacao = quantidade;
        if (tipo == TipoMovimentacao.Correcao)
        {
            variacao = quantidade - tamanho.Estoque;
            if (variacao == 0)
                throw new RegraDeNegocioException("A contagem é igual ao estoque atual. Nada a corrigir.");
        }

        var nomeTamanho = tamanho.Tamanho;
        var produto = await _estoque.AjustarAsync(id, tipo, variacao, Motivo, nomeTamanho);

        QuantidadeTexto = "";
        Motivo = "";
        await BuscarAsync();
        await AtualizarDetalhesAsync(id, nomeParaSelecionar: nomeTamanho);

        var agora = Grade.FirstOrDefault(t => t.Tamanho == nomeTamanho)?.Estoque ?? 0;
        var onde = nomeTamanho == Tamanhos.SemTamanho ? "" : $" (tamanho {nomeTamanho})";
        MostrarSucesso($"Estoque de \"{produto.Nome}\"{onde} atualizado: agora {agora}.");
    });


    // ================= Apoio =================

    /// <summary>Recarrega grade, total e histórico do produto aberto (sem mexer no que o operador digitou no cadastro).</summary>
    private async Task AtualizarDetalhesAsync(int produtoId, string? nomeParaSelecionar = null, bool escolherTamanhoPadrao = false)
    {
        var anterior = nomeParaSelecionar ?? TamanhoSelecionado?.Tamanho;

        var grade = await _produtos.ListarTamanhosAsync(produtoId);
        var historico = await _estoque.ListarMovimentacoesAsync(produtoId);

        Grade.Clear();
        foreach (var t in grade) Grade.Add(t);
        Movimentacoes.Clear();
        foreach (var m in historico) Movimentacoes.Add(m);

        EstoqueTotalEditando = grade.Sum(t => t.Estoque);

        // mantém o tamanho que estava escolhido; se não houver, começa pelo primeiro tamanho de verdade (ou "Sem tamanho")
        TamanhoSelecionado = Grade.FirstOrDefault(t => t.Tamanho == anterior && !escolherTamanhoPadrao)
                             ?? Grade.FirstOrDefault(t => !t.EhSemTamanho)
                             ?? Grade.FirstOrDefault();
    }
}
