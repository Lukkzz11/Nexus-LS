using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record LinhaChequeEmitido(ChequeEmitidoComConta Item, DateTime Hoje)
{
    public int Id => Item.Cheque.Id;
    public string Valor => Dinheiro.Formatar(Item.Cheque.ValorCentavos);
    public string Numero => Item.Cheque.Numero ?? "—";
    public string Favorecido => Item.Cheque.Favorecido ?? "—";
    public string BomPara => Item.Cheque.BomPara.ToString("dd/MM/yyyy");
    public string Conta => Item.ContaNome;
    public string Descricao => Item.Descricao;
    public string Situacao => Item.Cheque.Situacao switch
    {
        SituacaoChequeEmitido.Compensado => "Compensado",
        SituacaoChequeEmitido.Cancelado => "Cancelado",
        _ => Item.Cheque.BomPara.Date <= Hoje.Date ? "Pode compensar" : "Pendente"
    };
}

/// <summary>Uma conta a pagar que pode receber um cheque.</summary>
public record OpcaoContaAPagar(Lancamento Conta)
{
    public string Texto => $"{Conta.Descricao} · {Dinheiro.Formatar(Conta.ValorCentavos)} · vence {Conta.Vencimento:dd/MM/yyyy}";
}

/// <summary>Cheques emitidos para pagar contas: emitir, informar que compensou ou cancelar (com motivo).</summary>
public partial class ChequesEmitidosViewModel : ViewModelBase
{
    private readonly ChequeEmitidoService _servico;
    private readonly ContaFinanceiraService _contas;
    private readonly Action _aoMudar;
    private readonly Func<DateTime> _agora;

    public ChequesEmitidosViewModel(ChequeEmitidoService servico, ContaFinanceiraService contas, Action? aoMudar = null, Func<DateTime>? agora = null)
    {
        _servico = servico;
        _contas = contas;
        _aoMudar = aoMudar ?? (() => { });
        _agora = agora ?? (() => DateTime.Now);
        _formBomPara = _agora().Date;
        _dataCompensacao = _agora().Date;
        Carregamento = CarregarAsync();
    }

    public Task Carregamento { get; }
    public Func<string, string?> PedirMotivo { get; set; } = _ => null;

    public ObservableCollection<LinhaChequeEmitido> Cheques { get; } = new();
    public ObservableCollection<OpcaoContaAPagar> ContasAPagar { get; } = new();
    public ObservableCollection<ContaFinanceira> ContasBanco { get; } = new();

    [ObservableProperty] private bool _mostrarTodos;
    [ObservableProperty] private LinhaChequeEmitido? _selecionado;
    [ObservableProperty] private string _resumo = "";

    partial void OnMostrarTodosChanged(bool value) => _ = CarregarAsync();

    // ----- emissão -----
    [ObservableProperty] private OpcaoContaAPagar? _formConta;
    [ObservableProperty] private ContaFinanceira? _formContaBanco;
    [ObservableProperty] private string _formNumero = "";
    [ObservableProperty] private string _formBanco = "";
    [ObservableProperty] private string _formFavorecido = "";
    [ObservableProperty] private DateTime? _formBomPara;

    // ----- compensação -----
    [ObservableProperty] private DateTime? _dataCompensacao;

    partial void OnFormContaChanged(OpcaoContaAPagar? value)
    {
        if (value is not null) FormFavorecido = value.Conta.Contraparte ?? "";
    }

    [RelayCommand]
    private Task EmitirAsync() => ExecutarAsync(async () =>
    {
        if (FormConta is null) throw new RegraDeNegocioException("Escolha a conta a pagar.");
        if (FormContaBanco is null) throw new RegraDeNegocioException("Escolha a conta de banco de onde o cheque vai sair.");
        if (FormBomPara is not DateTime bomPara) throw new RegraDeNegocioException("Informe a data \"bom para\".");
        var valor = Dinheiro.Formatar(FormConta.Conta.ValorCentavos);
        await _servico.EmitirAsync(new DadosChequeEmitido(FormConta.Conta.Id, FormContaBanco.Id, bomPara, FormNumero, FormBanco, FormFavorecido));
        FormNumero = FormBanco = FormFavorecido = "";
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso($"Cheque de {valor} emitido. A conta fica em compensação até o cheque compensar.");
    });

    [RelayCommand]
    private Task CompensarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { Item.Cheque.Situacao: SituacaoChequeEmitido.Pendente } c) throw new RegraDeNegocioException("Escolha um cheque pendente.");
        if (DataCompensacao is not DateTime data) throw new RegraDeNegocioException("Informe o dia em que compensou.");
        await _servico.CompensarAsync(c.Id, data);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso($"Cheque de {c.Valor} compensado: a conta \"{c.Descricao}\" foi paga.");
    });

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { Item.Cheque.Situacao: SituacaoChequeEmitido.Pendente } c) throw new RegraDeNegocioException("Escolha um cheque pendente.");
        var motivo = PedirMotivo($"Cancelar o cheque de {c.Valor}?\nA conta \"{c.Descricao}\" volta para \"em aberto\". Informe o motivo:");
        if (motivo is null) return;
        await _servico.CancelarAsync(c.Id, motivo);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Cheque cancelado. A conta voltou para em aberto.");
    });

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var hoje = _agora().Date;
        var lista = await _servico.ListarAsync(MostrarTodos ? null : SituacaoChequeEmitido.Pendente);
        Cheques.Clear();
        foreach (var c in lista) Cheques.Add(new LinhaChequeEmitido(c, hoje));
        var pendentes = lista.Where(c => c.Cheque.Situacao == SituacaoChequeEmitido.Pendente).ToList();
        Resumo = pendentes.Count == 0 ? "Nenhum cheque emitido pendente."
            : $"{pendentes.Count} pendente(s) · {Dinheiro.Formatar(pendentes.Sum(c => c.Cheque.ValorCentavos))} a sair do banco";

        var anteriorConta = FormConta?.Conta.Id;
        ContasAPagar.Clear();
        foreach (var l in await _servico.ContasParaChequeAsync()) ContasAPagar.Add(new OpcaoContaAPagar(l));
        FormConta = ContasAPagar.FirstOrDefault(o => o.Conta.Id == anteriorConta);

        var anteriorBanco = FormContaBanco?.Id;
        var bancos = (await _contas.ListarAsync()).Where(c => c.Tipo == TipoContaFin.Banco).ToList();
        ContasBanco.Clear();
        foreach (var b in bancos) ContasBanco.Add(b);
        FormContaBanco = bancos.FirstOrDefault(b => b.Id == anteriorBanco) ?? bancos.FirstOrDefault(b => b.Padrao) ?? bancos.FirstOrDefault();
    });
}
