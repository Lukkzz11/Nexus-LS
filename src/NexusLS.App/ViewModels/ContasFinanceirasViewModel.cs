using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record LinhaConta(ContaFinanceira Conta)
{
    public int Id => Conta.Id;
    public string Nome => Conta.Nome + (Conta.Padrao ? " (padrão)" : "") + (Conta.Ativa ? "" : " — desativada");
    public string Tipo => Conta.Tipo == TipoContaFin.Caixa ? "Dinheiro" : "Banco";
    public string SaldoInicial => Dinheiro.Formatar(Conta.SaldoInicialCentavos);
    public string Desde => Conta.DataSaldoInicial?.ToString("dd/MM/yyyy") ?? "—";
}

public record LinhaTransferencia(TransferenciaConta Transferencia, string Origem, string Destino)
{
    public int Id => Transferencia.Id;
    public string Data => Transferencia.Data.ToString("dd/MM/yyyy");
    public string Descricao => Transferencia.Descricao ?? "";
    public string Valor => Dinheiro.Formatar(Transferencia.ValorCentavos);
    public string Situacao => Transferencia.Cancelada ? "Cancelada" : "Feita";
    public string DeParaTexto => $"{Origem} → {Destino}";
}

/// <summary>Cadastro das contas (gaveta, cofre, bancos) e transferências entre elas.</summary>
public partial class ContasFinanceirasViewModel : ViewModelBase
{
    private readonly ContaFinanceiraService _servico;
    private readonly Func<DateTime> _agora;
    private readonly Action _aoMudar;
    private int? _editandoId;

    public ContasFinanceirasViewModel(ContaFinanceiraService servico, Action? aoMudar = null, Func<DateTime>? agora = null)
    {
        _servico = servico;
        _aoMudar = aoMudar ?? (() => { });
        _agora = agora ?? (() => DateTime.Now);
        _transfData = _agora().Date;
        Carregamento = CarregarAsync();
    }

    public Task Carregamento { get; }

    public Func<string, string?> PedirMotivo { get; set; } = _ => null;

    public ObservableCollection<LinhaConta> Contas { get; } = new();
    public ObservableCollection<ContaFinanceira> ContasAtivas { get; } = new();
    public ObservableCollection<LinhaTransferencia> Transferencias { get; } = new();

    // ----- formulário da conta -----
    [ObservableProperty] private LinhaConta? _selecionada;
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private bool _ehBanco;
    [ObservableProperty] private string _saldoInicialTexto = "";
    [ObservableProperty] private DateTime? _dataSaldoInicial;
    [ObservableProperty] private bool _padrao;
    [ObservableProperty] private bool _mostrarDesativadas;

    public string TituloFormulario => _editandoId is null ? "Nova conta" : "Editar conta";
    public bool PodeReativar => Selecionada is { Conta.Ativa: false };
    public bool PodeDesativar => Selecionada is { Conta.Ativa: true };

    partial void OnMostrarDesativadasChanged(bool value) => _ = CarregarAsync();

    partial void OnSelecionadaChanged(LinhaConta? value)
    {
        if (value is null) return;
        _editandoId = value.Id;
        Nome = value.Conta.Nome;
        EhBanco = value.Conta.Tipo == TipoContaFin.Banco;
        SaldoInicialTexto = Dinheiro.FormatarParaEdicao(value.Conta.SaldoInicialCentavos);
        DataSaldoInicial = value.Conta.DataSaldoInicial;
        Padrao = value.Conta.Padrao;
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(PodeReativar));
        OnPropertyChanged(nameof(PodeDesativar));
    }

    [RelayCommand]
    private void Nova()
    {
        _editandoId = null;
        Selecionada = null;
        Nome = "";
        EhBanco = true;
        SaldoInicialTexto = "";
        DataSaldoInicial = _agora().Date;
        Padrao = false;
        Mensagem = "";
        OnPropertyChanged(nameof(TituloFormulario));
        OnPropertyChanged(nameof(PodeReativar));
        OnPropertyChanged(nameof(PodeDesativar));
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        long saldo = 0;
        if (!string.IsNullOrWhiteSpace(SaldoInicialTexto) && !Dinheiro.TentarConverter(SaldoInicialTexto, out saldo))
            throw new RegraDeNegocioException("O saldo inicial não é um valor válido.");
        if (saldo != 0 && DataSaldoInicial is null)
            throw new RegraDeNegocioException("Informe a data do saldo inicial: o que aconteceu antes dela não conta.");

        var dados = new DadosConta(Nome, EhBanco ? TipoContaFin.Banco : TipoContaFin.Caixa, saldo, DataSaldoInicial, Padrao);
        if (_editandoId is int id) await _servico.EditarAsync(id, dados);
        else await _servico.CriarAsync(dados);

        await CarregarAsync();
        _aoMudar();
        Nova();                       // limpa o formulário (e a mensagem)
        MostrarSucesso("Conta salva.");
    });

    [RelayCommand]
    private Task DesativarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is null) return;
        await _servico.DesativarAsync(Selecionada.Id);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Conta desativada. O histórico dela continua nos saldos.");
    });

    [RelayCommand]
    private Task ReativarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is null) return;
        await _servico.ReativarAsync(Selecionada.Id);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Conta reativada.");
    });

    // ----- transferência -----
    [ObservableProperty] private ContaFinanceira? _transfOrigem;
    [ObservableProperty] private ContaFinanceira? _transfDestino;
    [ObservableProperty] private string _transfValorTexto = "";
    [ObservableProperty] private DateTime? _transfData;
    [ObservableProperty] private string _transfDescricao = "";
    [ObservableProperty] private LinhaTransferencia? _transferenciaSelecionada;

    [RelayCommand]
    private Task TransferirAsync() => ExecutarAsync(async () =>
    {
        if (TransfOrigem is null || TransfDestino is null)
            throw new RegraDeNegocioException("Escolha a conta de onde sai e a conta para onde vai o dinheiro.");
        if (!Dinheiro.TentarConverter(TransfValorTexto, out var valor))
            throw new RegraDeNegocioException("O valor da transferência não é válido.");
        if (TransfData is not DateTime data)
            throw new RegraDeNegocioException("Informe a data.");

        await _servico.TransferirAsync(new DadosTransferencia(TransfOrigem.Id, TransfDestino.Id, valor, data, TransfDescricao));
        TransfValorTexto = "";
        TransfDescricao = "";
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso($"Transferência de {Dinheiro.Formatar(valor)} feita.");
    });

    [RelayCommand]
    private Task CancelarTransferenciaAsync() => ExecutarAsync(async () =>
    {
        if (TransferenciaSelecionada is not { Transferencia.Cancelada: false } t)
            throw new RegraDeNegocioException("Escolha uma transferência que ainda esteja valendo.");
        var motivo = PedirMotivo($"Cancelar a transferência de {t.Valor} ({t.DeParaTexto})?\nInforme o motivo:");
        if (motivo is null) return;
        await _servico.CancelarTransferenciaAsync(t.Id, motivo);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Transferência cancelada.");
    });

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        await _servico.GarantirContaPadraoAsync();
        var todas = await _servico.ListarAsync(MostrarDesativadas);

        Contas.Clear();
        foreach (var c in todas) Contas.Add(new LinhaConta(c));

        var ativas = (await _servico.ListarAsync()).ToList();
        var origemAnterior = TransfOrigem?.Id;
        var destinoAnterior = TransfDestino?.Id;
        ContasAtivas.Clear();
        foreach (var c in ativas) ContasAtivas.Add(c);
        TransfOrigem = ativas.FirstOrDefault(c => c.Id == origemAnterior);
        TransfDestino = ativas.FirstOrDefault(c => c.Id == destinoAnterior);

        var nomes = (await _servico.ListarAsync(true)).ToDictionary(c => c.Id, c => c.Nome);
        var hoje = _agora().Date;
        Transferencias.Clear();
        foreach (var t in await _servico.ListarTransferenciasAsync(hoje.AddDays(-60), hoje))
            Transferencias.Add(new LinhaTransferencia(t, nomes.GetValueOrDefault(t.ContaOrigemId, "?"), nomes.GetValueOrDefault(t.ContaDestinoId, "?")));
    });
}
