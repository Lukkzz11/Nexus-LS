using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Frente de caixa. O carrinho (quantidades, estoque por tamanho, preço editado) é a classe Carrinho do Core;
/// a regra de venda de verdade (preço, estoque, troco) mora no VendaService. Aqui fica só o que a tela mostra.
/// </summary>
public partial class PdvViewModel : ViewModelBase, IEditorCarrinho
{
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly RascunhoService _rascunho;
    private Carrinho _carrinho = new();

    public PdvViewModel(ProdutoService produtos, VendaService vendas, RascunhoService rascunho)
    {
        _produtos = produtos;
        _vendas = vendas;
        _rascunho = rascunho;
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

    public bool CarrinhoVazio => _carrinho.Vazio;

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
    public bool PagaCartao { get => Forma == FormaPagamento.CartaoDebito; set { if (value) Forma = FormaPagamento.CartaoDebito; } }

    // ----- Valores calculados (a tela só exibe) -----
    public long SubtotalCentavos => _carrinho.SubtotalCentavos;

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

    // ================= Rascunho =================

    /// <summary>
    /// Chamado ao abrir o programa: se a venda anterior ficou no meio (o app foi fechado com itens no
    /// carrinho), ela volta, já conferida com o estoque e os produtos de agora.
    /// </summary>
    public Task RestaurarRascunhoAsync() => ExecutarAsync(async () =>
    {
        var restaurado = await _rascunho.RestaurarAsync();
        if (restaurado is null) return;

        _carrinho = restaurado.Carrinho;
        Itens.Clear();
        DescontoTexto = restaurado.DescontoTexto ?? "";
        Sincronizar();

        var texto = $"Venda em andamento restaurada ({_carrinho.QuantidadeTotal} unidade(s)).";
        if (restaurado.Avisos.Count > 0) texto += " " + string.Join(" ", restaurado.Avisos);
        if (restaurado.Avisos.Count > 0) MostrarErro(texto); else MostrarSucesso(texto);
    });

    private void SalvarRascunho()
    {
        try
        {
            _rascunho.Salvar(_carrinho, DescontoTexto, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha ao gravar o rascunho (disco cheio, pasta bloqueada) não pode atrapalhar a venda.
        }
    }

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
            await AdicionarAoCarrinhoAsync(ResultadoSelecionado, quantidade);
            return;
        }

        var porCodigo = await _produtos.ObterPorCodigoBarrasAsync(texto);
        if (porCodigo is not null)
        {
            await AdicionarAoCarrinhoAsync(porCodigo, quantidade);
            return;
        }

        var achados = await _produtos.BuscarAsync(texto);
        if (achados.Count == 0)
        {
            MostrarErro($"Nenhum produto encontrado para \"{texto}\".");
        }
        else if (achados.Count == 1)
        {
            await AdicionarAoCarrinhoAsync(achados[0], quantidade);
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
        if (item is null) return;
        Editar(() => _carrinho.AlterarQuantidade(item.Linha, item.Linha.Quantidade + 1));
    }

    [RelayCommand]
    private void Diminuir(ItemCarrinhoViewModel? item)
    {
        if (item is null) return;
        if (item.Linha.Quantidade <= 1) Editar(() => _carrinho.Remover(item.Linha));
        else Editar(() => _carrinho.AlterarQuantidade(item.Linha, item.Linha.Quantidade - 1));
    }

    [RelayCommand]
    private void Remover(ItemCarrinhoViewModel? item)
    {
        if (item is not null) Editar(() => _carrinho.Remover(item.Linha));
    }

    [RelayCommand]
    private void RemoverSelecionado()
    {
        if (ItemSelecionado is { } item) Editar(() => _carrinho.Remover(item.Linha));
    }

    /// <summary>Esvazia o carrinho depois de perguntar. Também zera desconto e pagamento.</summary>
    [RelayCommand]
    private void Esvaziar()
    {
        if (_carrinho.Vazio) return;
        if (!Confirmar("Esvaziar o carrinho? Todos os itens serão removidos.")) return;

        LimparTudo();
        FocoNaBuscaSolicitado?.Invoke();
    }

    [RelayCommand]
    private void EscolherDinheiro()
    {
        Forma = FormaPagamento.Dinheiro;
        FocoNoValorRecebidoSolicitado?.Invoke(); // F6 já leva o cursor para digitar quanto o cliente deu
    }

    [RelayCommand] private void EscolherPix() => Forma = FormaPagamento.Pix;
    [RelayCommand] private void EscolherCartao() => Forma = FormaPagamento.CartaoDebito;

    /// <summary>F10: grava a venda. O VendaService valida tudo de novo e faz venda + baixa numa transação.</summary>
    [RelayCommand]
    private Task FinalizarAsync() => ExecutarAsync(async () =>
    {
        if (_carrinho.Vazio)
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

        var pedido = new PedidoVenda(_carrinho.ParaPedido(), desconto, Forma, recebido, _carrinho.Tabela);
        var venda = await _vendas.FinalizarAsync(pedido);

        LimparTudo(); // esvazia o carrinho e apaga o rascunho
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

        if (!_carrinho.Vazio && !Confirmar("Cancelar a venda atual e esvaziar o carrinho?")) return;

        LimparTudo();
        Busca = "";
        Mensagem = "";
        FocoNaBuscaSolicitado?.Invoke();
    }

    // ================= Edição pela linha do carrinho =================

    void IEditorCarrinho.EditarQuantidade(ItemCarrinhoViewModel item, int quantidade) =>
        Editar(() => _carrinho.AlterarQuantidade(item.Linha, quantidade));

    /// <summary>Vazio devolve o preço de tabela; qualquer valor vira o preço de UMA unidade.</summary>
    void IEditorCarrinho.EditarPrecoUnitario(ItemCarrinhoViewModel item, string texto) => Editar(() =>
    {
        if (string.IsNullOrWhiteSpace(texto)) _carrinho.RestaurarPreco(item.Linha);
        else _carrinho.DefinirPrecoUnitario(item.Linha, LerPreco(texto));
    });

    /// <summary>Vazio devolve o preço de tabela; qualquer valor vira o TOTAL da linha.</summary>
    void IEditorCarrinho.EditarTotal(ItemCarrinhoViewModel item, string texto) => Editar(() =>
    {
        if (string.IsNullOrWhiteSpace(texto)) _carrinho.RestaurarPreco(item.Linha);
        else _carrinho.DefinirTotalLinha(item.Linha, LerPreco(texto));
    });

    private static long LerPreco(string texto) =>
        Dinheiro.TentarConverter(texto, out var centavos)
            ? centavos
            : throw new RegraDeNegocioException("Valor inválido. Exemplo: 12,50");

    // ================= Apoio =================

    private async Task AdicionarAoCarrinhoAsync(Produto produto, int quantidade)
    {
        // Por enquanto o PDV vende da linha "Sem tamanho"; a escolha do tamanho entra na próxima etapa.
        var grade = await _produtos.ListarTamanhosAsync(produto.Id);
        var linhaEstoque = grade.First(t => t.EhSemTamanho);

        var linha = _carrinho.Adicionar(produto, linhaEstoque, quantidade);

        Resultados.Clear();
        Busca = "";
        Mensagem = "";
        Sincronizar();
        ItemSelecionado = Itens.FirstOrDefault(i => i.Linha == linha);
    }

    /// <summary>Roda uma edição no carrinho; se o Carrinho recusar (estoque, valor inválido), mostra o motivo.</summary>
    private void Editar(Action acao)
    {
        try
        {
            acao();
            Mensagem = "";
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
        }

        // A tela só relê os campos depois que o binding terminou de escrever; por isso o adiamento.
        var contexto = SynchronizationContext.Current;
        if (contexto is null) Sincronizar();
        else contexto.Post(_ => Sincronizar(), null);
    }

    /// <summary>Deixa a lista da tela igual ao Carrinho: tira o que saiu, põe o que entrou e relê os valores.</summary>
    private void Sincronizar()
    {
        for (var i = Itens.Count - 1; i >= 0; i--)
            if (!_carrinho.Linhas.Contains(Itens[i].Linha)) Itens.RemoveAt(i);

        foreach (var linha in _carrinho.Linhas)
            if (Itens.All(i => i.Linha != linha)) Itens.Add(new ItemCarrinhoViewModel(linha, this));

        foreach (var item in Itens) item.Atualizar();
        AtualizarTotais();
    }

    private void LimparTudo()
    {
        _carrinho = new Carrinho();
        Itens.Clear();
        ItemSelecionado = null;
        DescontoTexto = "";
        ValorRecebidoTexto = "";
        Forma = FormaPagamento.Dinheiro;
        AtualizarTotais();
        _rascunho.Descartar();
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
        SalvarRascunho();
    }
}
