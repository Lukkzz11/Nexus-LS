using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public sealed record LinhaTarefa(string Etiqueta, string Titulo, string Detalhe, bool Urgente, string? Telefone, string? Mensagem)
{
    public bool TemWhatsApp => !string.IsNullOrWhiteSpace(Telefone);
}

/// <summary>Um quadradinho do calendário. Vazio (Numero = "") nos espaços antes do dia 1.</summary>
public sealed record CelulaDia(DateTime? Dia, string Numero, string APagar, string AReceber, string Saldo,
    bool Atrasado, bool SaldoNegativo, bool Hoje, bool Pago, bool Selecionado);

public sealed record LinhaContaDoDia(string Titulo, string Situacao);

/// <summary>Secretária: o que fazer agora e o calendário de pagamentos, com os dados que já estão no sistema.</summary>
public partial class SecretariaViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly SecretariaService _secretaria;
    private readonly FinanceiroService _financeiro;
    private readonly Func<Task<long>> _saldoDeHoje;
    private readonly Func<Task<string>> _nomeLoja;
    private readonly Func<DateTime> _hoje;
    private readonly bool _clientes;

    public SecretariaViewModel(SecretariaService secretaria, FinanceiroService financeiro, Func<Task<long>> saldoDeHoje,
        Func<Task<string>> nomeLoja, bool verFinanceiro, bool verClientes, Func<DateTime>? hoje = null)
    {
        _secretaria = secretaria;
        _financeiro = financeiro;
        _saldoDeHoje = saldoDeHoje;
        _nomeLoja = nomeLoja;
        VerFinanceiro = verFinanceiro;
        _clientes = verClientes;
        _hoje = hoje ?? (() => DateTime.Today);
        _diaEscolhido = _hoje().Date;
        _mes = new DateTime(_diaEscolhido.Year, _diaEscolhido.Month, 1);
        Carregamento = RecarregarAsync();
    }

    /// <summary>A carga em andamento (os testes esperam por ela).</summary>
    public Task Carregamento { get; private set; }

    public bool VerFinanceiro { get; }
    public ObservableCollection<LinhaTarefa> Tarefas { get; } = new();
    public ObservableCollection<CelulaDia> Dias { get; } = new();
    public ObservableCollection<LinhaContaDoDia> ContasDoDia { get; } = new();

    /// <summary>A tela define (abre a janela do WhatsApp). Quem envia a mensagem é a pessoa.</summary>
    public Action<string, string> AbrirWhatsApp { get; set; } = (_, _) => { };

    [ObservableProperty] private DateTime _mes;
    [ObservableProperty] private DateTime _diaEscolhido;
    [ObservableProperty] private string _tituloDoMes = "";
    [ObservableProperty] private string _tituloDoDia = "";
    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _alertaDoMes = "";

    // Marcar um pagamento no dia escolhido.
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _valor = "";
    [ObservableProperty] private bool _eRecebimento;
    [ObservableProperty] private string _repetirMeses = "1";

    private async Task RecarregarAsync()
    {
        try
        {
            var tarefas = await _secretaria.TarefasAsync(VerFinanceiro, _clientes, await _nomeLoja());
            Tarefas.Clear();
            foreach (var t in tarefas)
                Tarefas.Add(new LinhaTarefa(Etiqueta(t.Assunto), t.Titulo, t.Detalhe, t.Urgente, t.Telefone, t.Mensagem));
            var urgentes = tarefas.Count(t => t.Urgente);
            Resumo = tarefas.Count == 0 ? "Tudo em dia. Nada pendente por aqui."
                : $"{tarefas.Count} item(ns) para olhar" + (urgentes > 0 ? $", {urgentes} urgente(s)." : ".");
            if (VerFinanceiro) await CarregarCalendarioAsync();
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao carregar a Secretária: " + ex.Message);
        }
    }

    private async Task CarregarCalendarioAsync()
    {
        var mes = Mes;
        var escolhido = DiaEscolhido;
        var dias = await _secretaria.MesAsync(mes.Year, mes.Month, await _saldoDeHoje());
        var contas = await _secretaria.ContasDoDiaAsync(escolhido);
        if (mes != Mes || escolhido != DiaEscolhido) return; // a pessoa já mudou

        TituloDoMes = mes.ToString("MMMM 'de' yyyy", PtBr);
        Dias.Clear();
        for (var i = 0; i < (int)mes.DayOfWeek; i++) Dias.Add(new CelulaDia(null, "", "", "", "", false, false, false, false, false));
        foreach (var d in dias)
            Dias.Add(new CelulaDia(d.Dia, d.Dia.Day.ToString(),
                d.APagarCentavos > 0 ? "− " + Dinheiro.Formatar(d.APagarCentavos) : "",
                d.AReceberCentavos > 0 ? "+ " + Dinheiro.Formatar(d.AReceberCentavos) : "",
                d.SaldoPrevistoCentavos is { } s && (d.APagarCentavos > 0 || d.AReceberCentavos > 0) ? "saldo " + Dinheiro.Formatar(s) : "",
                d.Atrasado, d.SaldoPrevistoCentavos < 0, d.Dia == _hoje().Date,
                d.PagoCentavos > 0 && d.APagarCentavos == 0 && d.AReceberCentavos == 0, d.Dia == escolhido));

        var negativo = dias.FirstOrDefault(d => d.SaldoPrevistoCentavos < 0);
        AlertaDoMes = negativo is null ? ""
            : $"Atenção: pelo previsto, o dinheiro fica negativo em {negativo.Dia:dd/MM} ({Dinheiro.Formatar(negativo.SaldoPrevistoCentavos!.Value)}).";

        TituloDoDia = escolhido.ToString("dddd, dd 'de' MMMM", PtBr);
        ContasDoDia.Clear();
        foreach (var l in contas)
            ContasDoDia.Add(new LinhaContaDoDia(
                $"{(l.Tipo == TipoLancamento.Despesa ? "Pagar" : "Receber")} {Dinheiro.Formatar(l.ValorCentavos)} · {l.Descricao}",
                l.PagoEm is not null ? "Pago" : l.Vencimento.Date < _hoje().Date ? "Atrasado" : "Em aberto"));
    }

    private static string Etiqueta(AssuntoTarefa a) => a switch
    {
        AssuntoTarefa.Pagar => "PAGAR",
        AssuntoTarefa.Receber => "RECEBER",
        AssuntoTarefa.Compromisso => "AGENDA",
        AssuntoTarefa.Pedido => "PEDIDO",
        AssuntoTarefa.Orcamento => "ORÇAMENTO",
        AssuntoTarefa.Aniversario => "ANIVERSÁRIO",
        _ => "ESTOQUE"
    };

    [RelayCommand]
    private Task AtualizarAsync() => Carregamento = RecarregarAsync();

    [RelayCommand]
    private Task MesAnteriorAsync() => IrParaMes(-1);

    [RelayCommand]
    private Task MesSeguinteAsync() => IrParaMes(1);

    private Task IrParaMes(int passo)
    {
        Mes = Mes.AddMonths(passo);
        DiaEscolhido = Mes;
        return Carregamento = ExecutarAsync(CarregarCalendarioAsync);
    }

    [RelayCommand]
    private Task EscolherDiaAsync(CelulaDia? celula)
    {
        if (celula?.Dia is not { } dia) return Task.CompletedTask;
        DiaEscolhido = dia;
        return Carregamento = ExecutarAsync(CarregarCalendarioAsync);
    }

    [RelayCommand]
    private void Chamar(LinhaTarefa? tarefa)
    {
        if (tarefa is { TemWhatsApp: true }) AbrirWhatsApp(tarefa.Telefone!, tarefa.Mensagem ?? "");
    }

    /// <summary>Marca a conta no dia escolhido. Repetindo, cria uma por mês, todas com o mesmo valor.</summary>
    [RelayCommand]
    private Task MarcarAsync() => ExecutarAsync(async () =>
    {
        if (!VerFinanceiro) throw new RegraDeNegocioException("Sem permissão para o Financeiro.");
        if (!Dinheiro.TentarConverter(Valor, out var centavos) || centavos <= 0)
            throw new RegraDeNegocioException("Informe o valor. Exemplo: 150,00");
        if (!int.TryParse(RepetirMeses.Trim(), out var meses) || meses is < 1 or > 60)
            throw new RegraDeNegocioException("Repetir por: um número de 1 a 60 meses.");

        // O Financeiro divide o total pelas parcelas; passando valor x meses, cada mês fica com o valor digitado.
        await _financeiro.CriarAsync(new DadosLancamento(
            ERecebimento ? TipoLancamento.Receita : TipoLancamento.Despesa,
            Descricao, checked(centavos * meses), DiaEscolhido, Parcelas: meses));

        Descricao = "";
        Valor = "";
        RepetirMeses = "1";
        await RecarregarAsync();
        MostrarSucesso(meses == 1 ? "Marcado no calendário." : $"Marcado por {meses} meses.");
    });
}
