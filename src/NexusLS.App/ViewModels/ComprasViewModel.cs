using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da compra em montagem: o operador troca o tamanho, a quantidade e o custo.</summary>
public partial class ItemCompraViewModel : ObservableObject
{
    private readonly Action _mudou;

    public ItemCompraViewModel(Produto produto, IReadOnlyList<string> tamanhos, Action mudou)
    {
        ProdutoId = produto.Id;
        Nome = produto.Nome;
        Tamanhos = tamanhos;
        _tamanho = tamanhos[0];
        _custoTexto = Dinheiro.FormatarParaEdicao(produto.PrecoCustoCentavos);
        _mudou = mudou;
    }

    public int ProdutoId { get; }
    public string Nome { get; }
    public IReadOnlyList<string> Tamanhos { get; }
    public bool TemVariosTamanhos => Tamanhos.Count > 1;

    [ObservableProperty] private string _tamanho;
    [ObservableProperty] private string _quantidadeTexto = "1";
    [ObservableProperty] private string _custoTexto;

    public int Quantidade => int.TryParse(QuantidadeTexto, out var q) && q > 0 ? q : 0;
    public long? CustoCentavos => Dinheiro.TentarConverter(CustoTexto, out var c) ? c : null;
    public long TotalCentavos => Quantidade * (CustoCentavos ?? 0);
    public string TotalTexto => Dinheiro.Formatar(TotalCentavos);

    partial void OnTamanhoChanged(string value) => _mudou();
    partial void OnQuantidadeTextoChanged(string value) { OnPropertyChanged(nameof(TotalTexto)); _mudou(); }
    partial void OnCustoTextoChanged(string value) { OnPropertyChanged(nameof(TotalTexto)); _mudou(); }
}

public record LinhaCompra(Compra Compra)
{
    private static readonly CultureInfo PtBr = new("pt-BR");
    public int Numero => Compra.Id;
    public string Data => Compra.DataHora.ToString("dd/MM/yy HH:mm", PtBr);
    public string Fornecedor => Compra.Fornecedor?.Nome ?? "—";
    public string Nota => Compra.NumeroNota ?? "";
    public string Total => Dinheiro.Formatar(Compra.TotalCentavos);
    public string Situacao => Compra.Cancelada ? "Cancelada" : "Registrada";
}

/// <summary>
/// Tela "Compras": monta a compra (fornecedor, produtos, quantidades, custos e como pagar) e registra tudo de uma vez:
/// entrada de estoque, novo custo e contas a pagar. O histórico fica embaixo; compra cancelada devolve o estoque.
/// </summary>
public partial class ComprasViewModel : ViewModelBase
{
    private readonly CompraService _compras;
    private readonly ProdutoService _produtos;
    private readonly FornecedorService _fornecedores;
    private readonly Func<DateTime> _agora;
    private int _versaoBusca;

    public ComprasViewModel(CompraService compras, ProdutoService produtos, FornecedorService fornecedores, Func<DateTime>? agora = null)
    {
        _compras = compras;
        _produtos = produtos;
        _fornecedores = fornecedores;
        _agora = agora ?? (() => DateTime.Now);
        var hoje = _agora().Date;
        _de = hoje.AddDays(-30);
        _ate = hoje;
        _primeiroVencimento = hoje.AddDays(30);
        _formaEscolhida = Formas[0];
        Carregamento = IniciarAsync();
    }

    public Task Carregamento { get; }

    public Func<string, string?> PedirMotivo { get; set; } = _ => null;
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    public IReadOnlyList<OpcaoForma> Formas { get; } = SelecaoPagamento.Formas
        .Where(f => f != FormaPagamento.APrazo).Select(f => new OpcaoForma(SelecaoPagamento.Nome(f), f)).ToList();

    public ObservableCollection<Fornecedor> Fornecedores { get; } = new();
    public ObservableCollection<Produto> Resultados { get; } = new();
    public ObservableCollection<ItemCompraViewModel> Itens { get; } = new();
    public ObservableCollection<LinhaCompra> Historico { get; } = new();
    public ObservableCollection<string> Detalhe { get; } = new();

    // ----- Compra em montagem -----
    [ObservableProperty] private Fornecedor? _fornecedor;
    [ObservableProperty] private string _numeroNota = "";
    [ObservableProperty] private string _observacao = "";
    [ObservableProperty] private string _buscaProduto = "";
    [ObservableProperty] private Produto? _resultadoSelecionado;
    [ObservableProperty] private bool _atualizarCusto = true;
    [ObservableProperty] private bool _gerarContas = true;
    [ObservableProperty] private string _parcelasTexto = "1";
    [ObservableProperty] private DateTime? _primeiroVencimento;
    [ObservableProperty] private bool _jaPago;
    [ObservableProperty] private OpcaoForma _formaEscolhida;
    [ObservableProperty] private string _totalTexto = "R$ 0,00";
    [ObservableProperty] private string _resumoItens = "Nenhum item";

    // ----- Histórico -----
    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;
    [ObservableProperty] private LinhaCompra? _selecionada;

    public bool PodeCancelar => Selecionada is { Compra.Cancelada: false };
    public bool TemItens => Itens.Count > 0;

    partial void OnBuscaProdutoChanged(string value) => _ = BuscarProdutosAsync();
    partial void OnDeChanged(DateTime? value) => _ = CarregarHistoricoAsync();
    partial void OnAteChanged(DateTime? value) => _ = CarregarHistoricoAsync();
    partial void OnSelecionadaChanged(LinhaCompra? value)
    {
        OnPropertyChanged(nameof(PodeCancelar));
        Detalhe.Clear();
        if (value is null) return;
        foreach (var i in value.Compra.Itens)
            Detalhe.Add($"{i.Quantidade} × {i.NomeProduto}{(i.Tamanho == Tamanhos.SemTamanho ? "" : " (" + i.Tamanho + ")")} a {Dinheiro.Formatar(i.CustoUnitarioCentavos)} = {Dinheiro.Formatar(i.TotalCentavos)}");
        if (value.Compra.Cancelada) Detalhe.Add($"Cancelada: {value.Compra.MotivoCancelamento}");
    }

    private async Task IniciarAsync()
    {
        await ExecutarAsync(async () =>
        {
            Fornecedores.Clear();
            foreach (var f in await _fornecedores.BuscarAsync()) Fornecedores.Add(f);
            await CarregarHistoricoAsync();
        });
    }

    // ================= buscar e adicionar produtos =================

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
        // Enter na busca: se ainda não chegou o resultado da digitação, busca agora e usa o primeiro.
        if (ResultadoSelecionado is null && !string.IsNullOrWhiteSpace(BuscaProduto))
        {
            var lista = await _produtos.BuscarAsync(BuscaProduto);
            ResultadoSelecionado = lista.FirstOrDefault();
        }
        if (ResultadoSelecionado is not { } produto)
        {
            MostrarErro("Não achei esse produto. Cadastre-o primeiro em Estoque da loja.");
            return;
        }

        var tamanhos = (await _produtos.ListarTamanhosAsync(produto.Id)).Select(t => t.Tamanho).ToList();
        if (tamanhos.Count == 0) tamanhos.Add(Tamanhos.SemTamanho);
        // Produto com grade: o "Sem tamanho" não recebe compra; começa no primeiro tamanho real.
        var reais = tamanhos.Where(t => t != Tamanhos.SemTamanho).ToList();
        var opcoes = reais.Count > 0 ? reais : tamanhos;

        var existente = Itens.FirstOrDefault(i => i.ProdutoId == produto.Id && i.Tamanho == opcoes[0]);
        if (existente is not null) existente.QuantidadeTexto = (existente.Quantidade + 1).ToString();
        else Itens.Add(new ItemCompraViewModel(produto, opcoes, Recalcular));

        BuscaProduto = "";
        Resultados.Clear();
        Mensagem = "";
        Recalcular();
    });

    [RelayCommand]
    private void RemoverItem(ItemCompraViewModel? item)
    {
        if (item is null) return;
        Itens.Remove(item);
        Recalcular();
    }

    private void Recalcular()
    {
        var total = Itens.Sum(i => i.TotalCentavos);
        TotalTexto = Dinheiro.Formatar(total);
        var unidades = Itens.Sum(i => i.Quantidade);
        ResumoItens = Itens.Count == 0 ? "Nenhum item" : $"{Itens.Count} item(ns), {unidades} unidade(s)";
        OnPropertyChanged(nameof(TemItens));
    }

    // ================= registrar =================

    [RelayCommand]
    private Task RegistrarAsync() => ExecutarAsync(async () =>
    {
        var itens = new List<ItemCompraDados>();
        foreach (var i in Itens)
        {
            if (i.Quantidade <= 0) throw new RegraDeNegocioException($"Informe a quantidade de \"{i.Nome}\" (número inteiro maior que zero).");
            if (i.CustoCentavos is not long custo) throw new RegraDeNegocioException($"Informe o custo de \"{i.Nome}\". Exemplo: 25,00");
            itens.Add(new ItemCompraDados(i.ProdutoId, i.Quantidade, custo, i.Tamanho));
        }

        PagamentoCompra? pagamento = null;
        if (GerarContas)
        {
            if (!int.TryParse(ParcelasTexto, out var parcelas)) throw new RegraDeNegocioException("Informe em quantas parcelas será pago (1 para à vista).");
            if (PrimeiroVencimento is not DateTime venc) throw new RegraDeNegocioException("Informe o vencimento da (primeira) parcela.");
            pagamento = new PagamentoCompra(parcelas, venc, JaPago, JaPago ? FormaEscolhida.Forma : null);
        }

        var compra = await _compras.RegistrarAsync(
            new DadosCompra(itens, Fornecedor?.Id, NumeroNota, Observacao, AtualizarCusto, pagamento));

        Limpar();
        await CarregarHistoricoAsync();
        MostrarSucesso($"Compra nº {compra.Id} registrada: {Dinheiro.Formatar(compra.TotalCentavos)}. O estoque já foi atualizado.");
    });

    [RelayCommand]
    private void Limpar()
    {
        Itens.Clear();
        NumeroNota = Observacao = BuscaProduto = "";
        Fornecedor = null;
        ParcelasTexto = "1";
        JaPago = false;
        PrimeiroVencimento = _agora().Date.AddDays(30);
        Resultados.Clear();
        Recalcular();
    }

    // ================= histórico =================

    [RelayCommand]
    private Task CarregarHistoricoAsync() => ExecutarAsync(async () =>
    {
        if (De is not DateTime de || Ate is not DateTime ate) return;
        var lista = await _compras.ListarAsync(de, ate);
        var numero = Selecionada?.Numero;
        Historico.Clear();
        foreach (var c in lista) Historico.Add(new LinhaCompra(c));
        Selecionada = Historico.FirstOrDefault(l => l.Numero == numero);
    });

    [RelayCommand]
    private Task CancelarCompraAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } s || !PodeCancelar) return;
        var motivo = PedirMotivo($"Cancelar a compra nº {s.Numero}? O estoque dos itens volta ao que era e as contas em aberto são canceladas.");
        if (motivo is null) return;
        await _compras.CancelarAsync(s.Numero, motivo);
        await CarregarHistoricoAsync();
        MostrarSucesso($"Compra nº {s.Numero} cancelada (continua no histórico).");
    });
}
