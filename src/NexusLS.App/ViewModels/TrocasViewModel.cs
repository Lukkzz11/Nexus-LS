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
        Carregamento = AtualizarListasAsync();
    }

    public Task Carregamento { get; }

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
    private VendaParaTroca? _venda;

    public bool TemVenda => Venda is not null;
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
