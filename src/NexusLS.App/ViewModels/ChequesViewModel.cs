using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record LinhaCheque(ChequeRecebido Cheque, DateTime Hoje)
{
    public int Id => Cheque.Id;
    public string Valor => Dinheiro.Formatar(Cheque.ValorCentavos);
    public string Numero => Cheque.Numero ?? "—";
    public string Banco => Cheque.Banco ?? "—";
    public string Emitente => Cheque.Emitente ?? "—";
    public string BomPara => Cheque.BomPara.ToString("dd/MM/yyyy");
    public string Origem => Cheque.VendaId is int v ? $"Venda #{v}" : Cheque.PagamentoClienteId is not null ? "Pagamento de cliente" : "Avulso";
    public string Situacao => Cheque.Situacao switch
    {
        SituacaoCheque.Compensado => "Compensado",
        SituacaoCheque.Devolvido => "Devolvido",
        SituacaoCheque.Cancelado => "Cancelado",
        _ => Cheque.BomPara.Date <= Hoje.Date ? "Pode depositar" : "Pendente"
    };
}

/// <summary>Cheques recebidos: completar os dados, compensar numa conta, devolver ou cancelar.</summary>
public partial class ChequesViewModel : ViewModelBase
{
    private readonly ChequeService _servico;
    private readonly ContaFinanceiraService _contas;
    private readonly Action _aoMudar;
    private readonly Func<DateTime> _agora;
    private int? _editandoId;

    public ChequesViewModel(ChequeService servico, ContaFinanceiraService contas, Action? aoMudar = null, Func<DateTime>? agora = null)
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

    public ObservableCollection<LinhaCheque> Cheques { get; } = new();
    public ObservableCollection<ContaFinanceira> Contas { get; } = new();

    [ObservableProperty] private bool _mostrarTodos;
    [ObservableProperty] private LinhaCheque? _selecionado;
    [ObservableProperty] private string _resumo = "";

    partial void OnMostrarTodosChanged(bool value) => _ = CarregarAsync();

    // ----- formulário do cheque -----
    [ObservableProperty] private string _formValorTexto = "";
    [ObservableProperty] private string _formNumero = "";
    [ObservableProperty] private string _formBanco = "";
    [ObservableProperty] private string _formEmitente = "";
    [ObservableProperty] private DateTime? _formBomPara;
    [ObservableProperty] private string _formObservacao = "";

    // ----- compensação -----
    [ObservableProperty] private ContaFinanceira? _contaCompensacao;
    [ObservableProperty] private DateTime? _dataCompensacao;

    public string TituloFormulario => _editandoId is null ? "Novo cheque recebido" : "Dados do cheque";
    public bool ValorEditavel => _editandoId is null;

    partial void OnSelecionadoChanged(LinhaCheque? value)
    {
        if (value is null) return;
        _editandoId = value.Id;
        FormValorTexto = Dinheiro.FormatarParaEdicao(value.Cheque.ValorCentavos);
        FormNumero = value.Cheque.Numero ?? "";
        FormBanco = value.Cheque.Banco ?? "";
        FormEmitente = value.Cheque.Emitente ?? "";
        FormBomPara = value.Cheque.BomPara;
        FormObservacao = value.Cheque.Observacao ?? "";
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(ValorEditavel));
    }

    [RelayCommand]
    private void Novo()
    {
        _editandoId = null;
        Selecionado = null;
        FormValorTexto = FormNumero = FormBanco = FormEmitente = FormObservacao = "";
        FormBomPara = _agora().Date;
        Mensagem = "";
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(ValorEditavel));
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (FormBomPara is not DateTime bomPara) throw new RegraDeNegocioException("Informe a data \"bom para\".");
        long valor = 0;
        if (_editandoId is null && !Dinheiro.TentarConverter(FormValorTexto, out valor))
            throw new RegraDeNegocioException("O valor do cheque não é válido.");

        var dados = new DadosCheque(valor, bomPara, FormNumero, FormBanco, FormEmitente, FormObservacao);
        if (_editandoId is int id) await _servico.EditarAsync(id, dados);
        else await _servico.RegistrarAsync(dados);

        await CarregarAsync();
        _aoMudar();
        Novo();
        MostrarSucesso("Cheque salvo.");
    });

    [RelayCommand]
    private Task CompensarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { Cheque.Situacao: SituacaoCheque.Pendente } c) throw new RegraDeNegocioException("Escolha um cheque pendente.");
        if (ContaCompensacao is null) throw new RegraDeNegocioException("Escolha a conta onde o cheque caiu.");
        if (DataCompensacao is not DateTime data) throw new RegraDeNegocioException("Informe o dia em que compensou.");
        await _servico.CompensarAsync(c.Id, ContaCompensacao.Id, data);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso($"Cheque de {c.Valor} compensado em {ContaCompensacao.Nome}.");
    });

    [RelayCommand]
    private Task DevolverAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { Cheque.Situacao: SituacaoCheque.Pendente } c) throw new RegraDeNegocioException("Escolha um cheque pendente.");
        var motivo = PedirMotivo($"O banco devolveu o cheque de {c.Valor}?\nO valor volta para \"a receber\" (vencido). Informe o motivo:");
        if (motivo is null) return;
        await _servico.DevolverAsync(c.Id, motivo);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Cheque marcado como devolvido. Cobre o cliente.");
    });

    [RelayCommand]
    private Task CancelarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is null) throw new RegraDeNegocioException("Escolha o cheque.");
        var c = Selecionado;
        var motivo = PedirMotivo($"Cancelar o cheque de {c.Valor}?\nInforme o motivo:");
        if (motivo is null) return;
        await _servico.CancelarAsync(c.Id, motivo);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Cheque cancelado.");
    });

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var hoje = _agora().Date;
        var lista = await _servico.ListarAsync(MostrarTodos ? null : SituacaoCheque.Pendente);
        Cheques.Clear();
        foreach (var c in lista) Cheques.Add(new LinhaCheque(c, hoje));
        var pendentes = lista.Where(c => c.Situacao == SituacaoCheque.Pendente).ToList();
        Resumo = pendentes.Count == 0 ? "Nenhum cheque pendente."
            : $"{pendentes.Count} pendente(s) · {Dinheiro.Formatar(pendentes.Sum(c => c.ValorCentavos))}";

        var anterior = ContaCompensacao?.Id;
        var contas = (await _contas.ListarAsync()).ToList();
        Contas.Clear();
        foreach (var c in contas) Contas.Add(c);
        ContaCompensacao = contas.FirstOrDefault(c => c.Id == anterior) ?? contas.FirstOrDefault(c => c.Tipo == TipoContaFin.Banco && c.Padrao) ?? contas.FirstOrDefault();
    });
}
