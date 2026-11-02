using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Um compromisso na lista do dia.</summary>
public sealed record LinhaCompromisso(int Id, string Quando, string Titulo, string Descricao, bool Concluido, Compromisso Origem);

/// <summary>Agenda da loja: escolha o dia no calendário, veja o que está marcado e crie, edite, conclua ou remova.</summary>
public partial class AgendaViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly AgendaService _agenda;
    private readonly Func<DateTime> _hoje;
    private int? _editandoId;

    public AgendaViewModel(AgendaService agenda, Func<DateTime>? hoje = null)
    {
        _agenda = agenda;
        _hoje = hoje ?? (() => DateTime.Today);
        _dia = _hoje().Date;
        _ = RecarregarAsync();
    }

    public ObservableCollection<LinhaCompromisso> Compromissos { get; } = new();
    public ObservableCollection<LinhaCompromisso> Proximos { get; } = new();

    /// <summary>Pergunta "tem certeza?" (a tela define; assim o ViewModel não depende de MessageBox).</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    [ObservableProperty] private DateTime _dia;
    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _hora = "09:00";
    [ObservableProperty] private string _horaFim = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HorarioAtivo))]
    private bool _diaInteiro;

    public bool HorarioAtivo => !DiaInteiro;
    [ObservableProperty] private string _descricao = "";
    [ObservableProperty] private string _tituloDoDia = "";
    [ObservableProperty] private string _formularioTitulo = "Novo compromisso";

    partial void OnDiaChanged(DateTime value) => _ = RecarregarAsync();

    private async Task RecarregarAsync()
    {
        try
        {
            var dia = Dia.Date;
            var lista = await _agenda.DoDiaAsync(dia);
            var proximos = await _agenda.ProximosAsync(5);
            if (dia != Dia.Date) return; // o usuário já trocou de dia

            TituloDoDia = $"{dia.ToString("dddd, dd 'de' MMMM 'de' yyyy", PtBr)}{(dia == _hoje().Date ? " (hoje)" : "")}";
            Compromissos.Clear();
            foreach (var c in lista) Compromissos.Add(Montar(c, false));
            Proximos.Clear();
            foreach (var c in proximos) Proximos.Add(Montar(c, true));
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao carregar a agenda: " + ex.Message);
        }
    }

    private static LinhaCompromisso Montar(Compromisso c, bool comData)
    {
        var quando = c.DiaInteiro ? "Dia inteiro" : c.Fim is { } fim ? $"{c.Inicio:HH:mm}–{fim:HH:mm}" : $"{c.Inicio:HH:mm}";
        if (comData) quando = $"{c.Inicio:dd/MM} · {quando}";
        return new LinhaCompromisso(c.Id, quando, c.Titulo, c.Descricao ?? "", c.Concluido, c);
    }

    private static TimeSpan LerHora(string texto, string quem)
    {
        if (TimeSpan.TryParseExact(texto.Trim().Replace('h', ':'), new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out var h)
            && h < TimeSpan.FromDays(1))
            return h;
        throw new RegraDeNegocioException($"{quem} inválida. Escreva assim: 14:30");
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var inicio = Dia.Date;
        DateTime? fim = null;
        if (!DiaInteiro)
        {
            inicio += LerHora(Hora, "Hora");
            if (!string.IsNullOrWhiteSpace(HoraFim)) fim = Dia.Date + LerHora(HoraFim, "Hora do fim");
        }

        var dados = new DadosCompromisso(Titulo, inicio, fim, DiaInteiro, Descricao);
        if (_editandoId is int id) await _agenda.EditarAsync(id, dados);
        else await _agenda.CriarAsync(dados);

        var editou = _editandoId is not null;
        Novo();
        await RecarregarAsync();
        MostrarSucesso(editou ? "Compromisso atualizado." : "Compromisso criado.");
    });

    [RelayCommand]
    private void Novo()
    {
        _editandoId = null;
        FormularioTitulo = "Novo compromisso";
        Titulo = "";
        Hora = "09:00";
        HoraFim = "";
        DiaInteiro = false;
        Descricao = "";
    }

    [RelayCommand]
    private void Editar(LinhaCompromisso? linha)
    {
        if (linha is null) return;
        var c = linha.Origem;
        _editandoId = c.Id;
        FormularioTitulo = "Editar compromisso";
        Titulo = c.Titulo;
        DiaInteiro = c.DiaInteiro;
        Hora = c.DiaInteiro ? "09:00" : c.Inicio.ToString("HH:mm");
        HoraFim = c.Fim is { } fim ? fim.ToString("HH:mm") : "";
        Descricao = c.Descricao ?? "";
        Dia = c.Inicio.Date;
    }

    [RelayCommand]
    private Task AlternarConcluidoAsync(LinhaCompromisso? linha) => ExecutarAsync(async () =>
    {
        if (linha is null) return;
        await _agenda.DefinirConcluidoAsync(linha.Id, !linha.Concluido);
        await RecarregarAsync();
    });

    [RelayCommand]
    private Task RemoverAsync(LinhaCompromisso? linha) => ExecutarAsync(async () =>
    {
        if (linha is null) return;
        if (!Confirmar($"Remover o compromisso \"{linha.Titulo}\"?")) return;
        await _agenda.RemoverAsync(linha.Id);
        if (_editandoId == linha.Id) Novo();
        await RecarregarAsync();
        MostrarSucesso("Compromisso removido.");
    });

    [RelayCommand]
    private void Hoje() => Dia = _hoje().Date;
}
