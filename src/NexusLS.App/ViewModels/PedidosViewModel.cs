using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Um cartão do quadro, com os textos prontos.</summary>
public record CartaoPedido(Pedido Pedido)
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    public int Id => Pedido.Id;
    public string Numero => Pedido.Numero;
    public string Cliente => Pedido.ClienteNome;
    public string Total => Dinheiro.Formatar(Pedido.TotalCentavos);
    public string Resumo
    {
        get
        {
            var pecas = Pedido.Itens.Sum(i => i.Quantidade);
            return $"{pecas} peça(s) · {RegrasPedido.Nome(Pedido.Origem)}";
        }
    }
    public string Quando => Pedido.CriadoEm.ToString("dd/MM HH:mm", PtBr);
}

public partial class ColunaPedido : ObservableObject
{
    public ColunaPedido(StatusPedido status) { Status = status; Titulo = RegrasPedido.Nome(status); }

    public StatusPedido Status { get; }
    public string Titulo { get; }
    public ObservableCollection<CartaoPedido> Cartoes { get; } = new();
    public string TituloComContagem => $"{Titulo} ({Cartoes.Count})";

    /// <summary>Avisa a tela que o número de cartões (no título da coluna) mudou.</summary>
    public void AtualizarTitulo() => OnPropertyChanged(nameof(TituloComContagem));
}

/// <summary>Uma linha do pedido em montagem.</summary>
public partial class ItemPedidoViewModel : ObservableObject
{
    private readonly Action _mudou;

    public ItemPedidoViewModel(int? produtoId, string nome, long precoCentavos, IReadOnlyList<string> tamanhos, Action mudou)
    {
        ProdutoId = produtoId;
        _nome = nome;
        Tamanhos = tamanhos;
        _tamanho = tamanhos[0];
        _precoTexto = Dinheiro.FormatarParaEdicao(precoCentavos);
        _mudou = mudou;
    }

    public int? ProdutoId { get; }
    public IReadOnlyList<string> Tamanhos { get; }
    public bool TemVariosTamanhos => Tamanhos.Count > 1;
    public bool NomeEditavel => ProdutoId is null;

    [ObservableProperty] private string _nome;
    [ObservableProperty] private string _tamanho;
    [ObservableProperty] private string _quantidadeTexto = "1";
    [ObservableProperty] private string _precoTexto;

    public int Quantidade => int.TryParse(QuantidadeTexto, out var q) && q > 0 ? q : 0;
    public long? PrecoCentavos => Dinheiro.TentarConverter(PrecoTexto, out var c) ? c : null;
    public long TotalCentavos => Quantidade * (PrecoCentavos ?? 0);
    public string TotalTexto => Dinheiro.Formatar(TotalCentavos);

    partial void OnQuantidadeTextoChanged(string value) { OnPropertyChanged(nameof(TotalTexto)); _mudou(); }
    partial void OnPrecoTextoChanged(string value) { OnPropertyChanged(nameof(TotalTexto)); _mudou(); }
}

public record OpcaoOrigem(OrigemPedido Origem, string Nome);

/// <summary>
/// Tela "Pedidos": quadro por etapa (Novo → Em separação → Separado → Pronto → Entregue), detalhe do pedido escolhido,
/// avançar/voltar/cancelar, lista de separação e criação de pedidos (WhatsApp, catálogo, encomenda, balcão).
/// Venda a prazo feita no PDV já cai aqui sozinha.
/// </summary>
public partial class PedidosViewModel : ViewModelBase
{
    private readonly PedidoService _pedidos;
    private readonly ProdutoService _produtos;
    private readonly ClienteService _clientes;
    private readonly CupomService? _cupons;
    private int _versao;
    private int _versaoBusca;
    private int? _idParaSelecionar;

    public PedidosViewModel(PedidoService pedidos, ProdutoService produtos, ClienteService clientes, CupomService? cupons = null)
    {
        _pedidos = pedidos;
        _produtos = produtos;
        _clientes = clientes;
        _cupons = cupons;
        _origemSelecionada = Origens[1];
        foreach (var s in RegrasPedido.Fluxo) Colunas.Add(new ColunaPedido(s));
        Carregamento = CarregarAsync();
    }

    public Task Carregamento { get; }

    public Func<string, string?> PedirMotivo { get; set; } = _ => null;

    public ObservableCollection<ColunaPedido> Colunas { get; } = new();
    public ColunaPedido Cancelados { get; } = new(StatusPedido.Cancelado);
    public ObservableCollection<string> DetalheItens { get; } = new();
    public ObservableCollection<string> DetalheHistorico { get; } = new();

    public IReadOnlyList<OpcaoOrigem> Origens { get; } = new[] { OrigemPedido.Balcao, OrigemPedido.WhatsApp, OrigemPedido.Catalogo, OrigemPedido.Encomenda }
        .Select(o => new OpcaoOrigem(o, RegrasPedido.Nome(o))).ToList();

    // ----- Quadro -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarCancelados;
    [ObservableProperty] private CartaoPedido? _selecionado;

    partial void OnBuscaChanged(string value) => _ = CarregarAsync();
    partial void OnMostrarCanceladosChanged(bool value) => _ = CarregarAsync();

    // ----- Detalhe do pedido escolhido -----
    [ObservableProperty] private string _detalheTitulo = "";
    [ObservableProperty] private string _detalheCliente = "";
    [ObservableProperty] private string _detalheTotal = "";
    [ObservableProperty] private string _detalheObservacao = "";
    [ObservableProperty] private string _textoAvancar = "Avançar";
    [ObservableProperty] private string _textoVoltar = "Voltar";

    public bool TemSelecao => Selecionado is not null;
    public bool PodeAvancar => Selecionado is { } s && RegrasPedido.Proximo(s.Pedido.Status) is not null;
    public bool PodeVoltar => Selecionado is { } s && RegrasPedido.Anterior(s.Pedido.Status) is not null;
    public bool PodeCancelar => Selecionado is { } s && RegrasPedido.PodeCancelar(s.Pedido.Status);

    partial void OnSelecionadoChanged(CartaoPedido? value)
    {
        OnPropertyChanged(nameof(TemSelecao));
        OnPropertyChanged(nameof(PodeAvancar));
        OnPropertyChanged(nameof(PodeVoltar));
        OnPropertyChanged(nameof(PodeCancelar));
        _ = MostrarDetalheAsync(value);
    }

    private async Task MostrarDetalheAsync(CartaoPedido? cartao)
    {
        DetalheItens.Clear();
        DetalheHistorico.Clear();
        if (cartao is null) { DetalheTitulo = DetalheCliente = DetalheTotal = DetalheObservacao = ""; return; }

        Pedido p;
        try { p = await _pedidos.ObterAsync(cartao.Id); }
        catch (RegraDeNegocioException) { return; }
        if (Selecionado?.Id != cartao.Id) return; // a seleção mudou enquanto carregava

        DetalheTitulo = $"{p.Numero} · {RegrasPedido.Nome(p.Status)}";
        DetalheCliente = p.ClienteNome + (string.IsNullOrWhiteSpace(p.ClienteTelefone) ? "" : $" · {p.ClienteTelefone}");
        DetalheTotal = Dinheiro.Formatar(p.TotalCentavos);
        DetalheObservacao = p.Status == StatusPedido.Cancelado && !string.IsNullOrWhiteSpace(p.MotivoCancelamento)
            ? $"Cancelado: {p.MotivoCancelamento}" : p.Observacao ?? "";
        foreach (var i in p.Itens)
            DetalheItens.Add($"{i.Quantidade} × {i.Nome}{(i.Tamanho == Tamanhos.SemTamanho ? "" : " (" + i.Tamanho + ")")}  —  {Dinheiro.Formatar(i.TotalCentavos)}");
        foreach (var h in p.Historico.OrderBy(x => x.DataHora).ThenBy(x => x.Id))
            DetalheHistorico.Add($"{h.DataHora:dd/MM HH:mm} · {RegrasPedido.Nome(h.Status)}{(string.IsNullOrWhiteSpace(h.Operador) ? "" : " · " + h.Operador)}");

        var proximo = RegrasPedido.Proximo(p.Status);
        var anterior = RegrasPedido.Anterior(p.Status);
        TextoAvancar = proximo is null ? "Avançar" : $"Avançar para {RegrasPedido.Nome(proximo.Value)}";
        TextoVoltar = anterior is null ? "Voltar" : $"Voltar para {RegrasPedido.Nome(anterior.Value)}";
    }

    [RelayCommand]
    private void Selecionar(CartaoPedido? cartao) => Selecionado = cartao;

    // ================= quadro =================

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        var lista = await _pedidos.ListarAsync(new FiltroPedidos(Texto: Busca));
        if (versao != _versao) return;

        var idSelecionado = _idParaSelecionar ?? Selecionado?.Id;
        _idParaSelecionar = null;
        foreach (var c in Colunas) c.Cartoes.Clear();
        Cancelados.Cartoes.Clear();
        foreach (var p in lista)
        {
            var cartao = new CartaoPedido(p);
            if (p.Status == StatusPedido.Cancelado) { if (MostrarCancelados) Cancelados.Cartoes.Add(cartao); }
            else Colunas.First(c => c.Status == p.Status).Cartoes.Add(cartao);
        }
        foreach (var c in Colunas) c.AtualizarTitulo();
        Cancelados.AtualizarTitulo();
        Selecionado = Colunas.SelectMany(c => c.Cartoes).Concat(Cancelados.Cartoes).FirstOrDefault(c => c.Id == idSelecionado);
    });

    [RelayCommand]
    private Task AvancarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { } s || !PodeAvancar) return;
        var depois = await _pedidos.AvancarAsync(s.Id);
        _idParaSelecionar = s.Id;
        await CarregarAsync();
        MostrarSucesso($"Pedido {depois.Numero}: {RegrasPedido.Nome(depois.Status)}.");
    });

    [RelayCommand]
    private Task VoltarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { } s || !PodeVoltar) return;
        var depois = await _pedidos.VoltarAsync(s.Id);
        _idParaSelecionar = s.Id;
        await CarregarAsync();
        MostrarSucesso($"Pedido {depois.Numero}: {RegrasPedido.Nome(depois.Status)}.");
    });

    [RelayCommand]
    private Task CancelarPedidoAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { } s || !PodeCancelar) return;
        var motivo = PedirMotivo($"Por que cancelar o pedido {s.Numero}?");
        if (motivo is null) return;
        await _pedidos.CancelarAsync(s.Id, motivo);
        _idParaSelecionar = s.Id;
        MostrarCancelados = true; // para o pedido cancelado continuar à vista
        await CarregarAsync();
        MostrarSucesso($"Pedido {s.Numero} cancelado (continua no histórico).");
    });

    [RelayCommand]
    private Task ImprimirListaAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { } s) return;
        if (_cupons is null) throw new RegraDeNegocioException("A impressão não está disponível.");
        await _cupons.ImprimirListaSeparacaoAsync(s.Id);
        MostrarSucesso($"Lista de separação do pedido {s.Numero} enviada para a impressora.");
    });

    // ================= novo pedido =================

    [ObservableProperty] private bool _formAberto;
    [ObservableProperty] private OpcaoOrigem _origemSelecionada;
    [ObservableProperty] private Cliente? _clienteCadastrado;
    [ObservableProperty] private string _clienteNome = "";
    [ObservableProperty] private string _clienteTelefone = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _buscaProduto = "";
    [ObservableProperty] private Produto? _resultadoSelecionado;
    [ObservableProperty] private string _totalForm = "R$ 0,00";

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<Produto> Resultados { get; } = new();
    public ObservableCollection<ItemPedidoViewModel> ItensForm { get; } = new();
    public bool TemItensForm => ItensForm.Count > 0;
    public bool SemClienteCadastrado => ClienteCadastrado is null;

    partial void OnBuscaProdutoChanged(string value) => _ = BuscarProdutosAsync();
    partial void OnClienteCadastradoChanged(Cliente? value) => OnPropertyChanged(nameof(SemClienteCadastrado));

    [RelayCommand]
    private Task NovoPedidoAsync() => ExecutarAsync(async () =>
    {
        LimparForm();
        Clientes.Clear();
        foreach (var c in await _clientes.BuscarAsync(null)) Clientes.Add(c);
        FormAberto = true;
        Mensagem = "";
    });

    [RelayCommand]
    private void FecharForm()
    {
        FormAberto = false;
        LimparForm();
    }

    private void LimparForm()
    {
        ItensForm.Clear();
        ClienteCadastrado = null;
        ClienteNome = ClienteTelefone = Observacao = BuscaProduto = "";
        OrigemSelecionada = Origens[1];
        Resultados.Clear();
        RecalcularForm();
    }

    private async Task BuscarProdutosAsync()
    {
        var versao = ++_versaoBusca;
        if (string.IsNullOrWhiteSpace(BuscaProduto)) { Resultados.Clear(); return; }
        var lista = await _produtos.BuscarAsync(BuscaProduto);
        if (versao != _versaoBusca) return;
        Resultados.Clear();
        foreach (var p in lista.Take(8)) Resultados.Add(p);
        ResultadoSelecionado = Resultados.FirstOrDefault();
    }

    [RelayCommand]
    private Task AdicionarProdutoAsync() => ExecutarAsync(async () =>
    {
        if (ResultadoSelecionado is null && !string.IsNullOrWhiteSpace(BuscaProduto))
            ResultadoSelecionado = (await _produtos.BuscarAsync(BuscaProduto)).FirstOrDefault();
        if (ResultadoSelecionado is not { } produto)
        {
            MostrarErro("Não achei esse produto. Use \"Item sem cadastro\" para um serviço ou encomenda.");
            return;
        }

        var tamanhos = (await _produtos.ListarTamanhosAsync(produto.Id)).Select(t => t.Tamanho).ToList();
        var reais = tamanhos.Where(t => t != Tamanhos.SemTamanho).ToList();
        var opcoes = reais.Count > 0 ? reais : new List<string> { Tamanhos.SemTamanho };

        var existente = ItensForm.FirstOrDefault(i => i.ProdutoId == produto.Id && i.Tamanho == opcoes[0]);
        if (existente is not null) existente.QuantidadeTexto = (existente.Quantidade + 1).ToString();
        else ItensForm.Add(new ItemPedidoViewModel(produto.Id, produto.Nome, produto.PrecoVendaCentavos, opcoes, RecalcularForm));

        BuscaProduto = "";
        Resultados.Clear();
        Mensagem = "";
        RecalcularForm();
    });

    [RelayCommand]
    private void AdicionarItemLivre()
    {
        ItensForm.Add(new ItemPedidoViewModel(null, "", 0, new[] { Tamanhos.SemTamanho }, RecalcularForm));
        RecalcularForm();
    }

    [RelayCommand]
    private void RemoverItem(ItemPedidoViewModel? item)
    {
        if (item is null) return;
        ItensForm.Remove(item);
        RecalcularForm();
    }

    private void RecalcularForm()
    {
        TotalForm = Dinheiro.Formatar(ItensForm.Sum(i => i.TotalCentavos));
        OnPropertyChanged(nameof(TemItensForm));
    }

    [RelayCommand]
    private Task SalvarPedidoAsync() => ExecutarAsync(async () =>
    {
        var itens = new List<ItemPedidoDados>();
        foreach (var i in ItensForm)
        {
            if (i.Quantidade <= 0) throw new RegraDeNegocioException($"Informe a quantidade de \"{i.Nome}\" (número inteiro maior que zero).");
            if (i.PrecoCentavos is not long preco) throw new RegraDeNegocioException($"Informe o preço de \"{i.Nome}\". Exemplo: 49,90");
            itens.Add(new ItemPedidoDados(i.ProdutoId, i.Quantidade, i.Nome, preco, i.Tamanho));
        }

        var pedido = await _pedidos.CriarAsync(new DadosPedido(
            OrigemSelecionada.Origem, itens, ClienteCadastrado?.Id, ClienteNome, ClienteTelefone, Observacao));

        FormAberto = false;
        LimparForm();
        _idParaSelecionar = pedido.Id;
        await CarregarAsync();
        MostrarSucesso($"Pedido {pedido.Numero} criado.");
    });
}
