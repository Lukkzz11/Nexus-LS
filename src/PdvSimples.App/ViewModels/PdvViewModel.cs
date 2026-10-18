using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdvSimples.Core.Models;
using PdvSimples.Core.Services;

namespace PdvSimples.App.ViewModels;

/// <summary>
/// Frente de caixa. Toda a regra de venda de verdade (preço, estoque, troco) mora no VendaService;
/// aqui fica só o "montar o carrinho" e o que a tela precisa mostrar.
/// </summary>
public partial class PdvViewModel : ViewModelBase
{
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;

    public PdvViewModel(ProdutoService produtos, VendaService vendas)
    {
        _produtos = produtos;
        _vendas = vendas;
        Resultados.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TemResultados));
    }

    public ObservableCollection<ItemCarrinhoViewModel> Itens { get; } = new();
    public ObservableCollection<Produto> Resultados { get; } = new();

    /// <summary>
    /// A tela define aqui como perguntar "tem certeza?" ao operador. Assim o ViewModel não
    /// depende de MessageBox (que é coisa de janela) e continua simples de testar.
    /// </summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    /// <summary>Evento: o ViewModel pede "volte o cursor para o campo de busca" e a tela atende.</summary>
    public event Action? FocoNaBuscaSolicitado;

    public event Action? FocoNoValorRecebidoSolicitado;

    public bool CarrinhoVazio => Itens.Count == 0;

    // ----- Busca -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private Produto? _resultadoSelecionado;
    [ObservableProperty] private ItemCarrinhoViewModel? _itemSelecionado;

    // ----- Pagamento -----
    [ObservableProperty] private string _descontoTexto = "";
    [ObservableProperty] private string _valorRecebidoTexto = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PagaDinheiro))]
    [NotifyPropertyChangedFor(nameof(PagaPix))]
    [NotifyPropertyChangedFor(nameof(PagaCartao))]
    [NotifyPropertyChangedFor(nameof(ExigeValorRecebido))]
    [NotifyPropertyChangedFor(nameof(TextoTroco))]
    private FormaPagamento _forma = FormaPagamento.Dinheiro;

    public bool TemResultados => Resultados.Count > 0;
    public bool ExigeValorRecebido => Forma == FormaPagamento.Dinheiro;

    // Os três RadioButtons da tela ligam nestas propriedades.
    public bool PagaDinheiro { get => Forma == FormaPagamento.Dinheiro; set { if (value) Forma = FormaPagamento.Dinheiro; } }
    public bool PagaPix { get => Forma == FormaPagamento.Pix; set { if (value) Forma = FormaPagamento.Pix; } }
    public bool PagaCartao { get => Forma == FormaPagamento.Cartao; set { if (value) Forma = FormaPagamento.Cartao; } }

    // ----- Valores calculados (a tela só exibe) -----
    public long SubtotalCentavos => Itens.Sum(i => i.TotalCentavos);

    private long DescontoCentavos =>
        Dinheiro.TentarConverter(DescontoTexto, out var d) ? d : 0;

    public long TotalCentavos => Math.Max(0, SubtotalCentavos - DescontoCentavos);

    public string SubtotalTexto => Dinheiro.Formatar(SubtotalCentavos);
    public string TotalTexto => Dinheiro.Formatar(TotalCentavos);

    public string TextoTroco
    {
        get
        {
            if (Forma != FormaPagamento.Dinheiro) return "";
            if (string.IsNullOrWhiteSpace(ValorRecebidoTexto)) return "Sem troco (valor exato)";
            if (!Dinheiro.TentarConverter(ValorRecebidoTexto, out var recebido)) return "Valor recebido inválido";
            var diferenca = recebido - TotalCentavos;
            return diferenca >= 0
                ? $"Troco: {Dinheiro.Formatar(diferenca)}"
                : $"Faltam {Dinheiro.Formatar(-diferenca)}";
        }
    }

    partial void OnBuscaChanged(string value) => Resultados.Clear(); // digitou de novo: a lista antiga não vale mais

    partial void OnDescontoTextoChanged(string value) => AtualizarTotais();

    partial void OnValorRecebidoTextoChanged(string value) => OnPropertyChanged(nameof(TextoTroco));

    // ================= Comandos =================

    /// <summary>
    /// Enter no campo de busca. Aceita "3*789" (3 unidades do código 789).
    /// Código de barras exato adiciona direto; nome com um só resultado também; com vários, mostra a lista.
    /// </summary>
    [RelayCommand]
    private Task BuscarEAdicionarAsync() => ExecutarAsync(async () =>
    {
        var texto = Busca.Trim();
        if (texto.Length == 0) return;

        var quantidade = 1;
        var m = Regex.Match(texto, @"^(\d+)\*(.+)$");
        if (m.Success)
        {
            quantidade = int.TryParse(m.Groups[1].Value, out var q) ? q : 0;
            if (quantidade <= 0) throw new RegraDeNegocioException("A quantidade deve ser maior que zero.");
            texto = m.Groups[2].Value.Trim();
        }

        // Já existe uma lista aberta: Enter confirma o item destacado.
        if (TemResultados && ResultadoSelecionado is not null)
        {
            AdicionarAoCarrinho(ResultadoSelecionado, quantidade);
            return;
        }

        var porCodigo = await _produtos.ObterPorCodigoBarrasAsync(texto);
        if (porCodigo is not null)
        {
            AdicionarAoCarrinho(porCodigo, quantidade);
            return;
        }

        var achados = await _produtos.BuscarAsync(texto);
        if (achados.Count == 0)
        {
            MostrarErro($"Nenhum produto encontrado para \"{texto}\".");
        }
        else if (achados.Count == 1)
        {
            AdicionarAoCarrinho(achados[0], quantidade);
        }
        else
        {
            foreach (var p in achados) Resultados.Add(p);
            ResultadoSelecionado = Resultados[0];
            MostrarSucesso($"{achados.Count} produtos encontrados: use ↑ ↓ e Enter.");
        }
    });

    [RelayCommand]
    private void ProximoResultado() => MoverResultado(+1);

    [RelayCommand]
    private void ResultadoAnterior() => MoverResultado(-1);

    [RelayCommand]
    private void Aumentar(ItemCarrinhoViewModel? item)
    {
        if (item is not null) item.Quantidade++; // os limites são checados em AoMudarItem
    }

    [RelayCommand]
    private void Diminuir(ItemCarrinhoViewModel? item)
    {
        if (item is null) return;
        if (item.Quantidade <= 1) RemoverItem(item);
        else item.Quantidade--;
    }

    [RelayCommand]
    private void Remover(ItemCarrinhoViewModel? item)
    {
        if (item is not null) RemoverItem(item);
    }

    [RelayCommand]
    private void RemoverSelecionado()
    {
        if (ItemSelecionado is not null) RemoverItem(ItemSelecionado);
    }

    [RelayCommand]
    private void EscolherDinheiro()
    {
        Forma = FormaPagamento.Dinheiro;
        FocoNoValorRecebidoSolicitado?.Invoke(); // F6 já leva o cursor para digitar quanto o cliente deu
    }

    [RelayCommand] private void EscolherPix() => Forma = FormaPagamento.Pix;
    [RelayCommand] private void EscolherCartao() => Forma = FormaPagamento.Cartao;

    /// <summary>F10: grava a venda. O VendaService valida tudo de novo e faz venda + baixa numa transação.</summary>
    [RelayCommand]
    private Task FinalizarAsync() => ExecutarAsync(async () =>
    {
        if (Itens.Count == 0)
            throw new RegraDeNegocioException("O carrinho está vazio.");

        long desconto = 0;
        if (!string.IsNullOrWhiteSpace(DescontoTexto) && !Dinheiro.TentarConverter(DescontoTexto, out desconto))
            throw new RegraDeNegocioException("Desconto inválido. Exemplo: 5,00");

        long? recebido = null;
        if (Forma == FormaPagamento.Dinheiro)
        {
            // Campo vazio = cliente pagou o valor exato (sem troco), o caso mais comum.
            if (string.IsNullOrWhiteSpace(ValorRecebidoTexto))
                recebido = Math.Max(0, SubtotalCentavos - desconto);
            else if (Dinheiro.TentarConverter(ValorRecebidoTexto, out var valor))
                recebido = valor;
            else
                throw new RegraDeNegocioException("Valor recebido inválido. Exemplo: 50,00");
        }

        var pedido = new PedidoVenda(
            Itens.Select(i => new ItemCarrinho(i.ProdutoId, i.Quantidade)).ToList(),
            desconto, Forma, recebido);

        var venda = await _vendas.FinalizarAsync(pedido);

        LimparCarrinho();
        var resumo = $"Venda #{venda.Id} finalizada: {Dinheiro.Formatar(venda.TotalCentavos)}";
        if (venda.TrocoCentavos > 0) resumo += $" · Troco: {Dinheiro.Formatar(venda.TrocoCentavos)}";
        MostrarSucesso(resumo);
        FocoNaBuscaSolicitado?.Invoke();
    });

    /// <summary>Esc: fecha a lista de resultados; se não há lista, cancela a venda (pedindo confirmação).</summary>
    [RelayCommand]
    private void Cancelar()
    {
        if (TemResultados)
        {
            Resultados.Clear();
            return;
        }

        if (Itens.Count > 0 && !Confirmar("Cancelar a venda atual e esvaziar o carrinho?")) return;

        LimparCarrinho();
        Busca = "";
        Mensagem = "";
        FocoNaBuscaSolicitado?.Invoke();
    }

    // ================= Apoio =================

    private void AdicionarAoCarrinho(Produto produto, int quantidade)
    {
        var existente = Itens.FirstOrDefault(i => i.ProdutoId == produto.Id);
        var noCarrinho = existente?.Quantidade ?? 0;

        if (produto.EstoqueAtual <= 0)
            throw new RegraDeNegocioException($"\"{produto.Nome}\" está sem estoque.");
        if (noCarrinho + quantidade > produto.EstoqueAtual)
            throw new RegraDeNegocioException(
                $"Estoque insuficiente: \"{produto.Nome}\" tem {produto.EstoqueAtual} unidade(s) (no carrinho: {noCarrinho}).");

        if (existente is not null)
        {
            existente.EstoqueDisponivel = produto.EstoqueAtual;
            existente.Quantidade += quantidade;
            ItemSelecionado = existente;
        }
        else
        {
            var item = new ItemCarrinhoViewModel(produto, quantidade);
            item.PropertyChanged += AoMudarItem;
            Itens.Add(item);
            ItemSelecionado = item;
        }

        Resultados.Clear();
        Busca = "";
        Mensagem = "";
        AtualizarTotais();
    }

    /// <summary>Quando o operador edita a quantidade na tela: não deixa passar do estoque nem ficar abaixo de 1.</summary>
    private void AoMudarItem(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ItemCarrinhoViewModel.Quantidade) || sender is not ItemCarrinhoViewModel item) return;

        if (item.Quantidade < 1)
        {
            item.Quantidade = 1;
            MostrarErro("A quantidade mínima é 1. Para tirar o item, use Remover (Del).");
        }
        else if (item.Quantidade > item.EstoqueDisponivel)
        {
            item.Quantidade = item.EstoqueDisponivel;
            MostrarErro($"Estoque insuficiente: \"{item.Nome}\" tem apenas {item.EstoqueDisponivel} unidade(s).");
        }

        AtualizarTotais();
    }

    private void RemoverItem(ItemCarrinhoViewModel item)
    {
        item.PropertyChanged -= AoMudarItem;
        Itens.Remove(item);
        AtualizarTotais();
    }

    private void LimparCarrinho()
    {
        foreach (var item in Itens) item.PropertyChanged -= AoMudarItem;
        Itens.Clear();
        ItemSelecionado = null;
        DescontoTexto = "";
        ValorRecebidoTexto = "";
        Forma = FormaPagamento.Dinheiro;
        AtualizarTotais();
    }

    private void MoverResultado(int passo)
    {
        if (Resultados.Count == 0) return;
        var atual = ResultadoSelecionado is null ? 0 : Resultados.IndexOf(ResultadoSelecionado);
        var novo = Math.Clamp(atual + passo, 0, Resultados.Count - 1);
        ResultadoSelecionado = Resultados[novo];
    }

    /// <summary>Avisa a tela que os valores calculados mudaram (eles não são [ObservableProperty], então é manual).</summary>
    private void AtualizarTotais()
    {
        OnPropertyChanged(nameof(CarrinhoVazio));
        OnPropertyChanged(nameof(SubtotalTexto));
        OnPropertyChanged(nameof(TotalCentavos));
        OnPropertyChanged(nameof(TotalTexto));
        OnPropertyChanged(nameof(TextoTroco));
    }
}
