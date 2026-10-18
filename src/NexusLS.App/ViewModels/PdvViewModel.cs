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
public enum ComandoTecla { Enter, Esc, Esquerda, Direita, Cima, Baixo, F10 }

/// <summary>Para onde o ViewModel pede que o cursor vá (a tela é quem sabe mexer no foco).</summary>
public enum AlvoFoco { Busca, Modal, Desconto, ValorRecebido, Cliente }

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

    private Carrinho _carrinho = new();

    // estado da venda em andamento
    private Produto? _produtoAtual;
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
        TimeSpan? tempoMinimoSucesso = null)
    {
        _tempoMinimoSucesso = tempoMinimoSucesso ?? TimeSpan.FromMilliseconds(400);
        _produtos = produtos;
        _vendas = vendas;
        _rascunho = rascunho;
        _clientes = clientes;
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

    /// <summary>A tela imprime o cupom da venda (etapa seguinte do projeto); por enquanto ninguém escuta.</summary>
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
    [ObservableProperty] private int _quantidadeValor = 1;
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

    // ================= Teclado =================

    /// <summary>
    /// Ponto único de entrada das teclas do fluxo. Devolve true se a tecla foi usada
    /// (a tela então não a repassa a mais ninguém). Enquanto uma operação está em andamento,
    /// Enter e F10 são engolidos: assim um Enter repetido nunca cria uma segunda venda.
    /// </summary>
    public bool ProcessarTecla(ComandoTecla tecla)
    {
        if (_ocupado) return tecla is ComandoTecla.Enter or ComandoTecla.F10;

        return Etapa switch
        {
            EtapaPdv.Busca => TeclaBusca(tecla),
            EtapaPdv.Tamanho => TeclaTamanho(tecla),
            EtapaPdv.Quantidade => TeclaQuantidade(tecla),
            EtapaPdv.Desconto => TeclaDesconto(tecla),
            EtapaPdv.Pagamento => TeclaPagamento(tecla),
            _ => TeclaSucesso(tecla)
        };
    }

    private bool TeclaBusca(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Enter:
                if (Busca.Trim().Length == 0) AbrirDesconto();          // busca vazia + carrinho cheio: fechar a venda
                else _ = ExecutarOcupadoAsync(EscolherProdutoAsync);    // senão: escolher o produto
                return true;
            case ComandoTecla.F10:
                AbrirDesconto();
                return true;
            case ComandoTecla.Cima:
            case ComandoTecla.Baixo:
                if (Resultados.Count == 0) return false;
                MoverResultado(tecla == ComandoTecla.Baixo ? +1 : -1);
                _desceu = true; // a partir daqui o Enter usa o item destacado
                return true;
            case ComandoTecla.Esc:
                if (Resultados.Count > 0) Resultados.Clear();
                else if (Busca.Length > 0) Busca = "";
                _desceu = false;
                return true;
            default:
                return false; // ← → ficam para o cursor do campo de texto
        }
    }

    private bool TeclaTamanho(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda: MoverGrade(DirecaoGrade.Esquerda); return true;
            case ComandoTecla.Direita: MoverGrade(DirecaoGrade.Direita); return true;
            case ComandoTecla.Cima: MoverGrade(DirecaoGrade.Cima); return true;
            case ComandoTecla.Baixo: MoverGrade(DirecaoGrade.Baixo); return true;
            case ComandoTecla.Enter: ConfirmarTamanho(); return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaQuantidade(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Cima:
                if (_quantidadeSel!.Aumentar()) Mensagem = "";
                else MostrarErro($"Só há {_quantidadeSel.Maximo} unidade(s) disponíveis.");
                QuantidadeValor = _quantidadeSel.Valor;
                return true;
            case ComandoTecla.Baixo:
                _quantidadeSel!.Diminuir();
                QuantidadeValor = _quantidadeSel.Valor;
                Mensagem = "";
                return true;
            case ComandoTecla.Enter: ConfirmarQuantidade(); return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaDesconto(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda:
                _descontoSel.Esquerda();
                AtualizarOpcoesDesconto();
                return true;
            case ComandoTecla.Direita:
                _descontoSel.Direita();
                AtualizarOpcoesDesconto();
                return true;
            case ComandoTecla.Enter:
            case ComandoTecla.F10:
                ConfirmarDesconto();
                return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaPagamento(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda: _pagamento!.Anterior(); FormaMudou(); return true;
            case ComandoTecla.Direita: _pagamento!.Proxima(); FormaMudou(); return true;
            case ComandoTecla.Cima:
                if (EmAPrazo) MoverCliente(-1);
                else if (_pagamento!.MaisParcelas()) AtualizarPagamento();
                return true;
            case ComandoTecla.Baixo:
                if (EmAPrazo) MoverCliente(+1);
                else if (_pagamento!.MenosParcelas()) AtualizarPagamento();
                return true;
            case ComandoTecla.Enter:
            case ComandoTecla.F10:
                _ = ExecutarOcupadoAsync(FinalizarVendaAsync);
                return true;
            case ComandoTecla.Esc: VoltarParaBusca(); return true;
            default: return false;
        }
    }

    private bool TeclaSucesso(ComandoTecla tecla)
    {
        switch (tecla)
        {
            case ComandoTecla.Esquerda: _sucessoSel.Esquerda(); AtualizarOpcoesSucesso(); return true;
            case ComandoTecla.Direita: _sucessoSel.Direita(); AtualizarOpcoesSucesso(); return true;
            case ComandoTecla.Enter:
                if (DateTime.UtcNow - _sucessoDesde < _tempoMinimoSucesso) return true; // Enter apressado: engole
                if (_sucessoSel.Opcao == OpcaoSucesso.NovaVenda) NovaVenda();
                else ReimprimirCupom();
                return true;
            case ComandoTecla.Esc: NovaVenda(); return true;
            default: return false;
        }
    }

    // ================= Atalhos globais =================

    /// <summary>F2: foco na busca (e a tela seleciona o texto). Se há uma modal do fluxo aberta, ela é fechada.</summary>
    public void PedirBusca()
    {
        if (Etapa is EtapaPdv.Tamanho or EtapaPdv.Quantidade or EtapaPdv.Desconto or EtapaPdv.Pagamento)
            VoltarParaBusca();
        else if (Etapa == EtapaPdv.Busca)
            FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }

    /// <summary>F8: alterna a tabela de preço (varejo/atacado). Só na busca, para não mudar preços no meio de uma modal.</summary>
    [RelayCommand]
    private void AlternarTabela()
    {
        if (Etapa != EtapaPdv.Busca || _ocupado) return;

        _carrinho.MudarTabela(_carrinho.Tabela == TabelaPreco.Atacado ? TabelaPreco.Varejo : TabelaPreco.Atacado);
        Sincronizar();
        OnPropertyChanged(nameof(TabelaTexto));
        OnPropertyChanged(nameof(EhAtacado));
        MostrarSucesso($"Tabela de preço: {TabelaTexto}.");
        FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }

    [RelayCommand]
    private void FecharVenda() => AbrirDesconto();

    /// <summary>Esvazia o carrinho depois de perguntar. O Esc não esvazia mais: foi só fechar o modal.</summary>
    [RelayCommand]
    private void Esvaziar()
    {
        if (_carrinho.Vazio || Etapa != EtapaPdv.Busca) return;
        if (!Confirmar("Esvaziar o carrinho? Todos os itens serão removidos.")) return;

        LimparCarrinho();
        Mensagem = "";
        FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }

    // ================= Etapa: busca → tamanho → quantidade =================

    private async Task AtualizarResultadosAsync(string texto)
    {
        _buscaCts?.Cancel();
        var cts = _buscaCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token); // espera o operador parar de digitar
            texto = texto.Trim();
            if (texto.Length == 0)
            {
                Resultados.Clear();
                return;
            }

            var lista = await _produtos.BuscarAsync(texto);
            if (cts.IsCancellationRequested) return;

            Resultados.Clear();
            foreach (var p in lista.Take(8)) Resultados.Add(p);
            ResultadoSelecionado = Resultados.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // chegou uma digitação mais nova; esta busca não vale mais
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao buscar: " + ex.Message);
        }
    }

    /// <summary>
    /// Enter na busca: item destacado com ↑↓, senão código de barras exato (leitor), senão o primeiro resultado.
    /// Depois abre SEMPRE o modal de tamanho.
    /// </summary>
    private async Task EscolherProdutoAsync()
    {
        var texto = Busca;
        var destacado = _desceu ? ResultadoSelecionado : null;

        var produto = await _produtos.EscolherParaVendaAsync(texto, destacado);
        if (produto is null)
        {
            MostrarErro($"Nenhum produto encontrado para \"{texto.Trim()}\".");
            return;
        }

        _gradeReal = await _produtos.ListarTamanhosAsync(produto.Id);
        var opcoes = GradeTamanhos.OpcoesParaVenda(_gradeReal, _carrinho.Tabela)
            .Select(t => new ProdutoTamanho   // o estoque mostrado já desconta o que está no carrinho
            {
                Id = t.Id,
                ProdutoId = t.ProdutoId,
                Tamanho = t.Tamanho,
                Estoque = Math.Max(0, t.Estoque - _carrinho.QuantidadeDe(produto.Id, t.Tamanho))
            })
            .ToList();

        _produtoAtual = produto;
        _grade = new GradeTamanhos(opcoes);
        ProdutoModalTexto = $"{produto.Nome}  ·  {Dinheiro.Formatar(produto.PrecoPara(_carrinho.Tabela))}";

        OpcoesTamanho.Clear();
        foreach (var o in _grade.Opcoes)
            OpcoesTamanho.Add(new OpcaoViewModel(o.Tamanho, o.Estoque > 0 ? $"{o.Estoque} em estoque" : "esgotado", o.Estoque <= 0));
        AtualizarSelecaoTamanho();

        Resultados.Clear();
        Mensagem = "";
        IrPara(EtapaPdv.Tamanho);
    }

    private void MoverGrade(DirecaoGrade direcao)
    {
        _grade!.Mover(direcao);
        AtualizarSelecaoTamanho();
        Mensagem = "";
    }

    private void AtualizarSelecaoTamanho()
    {
        for (var i = 0; i < OpcoesTamanho.Count; i++) OpcoesTamanho[i].Selecionada = i == _grade!.Indice;
    }

    private void ConfirmarTamanho()
    {
        var escolhido = _grade!.Selecionado;
        if (escolhido is null || escolhido.Estoque <= 0)
        {
            MostrarErro("Este tamanho está esgotado. Escolha outro.");
            return;
        }

        _tamanhoAtual = _gradeReal.First(t => t.Tamanho == escolhido.Tamanho);
        _quantidadeSel = new SeletorQuantidade(escolhido.Estoque);
        QuantidadeValor = 1;
        TamanhoModalTexto = escolhido.Tamanho == Tamanhos.SemTamanho ? "Sem tamanho" : $"Tamanho {escolhido.Tamanho}";
        QuantidadeMaximoTexto = $"Disponível: {escolhido.Estoque}";
        Mensagem = "";
        IrPara(EtapaPdv.Quantidade);
    }

    private void ConfirmarQuantidade()
    {
        try
        {
            var quantidade = _quantidadeSel!.Valor;
            var linha = _carrinho.Adicionar(_produtoAtual!, _tamanhoAtual!, quantidade);

            Busca = "";
            Sincronizar();
            ItemSelecionado = Itens.FirstOrDefault(i => i.Linha == linha);
            IrPara(EtapaPdv.Busca);
            MostrarSucesso($"{quantidade}× {_produtoAtual!.Nome} adicionado.");
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
        }
    }

    private void VoltarParaBusca()
    {
        IrPara(EtapaPdv.Busca);
        Mensagem = "";
    }

    // ================= Etapa: desconto → pagamento =================

    private void AbrirDesconto()
    {
        if (_carrinho.Vazio)
        {
            MostrarErro("O carrinho está vazio. Adicione um produto antes de fechar a venda.");
            return;
        }

        _descontoSel = new SelecaoDesconto();
        DescontoTexto = "";
        AtualizarOpcoesDesconto();
        AtualizarResumoDesconto();
        Mensagem = "";
        IrPara(EtapaPdv.Desconto);
    }

    private void ConfirmarDesconto()
    {
        try
        {
            _descontoResolvido = _descontoSel.Resolver(_carrinho.SubtotalCentavos);
        }
        catch (RegraDeNegocioException ex)
        {
            MostrarErro(ex.Message);
            return;
        }

        var total = _carrinho.SubtotalCentavos - _descontoResolvido.ValorEmCentavos;
        _pagamento = new SelecaoPagamento(total);
        TotalFinalTexto = Dinheiro.Formatar(total);
        ValorRecebidoTexto = _pagamento.ValorRecebidoTexto;
        ClienteBusca = "";
        ClientesResultado.Clear();

        OpcoesPagamento.Clear();
        foreach (var forma in SelecaoPagamento.Formas) OpcoesPagamento.Add(new OpcaoViewModel(SelecaoPagamento.Nome(forma)));
        OpcoesParcela.Clear();
        for (var n = 1; n <= SelecaoPagamento.MaxParcelas; n++) OpcoesParcela.Add(new OpcaoViewModel($"{n}x"));

        Mensagem = "";
        AtualizarPagamento();
        IrPara(EtapaPdv.Pagamento);
    }

    private void AtualizarOpcoesDesconto()
    {
        if (OpcoesDesconto.Count == 0)
        {
            OpcoesDesconto.Add(new OpcaoViewModel("R$", "valor em reais"));
            OpcoesDesconto.Add(new OpcaoViewModel("%", "percentual"));
        }
        OpcoesDesconto[0].Selecionada = _descontoSel.Tipo == TipoDesconto.Reais;
        OpcoesDesconto[1].Selecionada = _descontoSel.Tipo == TipoDesconto.Percentual;
        DescontoDica = _descontoSel.Tipo == TipoDesconto.Reais ? "Digite o valor. Ex.: 5,00" : "Digite o percentual. Ex.: 10";
        AtualizarResumoDesconto();
    }

    private void AtualizarResumoDesconto()
    {
        var subtotal = _carrinho.SubtotalCentavos;
        try
        {
            var d = _descontoSel.Resolver(subtotal);
            DescontoResumo = d.ValorEmCentavos > 0
                ? $"Desconto {Dinheiro.Formatar(d.ValorEmCentavos)}  ·  Total {Dinheiro.Formatar(subtotal - d.ValorEmCentavos)}"
                : $"Sem desconto  ·  Total {Dinheiro.Formatar(subtotal)}";
        }
        catch (RegraDeNegocioException)
        {
            DescontoResumo = "Valor inválido";
        }
    }

    /// <summary>Depois de ← →: outra forma de pagamento. Leva o cursor ao campo que ela pede.</summary>
    private void FormaMudou()
    {
        AtualizarPagamento();
        Mensagem = "";

        if (EmAPrazo)
        {
            ClienteBusca = "";
            _ = AtualizarClientesAsync();
            FocoSolicitado?.Invoke(AlvoFoco.Cliente);
        }
        else
        {
            FocoSolicitado?.Invoke(EmDinheiro ? AlvoFoco.ValorRecebido : AlvoFoco.Modal);
        }
    }

    private void AtualizarPagamento()
    {
        for (var i = 0; i < OpcoesPagamento.Count; i++)
            OpcoesPagamento[i].Selecionada = SelecaoPagamento.Formas[i] == _pagamento!.Forma;
        for (var i = 0; i < OpcoesParcela.Count; i++)
            OpcoesParcela[i].Selecionada = i + 1 == _pagamento!.Parcelas;

        OnPropertyChanged(nameof(EmDinheiro));
        OnPropertyChanged(nameof(EmCredito));
        OnPropertyChanged(nameof(EmAPrazo));
        AtualizarTroco();
    }

    private void AtualizarTroco()
    {
        if (_pagamento is null || _pagamento.Forma != FormaPagamento.Dinheiro)
        {
            TrocoTexto = "";
            return;
        }

        if (_pagamento.ValorRecebidoCentavos is not long recebido)
            TrocoTexto = "Valor recebido inválido";
        else if (recebido < _pagamento.TotalCentavos)
            TrocoTexto = $"Faltam {Dinheiro.Formatar(_pagamento.TotalCentavos - recebido)}";
        else
            TrocoTexto = $"Troco: {Dinheiro.Formatar(recebido - _pagamento.TotalCentavos)}";
    }

    private async Task AtualizarClientesAsync()
    {
        if (Etapa != EtapaPdv.Pagamento || !EmAPrazo) return;

        _clientesCts?.Cancel();
        var cts = _clientesCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            var lista = await _clientes.BuscarAsync(ClienteBusca);
            if (cts.IsCancellationRequested) return;

            ClientesResultado.Clear();
            foreach (var c in lista.Take(6)) ClientesResultado.Add(c);
            ClienteDestacado = ClientesResultado.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao buscar clientes: " + ex.Message);
        }
    }

    private void MoverCliente(int passo)
    {
        if (ClientesResultado.Count == 0) return;
        var atual = ClienteDestacado is null ? 0 : ClientesResultado.IndexOf(ClienteDestacado);
        ClienteDestacado = ClientesResultado[Math.Clamp(atual + passo, 0, ClientesResultado.Count - 1)];
    }

    /// <summary>
    /// Enter no pagamento. A venda a prazo sem cliente na lista cadastra o nome digitado na hora
    /// (primeiro Enter) e só finaliza no Enter seguinte. O VendaService valida tudo de novo e
    /// faz venda + baixa de estoque numa transação única.
    /// </summary>
    private async Task FinalizarVendaAsync()
    {
        if (_pagamento is null) return;

        if (EmAPrazo && ClienteDestacado is null && ClienteBusca.Trim().Length > 0)
        {
            var novo = await _clientes.CadastrarAsync(new DadosCliente(ClienteBusca));
            ClientesResultado.Clear();
            ClientesResultado.Add(novo);
            ClienteDestacado = novo;
            MostrarSucesso($"Cliente \"{novo.Nome}\" cadastrado. Tecle Enter para finalizar.");
            return;
        }

        var erro = _pagamento.Validar();
        if (erro is not null)
        {
            MostrarErro(erro);
            return;
        }

        var venda = await _vendas.FinalizarAsync(_pagamento.ParaPedido(_carrinho, _descontoResolvido));

        var pagou = SelecaoPagamento.Nome(venda.FormaPagamento);
        if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1) pagou += $" em {venda.Parcelas}x";
        SucessoTitulo = $"Venda #{venda.Id} finalizada!";
        SucessoDetalhe = $"{Dinheiro.Formatar(venda.TotalCentavos)}  ·  {pagou}"
                         + (venda.TrocoCentavos > 0 ? $"  ·  Troco {Dinheiro.Formatar(venda.TrocoCentavos)}" : "");

        LimparCarrinho();
        _sucessoSel = new SelecaoSucesso();
        _sucessoDesde = DateTime.UtcNow;
        AtualizarOpcoesSucesso();
        Mensagem = "";
        IrPara(EtapaPdv.Sucesso);
    }

    // ================= Etapa: sucesso =================

    private void AtualizarOpcoesSucesso()
    {
        if (OpcoesSucesso.Count == 0)
        {
            OpcoesSucesso.Add(new OpcaoViewModel("Reimprimir cupom"));
            OpcoesSucesso.Add(new OpcaoViewModel("Nova venda"));
        }
        OpcoesSucesso[0].Selecionada = _sucessoSel.Opcao == OpcaoSucesso.Reimprimir;
        OpcoesSucesso[1].Selecionada = _sucessoSel.Opcao == OpcaoSucesso.NovaVenda;
    }

    private void NovaVenda()
    {
        Busca = "";
        Mensagem = "";
        IrPara(EtapaPdv.Busca);
    }

    private void ReimprimirCupom() =>
        MostrarErro("A impressão do cupom chega na próxima etapa do projeto.");

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
        Sincronizar();
        OnPropertyChanged(nameof(TabelaTexto));
        OnPropertyChanged(nameof(EhAtacado));

        var texto = $"Venda em andamento restaurada ({_carrinho.QuantidadeTotal} unidade(s)).";
        if (restaurado.Avisos.Count > 0)
        {
            texto += " " + string.Join(" ", restaurado.Avisos);
            MostrarErro(texto);
        }
        else
        {
            MostrarSucesso(texto);
        }
    });

    private void SalvarRascunho()
    {
        try
        {
            _rascunho.Salvar(_carrinho, null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Falha ao gravar o rascunho (disco cheio, pasta bloqueada) não pode atrapalhar a venda.
        }
    }

    // ================= Edição pela linha do carrinho =================

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

    /// <summary>Muda de etapa e leva o cursor para o lugar certo daquela etapa.</summary>
    private void IrPara(EtapaPdv etapa)
    {
        Etapa = etapa;
        FocoSolicitado?.Invoke(etapa switch
        {
            EtapaPdv.Busca => AlvoFoco.Busca,
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

    private void LimparCarrinho()
    {
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
