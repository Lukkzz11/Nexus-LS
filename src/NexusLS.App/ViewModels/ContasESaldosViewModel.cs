using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Tela "Contas e saldos": reúne a visão geral (saldos, a receber/pagar, projeção, resultado), as contas e transferências,
/// os cartões a liquidar e os cheques. Quando qualquer aba muda dinheiro, a visão geral recalcula na hora.
/// </summary>
public partial class ContasESaldosViewModel : ObservableObject
{
    public ContasESaldosViewModel(PosicaoFinanceiraService posicao, ContaFinanceiraService contas,
        LiquidacaoCartaoService cartoes, ChequeService cheques, Func<DateTime>? agora = null)
    {
        Saldos = new SaldosViewModel(posicao, agora);
        void Recalcular() => _ = Saldos.CarregarAsync();
        Contas = new ContasFinanceirasViewModel(contas, Recalcular, agora);
        Cartoes = new CartoesViewModel(cartoes, contas, Recalcular, agora);
        Cheques = new ChequesViewModel(cheques, contas, Recalcular, agora);
    }

    public SaldosViewModel Saldos { get; }
    public ContasFinanceirasViewModel Contas { get; }
    public CartoesViewModel Cartoes { get; }
    public ChequesViewModel Cheques { get; }

    /// <summary>Qual aba está aberta (0 = visão geral). Ao voltar para a visão geral os números são recalculados.</summary>
    [ObservableProperty] private int _abaAtual;

    partial void OnAbaAtualChanged(int value)
    {
        if (value == 0) _ = Saldos.CarregarAsync();
    }
}
