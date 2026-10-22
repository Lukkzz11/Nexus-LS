using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Em que ponto do fluxo da venda o operador está. Só uma etapa fica ativa; as modais cobrem a tela.</summary>
public enum EtapaPdv { Busca, Tamanho, Quantidade, Desconto, Pagamento, Sucesso }

/// <summary>As teclas que o fluxo entende. A tela traduz as teclas do WPF para estas.</summary>
public enum ComandoTecla { Enter, Esc, Esquerda, Direita, Cima, Baixo, F10, Espaco }

/// <summary>Para onde o ViewModel pede que o cursor vá (a tela é quem sabe mexer no foco).</summary>
public enum AlvoFoco { Busca, Modal, Quantidade, Desconto, ValorRecebido, Cliente }

/// <summary>
/// Frente de caixa. Fluxo, tudo pelo teclado:
/// BUSCA ─Enter→ TAMANHO ─Enter→ QUANTIDADE ─Enter→ (volta à busca)
/// BUSCA vazia + carrinho cheio ─Enter→ DESCONTO ─Enter→ PAGAMENTO ─Enter→ SUCESSO ─Enter→ nova venda
/// As regras de cada etapa estão em classes do Core (testadas sem janela); aqui só se liga uma etapa à outra.
/// </summary>
public partial class PdvViewModel : ViewModelBase, IEditorCarrinho
{
    private readonly ProdutoService _produtos;
    private readonly VendaService _vendas;
    private readonly RascunhoService _rascunho;
    private readonly ClienteService _clientes;
    private readonly CupomService _cupons;
    private int _ultimaVendaId;

    private Carrinho _carrinho = new();

    // estado da venda em andamento
    private Produto? _produtoAtual;
    private readonly ConfiguracaoService? _configuracao;
    private List<ProdutoTamanho> _gradeReal = new();
    private GradeTamanhos? _grade;
    private ProdutoTamanho? _tamanhoAtual;
    private SeletorQuantidade? _quantidadeSel;
    private SelecaoDesconto _descontoSel = new();
    private DescontoResolvido _descontoResolvido = new(0, null, 0);
    private SelecaoPagamento? _pagamento;
    private SelecaoSucesso _sucessoSel = new();

    private bool _desceu;       // o operador desceu com ↑↓ na lista de resultados
    private bool _ocupado;      // há uma operação (busca, venda) em andamento: Enter extra é engolido
    private readonly TimeSpan _tempoMinimoSucesso;
    private DateTime _sucessoDesde = DateTime.MinValue;
    private CancellationTokenSource? _buscaCts;
    private CancellationTokenSource? _clientesCts;

    /// <param name="tempoMinimoSucesso">
    /// Quanto tempo a tela de sucesso ignora o Enter logo que aparece. Evita que um Enter duplo (ou a tecla
    /// segurada) pule a confirmação da venda sem o operador ver. Os testes passam zero.
    /// </param>
    public PdvViewModel(ProdutoService produtos, VendaService vendas, RascunhoService rascunho, ClienteService clientes,
        CupomService cupons, TimeSpan? tempoMinimoSucesso = null, ConfiguracaoService? configuracao = null)
    {
        _configuracao = configuracao;
        _tempoMinimoSucesso = tempoMinimoSucesso ?? TimeSpan.FromMilliseconds(400);
        _produtos = produtos;
        _vendas = vendas;
        _rascunho = rascunho;
        _clientes = clientes;
        _cupons = cupons;
        Resultados.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TemResultados));
        AtualizarOpcoesDesconto();
    }

    public ObservableCollection<ItemCarrinhoViewModel> Itens { get; } = new();
    public ObservableCollection<Produto> Resultados { get; } = new();

    // opções que os modais desenham
    public ObservableCollection<OpcaoViewModel> OpcoesTamanho { get; } = new();
    public ObservableCollection<OpcaoViewModel> OpcoesDesconto { get; } = new();
    public ObservableCollection<OpcaoViewModel> OpcoesPagamento { get; } = new();
    public ObservableCollection<OpcaoViewModel> OpcoesParcela { get; } = new();
    public ObservableCollection<OpcaoViewModel> OpcoesSucesso { get; } = new();
    public ObservableCollection<Cliente> ClientesResultado { get; } = new();

    /// <summary>A tela define como perguntar "tem certeza?" ao operador (assim o ViewModel não depende de MessageBox).</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    /// <summary>O ViewModel pede para a tela mover o cursor (a tela é quem sabe mexer no foco).</summary>
    public event Action<AlvoFoco>? FocoSolicitado;

    // ================= Estado que a tela mostra =================

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EmTamanho))]
    [NotifyPropertyChangedFor(nameof(EmQuantidade))]
    [NotifyPropertyChangedFor(nameof(EmDesconto))]
    [NotifyPropertyChangedFor(nameof(EmPagamento))]
    [NotifyPropertyChangedFor(nameof(EmSucesso))]
    [NotifyPropertyChangedFor(nameof(ModalAberto))]
    private EtapaPdv _etapa = EtapaPdv.Busca;

    public bool EmTamanho => Etapa == EtapaPdv.Tamanho;
    public bool EmQuantidade => Etapa == EtapaPdv.Quantidade;
    public bool EmDesconto => Etapa == EtapaPdv.Desconto;
    public bool EmPagamento => Etapa == EtapaPdv.Pagamento;
    public bool EmSucesso => Etapa == EtapaPdv.Sucesso;
    public bool ModalAberto => Etapa != EtapaPdv.Busca;

    // ----- Busca e carrinho -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private Produto? _resultadoSelecionado;
    [ObservableProperty] private ItemCarrinhoViewModel? _itemSelecionado;

    public bool TemResultados => Resultados.Count > 0;
    public bool CarrinhoVazio => _carrinho.Vazio;
    public string TabelaTexto => _carrinho.Tabela == TabelaPreco.Atacado ? "ATACADO" : "VAREJO";
    public bool EhAtacado => _carrinho.Tabela == TabelaPreco.Atacado;
    public string SubtotalTexto => Dinheiro.Formatar(_carrinho.SubtotalCentavos);
    public string ResumoItensTexto => _carrinho.Vazio
        ? "Nenhum item"
        : $"{_carrinho.QuantidadeTotal} unidade(s) em {_carrinho.Linhas.Count} linha(s)";

    // ----- Modal de tamanho / quantidade -----
    [ObservableProperty] private string _produtoModalTexto = "";
    [ObservableProperty] private string _tamanhoModalTexto = "";
    // A quantidade é um campo de texto: dá para digitar o número, e as setas ↑ ↓ mudam o mesmo texto.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QuantidadeValor))]
    private string _quantidadeTexto = "1";

    /// <summary>A quantidade digitada como número (1 se o campo estiver vazio ou inválido).</summary>
    public int QuantidadeValor =>
        int.TryParse(QuantidadeTexto, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 1 ? v : 1;
    [ObservableProperty] private string _quantidadeMaximoTexto = "";

    // ----- Modal de desconto -----
    [ObservableProperty] private string _descontoTexto = "";
    [ObservableProperty] private string _descontoResumo = "";
    [ObservableProperty] private string _descontoDica = "";

    // ----- Modal de pagamento -----
    [ObservableProperty] private string _totalFinalTexto = "";
    [ObservableProperty] private string _valorRecebidoTexto = "";
    [ObservableProperty] private string _clienteBusca = "";
    [ObservableProperty] private Cliente? _clienteDestacado;
    [ObservableProperty] private string _trocoTexto = "";

    public bool EmDinheiro => _pagamento?.Forma == FormaPagamento.Dinheiro;
    public bool EmCredito => _pagamento?.Forma == FormaPagamento.CartaoCredito;
    public bool EmAPrazo => _pagamento?.Forma == FormaPagamento.APrazo;

    // ----- Modal de sucesso -----
    [ObservableProperty] private string _sucessoTitulo = "";
    [ObservableProperty] private string _sucessoDetalhe = "";

    // ================= Mudanças de texto =================

    partial void OnBuscaChanged(string value)
    {
        _desceu = false;
        _ = AtualizarResultadosAsync(value);
    }

    /// <summary>
    /// Enquanto se digita a quantidade: só números, e nunca acima do que há em estoque (o excesso vira o máximo, com aviso).
    /// </summary>
    partial void OnQuantidadeTextoChanged(string value)
    {
        var digitos = new string(value.Where(char.IsAsciiDigit).ToArray());
        if (digitos != value)
        {
            QuantidadeTexto = digitos; // tira letras e símbolos de quem colou texto
            return;
        }

        if (_quantidadeSel is { } sel && int.TryParse(digitos, out var numero) && numero > sel.Maximo)
        {
            QuantidadeTexto = sel.Maximo.ToString();
            MostrarErro($"Só há {sel.Maximo} unidade(s) disponíveis.");
        }
    }

    partial void OnDescontoTextoChanged(string value)
    {
        _descontoSel.Texto = value;
        AtualizarResumoDesconto();
    }

    partial void OnValorRecebidoTextoChanged(string value)
    {
        if (_pagamento is null) return;
        _pagamento.ValorRecebidoTexto = value;
        AtualizarTroco();
    }

    partial void OnClienteBuscaChanged(string value) => _ = AtualizarClientesAsync();

    partial void OnClienteDestacadoChanged(Cliente? value)
    {
        if (_pagamento is not null) _pagamento.ClienteId = value?.Id;
    }

    // ================= Apoio =================

    /// <summary>Muda de etapa e leva o cursor para o lugar certo daquela etapa.</summary>
    private void IrPara(EtapaPdv etapa)
    {
        Etapa = etapa;
        FocoSolicitado?.Invoke(etapa switch
        {
            EtapaPdv.Busca => AlvoFoco.Busca,
            EtapaPdv.Quantidade => AlvoFoco.Quantidade,
            EtapaPdv.Desconto => AlvoFoco.Desconto,
            EtapaPdv.Pagamento => EmDinheiro ? AlvoFoco.ValorRecebido : EmAPrazo ? AlvoFoco.Cliente : AlvoFoco.Modal,
            _ => AlvoFoco.Modal
        });
    }

    /// <summary>
    /// Roda uma operação marcando o fluxo como "ocupado": enquanto ela não termina, outro Enter é ignorado.
    /// É a proteção contra venda duplicada por Enter repetido.
    /// </summary>
    private async Task ExecutarOcupadoAsync(Func<Task> acao)
    {
        if (_ocupado) return;
        _ocupado = true;
        try
        {
            await ExecutarAsync(acao);
        }
        finally
        {
            _ocupado = false;
        }
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

        OnPropertyChanged(nameof(CarrinhoVazio));
        OnPropertyChanged(nameof(SubtotalTexto));
        OnPropertyChanged(nameof(ResumoItensTexto));
        SalvarRascunho();
    }

    // Código da tentativa de venda atual: nasce novo a cada carrinho vazio e viaja com o pedido.
    private Guid _chaveVenda = Guid.NewGuid();

    private void LimparCarrinho()
    {
        _chaveVenda = Guid.NewGuid();
        _carrinho = new Carrinho();
        Itens.Clear();
        ItemSelecionado = null;
        _descontoResolvido = new DescontoResolvido(0, null, 0);
        Sincronizar(); // carrinho vazio apaga o rascunho
        OnPropertyChanged(nameof(TabelaTexto));
        OnPropertyChanged(nameof(EhAtacado));
        _rascunho.Descartar();
    }

    private void MoverResultado(int passo)
    {
        if (Resultados.Count == 0) return;
        var atual = ResultadoSelecionado is null ? 0 : Resultados.IndexOf(ResultadoSelecionado);
        ResultadoSelecionado = Resultados[Math.Clamp(atual + passo, 0, Resultados.Count - 1)];
    }
}
