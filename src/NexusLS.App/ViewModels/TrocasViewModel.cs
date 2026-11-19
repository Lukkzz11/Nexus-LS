using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da venda na tela de troca: quantas peças ainda podem voltar e quantas o operador marcou.</summary>
public partial class LinhaTrocaViewModel : ObservableObject
{
    public LinhaTrocaViewModel(ItemParaTroca item) => Item = item;

    public ItemParaTroca Item { get; }
    public string Produto => Item.Tamanho == Tamanhos.SemTamanho ? Item.NomeProduto : $"{Item.NomeProduto} ({Item.Tamanho})";
    public string VendidasTexto => Item.JaDevolvidos > 0 ? $"{Item.Vendidos} (já voltaram {Item.JaDevolvidos})" : Item.Vendidos.ToString();
    public string ValorUnitarioTexto => Dinheiro.Formatar(Item.ValorUnitarioCentavos);
    public bool PodeVoltar => Item.Disponiveis > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValorTexto))]
    private string _quantidadeTexto = "";

    /// <summary>Quantidade marcada (0 se vazio ou inválido).</summary>
    public int Quantidade => int.TryParse(QuantidadeTexto.Trim(), out var q) && q > 0 ? q : 0;

    public bool QuantidadeInvalida => QuantidadeTexto.Trim().Length > 0 && (!int.TryParse(QuantidadeTexto.Trim(), out var q) || q < 0 || q > Item.Disponiveis);

    public long ValorCentavos => QuantidadeInvalida ? 0 : Item.ValorPara(Quantidade);
    public string ValorTexto => ValorCentavos > 0 ? Dinheiro.Formatar(ValorCentavos) : "";
}

/// <summary>Uma venda na lista da tela Trocas, com os textos prontos.</summary>
public class LinhaVendaViewModel
{
    public LinhaVendaViewModel(ResumoVendaLista v) => Venda = v;

    public ResumoVendaLista Venda { get; }
    public int VendaId => Venda.VendaId;
    public string DataTexto => Venda.DataHora.ToString("dd/MM/yy HH:mm");
    public string TotalTexto => Dinheiro.Formatar(Venda.TotalCentavos);
    public string FormaTexto => SelecaoPagamento.Nome(Venda.Forma);
    public string ClienteTexto => Venda.Cliente ?? "";
    public string SituacaoTexto => Venda.Cancelada ? "Cancelada" : Venda.TemTroca ? "Com troca" : "";
}

/// <summary>A venda aberta na janela de detalhe, com os textos prontos.</summary>
public class DetalheVendaViewModel
{
    public DetalheVendaViewModel(DetalheVenda d) => Detalhe = d;

    public DetalheVenda Detalhe { get; }
    public string Titulo => $"Venda #{Detalhe.VendaId}";
    public string DataTexto => Detalhe.DataHora.ToString("dd/MM/yyyy HH:mm");
    public string PagamentoTexto
    {
        get
        {
            var t = SelecaoPagamento.Nome(Detalhe.Forma);
            if (Detalhe.Forma == FormaPagamento.CartaoCredito && Detalhe.Parcelas > 1) t += $" em {Detalhe.Parcelas}x";
            if (Detalhe.ValeTrocaCentavos > 0 && Detalhe.Forma != FormaPagamento.ValeTroca) t += $" + vale-troca de {Dinheiro.Formatar(Detalhe.ValeTrocaCentavos)}";
            return t;
        }
    }
    public string ClienteTexto => Detalhe.Cliente ?? "Consumidor final";
    public string OperadorTexto => string.IsNullOrWhiteSpace(Detalhe.Operador) ? "—" : Detalhe.Operador!;
    public string SubtotalTexto => Dinheiro.Formatar(Detalhe.SubtotalCentavos);
    public string DescontoTexto => Dinheiro.Formatar(Detalhe.DescontoCentavos);
    public string AcrescimoTexto => Dinheiro.Formatar(Detalhe.AcrescimoCentavos);
    public string TotalTexto => Dinheiro.Formatar(Detalhe.TotalCentavos);
    public bool TemDesconto => Detalhe.DescontoCentavos > 0;
    public bool TemAcrescimo => Detalhe.AcrescimoCentavos > 0;
    public bool PodeTrocar => Detalhe.PodeTrocar;
    public bool NaoPodeTrocar => !Detalhe.PodeTrocar;
    public string AvisoTexto => Detalhe.Cancelada
        ? $"Venda cancelada{(string.IsNullOrWhiteSpace(Detalhe.MotivoCancelamento) ? "" : ": " + Detalhe.MotivoCancelamento)}. Não há o que trocar."
        : Detalhe.MotivoNaoPodeTrocar ?? "";
    public bool TemTrocas => Detalhe.Trocas.Count > 0;
    public IReadOnlyList<LinhaItemDetalhe> Itens => Detalhe.Itens.Select(i => new LinhaItemDetalhe(i.NomeProduto,
        i.Tamanho == Tamanhos.SemTamanho ? "—" : i.Tamanho, i.Referencia ?? "", i.Quantidade, Dinheiro.Formatar(i.PrecoUnitarioCentavos),
        Dinheiro.Formatar(i.TotalCentavos), i.Devolvidos > 0 ? i.Devolvidos.ToString() : "")).ToList();
    public IReadOnlyList<ResumoDevolucao> Trocas => Detalhe.Trocas;
}

public record LinhaItemDetalhe(string Produto, string Tamanho, string Referencia, int Quantidade, string PrecoTexto, string TotalTexto, string DevolvidosTexto);

/// <summary>Uma escolha do filtro de forma de pagamento (<c>Forma</c> nula = todas).</summary>
public record OpcaoFormaFiltro(FormaPagamento? Forma, string Texto);

/// <summary>
/// Tela Trocas: acha a venda pelo número, marca as peças que voltam, gera e imprime o vale-troca. Mostra as trocas
/// recentes (com cancelamento, para quem pode cancelar vendas) e os vales com o saldo de cada um.
/// </summary>
public partial class TrocasViewModel : ViewModelBase
{
    private readonly TrocaService _trocas;
    private readonly CupomService? _cupons;

    public TrocasViewModel(TrocaService trocas, CupomService? cupons = null)
    {
        _trocas = trocas;
        _cupons = cupons;
        _filtroForma = FormasFiltro[0];
        Carregamento = CarregamentoInicialAsync();
    }

    public Task Carregamento { get; }

    private async Task CarregamentoInicialAsync()
    {
        await AtualizarListasAsync();
        await ExecutarAsync(() => CarregarPaginaAsync(reiniciar: true));
    }

    /// <summary>A tela mostra a venda por inteiro e devolve verdadeiro se o operador quer trocar. Sem tela, abre direto a troca.</summary>
    public Func<DetalheVenda, bool>? MostrarDetalhe { get; set; }

    // ---------- lista de vendas (com filtros) ----------

    private const int TamanhoPagina = 50;
    private FiltroVendasTroca _filtroAplicado = new();
    private int _buscaAtual;

    public ObservableCollection<LinhaVendaViewModel> Vendas { get; } = new();

    public IReadOnlyList<OpcaoFormaFiltro> FormasFiltro { get; } =
        new[] { new OpcaoFormaFiltro(null, "Todas as formas") }
            .Concat(SelecaoPagamento.Formas.Append(FormaPagamento.ValeTroca).Select(f => new OpcaoFormaFiltro(f, SelecaoPagamento.Nome(f))))
            .ToList();

    [ObservableProperty] private DateTime? _filtroDe;
    [ObservableProperty] private DateTime? _filtroAte;
    [ObservableProperty] private string _filtroCupom = "";
    [ObservableProperty] private string _filtroCliente = "";
    [ObservableProperty] private string _filtroProduto = "";
    [ObservableProperty] private OpcaoFormaFiltro? _filtroForma;
    [ObservableProperty] private LinhaVendaViewModel? _vendaSelecionada;
    [ObservableProperty] private string _resumoVendas = "";
    [ObservableProperty] private bool _temMaisVendas;
    [ObservableProperty] private bool _carregandoVendas;

    [RelayCommand]
    private Task PesquisarVendasAsync() => ExecutarAsync(async () =>
    {
        int? cupom = null;
        var texto = FiltroCupom.Trim().TrimStart('#');
        if (texto.Length > 0)
        {
            if (!int.TryParse(texto, out var n) || n <= 0) { MostrarErro("O número do cupom tem só dígitos. Ex.: 125"); return; }
            cupom = n;
        }
        if (FiltroDe is DateTime de && FiltroAte is DateTime ate && de.Date > ate.Date)
        {
            MostrarErro("A data inicial é depois da final.");
            return;
        }
        _filtroAplicado = new FiltroVendasTroca(FiltroDe, FiltroAte, cupom, FiltroCliente, FiltroForma?.Forma, FiltroProduto);
        Mensagem = "";
        await CarregarPaginaAsync(reiniciar: true);
    });

    [RelayCommand]
    private Task MaisVendasAsync() => ExecutarAsync(() => CarregarPaginaAsync(reiniciar: false));

    [RelayCommand]
    private Task LimparFiltrosAsync()
    {
        FiltroDe = null; FiltroAte = null; FiltroCupom = ""; FiltroCliente = ""; FiltroProduto = ""; FiltroForma = FormasFiltro[0];
        return PesquisarVendasAsync();
    }

    private async Task CarregarPaginaAsync(bool reiniciar)
    {
        var minha = ++_buscaAtual;
        CarregandoVendas = true;
        try
        {
            int? antes = reiniciar || Vendas.Count == 0 ? null : Vendas[^1].VendaId;
            var pagina = await _trocas.ListarVendasAsync(_filtroAplicado, antes, TamanhoPagina);
            if (minha != _buscaAtual) return; // chegou uma busca mais nova: esta resposta já não vale
            if (reiniciar) Vendas.Clear();
            foreach (var v in pagina.Vendas) Vendas.Add(new LinhaVendaViewModel(v));
            TemMaisVendas = pagina.TemMais;
            ResumoVendas = Vendas.Count == 0 ? "Nenhuma venda encontrada com esses filtros."
                : $"{Vendas.Count} venda(s) mostrada(s)" + (pagina.TemMais ? " · há mais antigas" : "");
        }
        finally { if (minha == _buscaAtual) CarregandoVendas = false; }
    }

    /// <summary>Abre a venda escolhida na lista (duplo clique ou botão "Abrir"); dali o operador inicia a troca.</summary>
    [RelayCommand]
    private Task AbrirVendaAsync() => ExecutarAsync(async () =>
    {
        if (VendaSelecionada is not { } linha) { MostrarErro("Escolha uma venda na lista."); return; }
        var detalhe = await _trocas.DetalharVendaAsync(linha.VendaId);
        var trocar = MostrarDetalhe?.Invoke(detalhe) ?? detalhe.PodeTrocar;
        if (!trocar) return;
        await IniciarTrocaAsync(linha.VendaId);
    });

    /// <summary>Carrega a venda na área "Nova troca" (o fluxo de troca de sempre).</summary>
    private async Task IniciarTrocaAsync(int vendaId)
    {
        NumeroVendaTexto = vendaId.ToString();
        await CarregarVendaAsync(vendaId);
        Mensagem = "";
        if (Linhas.All(l => !l.PodeVoltar)) MostrarErro("Todas as peças desta venda já voltaram em trocas anteriores.");
    }

    /// <summary>A tela pede o motivo (cancelar troca). Nulo = voltou.</summary>
    public Func<string, string?>? PedirMotivo { get; set; }

    // ---------- nova troca ----------

    [ObservableProperty] private string _numeroVendaTexto = "";
    [ObservableProperty] private string _vendaResumo = "";
    [ObservableProperty] private string _motivo = "";
    [ObservableProperty] private string _totalTrocaTexto = "";
    [ObservableProperty] private string _ultimoValeTexto = "";
    [ObservableProperty] private string? _ultimoValeCodigo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemVenda))]
    [NotifyPropertyChangedFor(nameof(SemVenda))]
    private VendaParaTroca? _venda;

    public bool TemVenda => Venda is not null;
    public bool SemVenda => Venda is null;
    public bool TemUltimoVale => UltimoValeCodigo is not null;
    partial void OnUltimoValeCodigoChanged(string? value) => OnPropertyChanged(nameof(TemUltimoVale));

    public ObservableCollection<LinhaTrocaViewModel> Linhas { get; } = new();

    // ---------- listas ----------

    public ObservableCollection<ResumoDevolucao> Devolucoes { get; } = new();
    public ObservableCollection<SaldoVale> Vales { get; } = new();
    [ObservableProperty] private ResumoDevolucao? _devolucaoSelecionada;
    [ObservableProperty] private SaldoVale? _valeSelecionado;
    [ObservableProperty] private bool _soValesComSaldo = true;
    [ObservableProperty] private string _resumoVales = "";

    public bool PodeCancelar => Sessao.Pode(Permissao.CancelarVendas);

    partial void OnSoValesComSaldoChanged(bool value) => _ = AtualizarListasAsync();

    [RelayCommand]
    private Task BuscarVendaAsync() => ExecutarAsync(async () =>
    {
        var texto = NumeroVendaTexto.Trim().TrimStart('#');
        if (!int.TryParse(texto, out var id) || id <= 0)
        {
            MostrarErro("Digite o número da venda (está no cupom). Ex.: 125");
            return;
        }
        await CarregarVendaAsync(id);
        Mensagem = "";
        if (Linhas.All(l => !l.PodeVoltar)) MostrarErro("Todas as peças desta venda já voltaram em trocas anteriores.");
    });

    private async Task CarregarVendaAsync(int id)
    {
        foreach (var l in Linhas) l.PropertyChanged -= LinhaMudou;
        Linhas.Clear();
        Venda = null;
        VendaResumo = "";
        var venda = await _trocas.VendaParaTrocaAsync(id);
        Venda = venda;
        VendaResumo = $"Venda #{venda.VendaId} · {venda.DataHora:dd/MM/yyyy HH:mm} · {Dinheiro.Formatar(venda.TotalCentavos)} · " +
                      $"{SelecaoPagamento.Nome(venda.Forma)}" + (venda.Cliente is null ? "" : $" · {venda.Cliente}");
        foreach (var item in venda.Itens)
        {
            var linha = new LinhaTrocaViewModel(item);
            linha.PropertyChanged += LinhaMudou;
            Linhas.Add(linha);
        }
        AtualizarTotal();
    }

    private void LinhaMudou(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LinhaTrocaViewModel.QuantidadeTexto)) AtualizarTotal();
    }

    private void AtualizarTotal()
    {
        var pecas = Linhas.Sum(l => l.QuantidadeInvalida ? 0 : l.Quantidade);
        var total = Linhas.Sum(l => l.ValorCentavos);
        TotalTrocaTexto = pecas == 0 ? "Marque as peças que voltaram" : $"{pecas} peça(s) · vale-troca de {Dinheiro.Formatar(total)}";
    }

    [RelayCommand]
    private Task GerarValeAsync() => ExecutarAsync(async () =>
    {
        if (Venda is null) { MostrarErro("Busque a venda primeiro."); return; }
        var invalida = Linhas.FirstOrDefault(l => l.QuantidadeInvalida);
        if (invalida is not null)
        {
            MostrarErro($"\"{invalida.Produto}\": dá para devolver de 0 a {invalida.Item.Disponiveis}.");
            return;
        }
        var itens = Linhas.Where(l => l.Quantidade > 0).Select(l => new ItemADevolver(l.Item.ItemVendaId, l.Quantidade)).ToList();
        var r = await _trocas.DevolverAsync(Venda.VendaId, itens, Motivo);

        Motivo = "";
        NumeroVendaTexto = "";
        foreach (var l in Linhas) l.PropertyChanged -= LinhaMudou;
        Linhas.Clear();
        Venda = null;
        VendaResumo = "";
        TotalTrocaTexto = "";
        UltimoValeCodigo = r.Vale?.Codigo;
        UltimoValeTexto = r.Vale is null
            ? $"Troca #{r.Devolucao.Id} registrada: as peças voltaram ao estoque (valor zero, sem vale)."
            : $"Vale-troca {r.Vale.Codigo} de {Dinheiro.Formatar(r.Vale.ValorCentavos)}";
        await AtualizarListasAsync();
        MostrarSucesso(r.Vale is null ? "Troca registrada." : "Troca registrada: as peças voltaram ao estoque e o vale foi gerado.");

        if (r.Vale is not null) await ImprimirAsync(r.Vale.Codigo);
    });

    [RelayCommand]
    private Task ReimprimirUltimoAsync() => UltimoValeCodigo is null ? Task.CompletedTask : ExecutarAsync(() => ImprimirAsync(UltimoValeCodigo));

    [RelayCommand]
    private Task ReimprimirValeAsync() => ValeSelecionado is null ? Task.CompletedTask : ExecutarAsync(() => ImprimirAsync(ValeSelecionado.Codigo));

    private async Task ImprimirAsync(string codigo)
    {
        if (_cupons is null) return;
        try
        {
            await _cupons.ImprimirValeTrocaAsync(codigo);
            MostrarSucesso($"Vale {codigo} enviado para a impressora.");
        }
        catch (Exception ex) when (ex is not RegraDeNegocioException)
        {
            MostrarErro($"A troca está gravada, mas o vale não foi impresso: {ex.Message} Use \"Reimprimir\".");
        }
    }

    [RelayCommand]
    private Task CancelarTrocaAsync() => ExecutarAsync(async () =>
    {
        if (DevolucaoSelecionada is not { } d) { MostrarErro("Escolha a troca na lista."); return; }
        if (d.Cancelada) { MostrarErro("Esta troca já está cancelada."); return; }
        if (!PodeCancelar) { MostrarErro("Seu perfil não pode cancelar trocas."); return; }
        var motivo = PedirMotivo?.Invoke(
            $"Cancelar a troca #{d.Id} da venda #{d.VendaId} ({Dinheiro.Formatar(d.ValorCentavos)})? As peças saem de novo do estoque e o vale {d.CodigoVale} deixa de valer.");
        if (motivo is null) return;
        await _trocas.CancelarDevolucaoAsync(d.Id, motivo);
        await AtualizarListasAsync();
        MostrarSucesso($"Troca #{d.Id} cancelada.");
    });

    [RelayCommand]
    private Task AtualizarListasAsync() => ExecutarAsync(async () =>
    {
        var hoje = DateTime.Today;
        var devolucoes = await _trocas.ListarDevolucoesAsync(hoje.AddDays(-90), hoje);
        Devolucoes.Clear();
        foreach (var d in devolucoes) Devolucoes.Add(d);

        var vales = await _trocas.ListarValesAsync(SoValesComSaldo);
        Vales.Clear();
        foreach (var v in vales) Vales.Add(v);
        var abertos = vales.Where(v => v.SaldoCentavos > 0).ToList();
        ResumoVales = $"{abertos.Count} vale(s) com saldo · {Dinheiro.Formatar(abertos.Sum(v => v.SaldoCentavos))} a usar";
    });
}
