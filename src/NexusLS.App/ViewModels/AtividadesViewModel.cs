using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma linha da lista de atividades, já com o texto pronto para a tela.</summary>
public record LinhaAtividade(string Quando, string Operador, string Tipo, string Descricao);

/// <summary>Trilha de atividades: quem cancelou, ajustou ou mudou o quê. Só leitura.</summary>
public partial class AtividadesViewModel : ViewModelBase
{
    private const string TodosOsTipos = "Todos os tipos";
    private readonly AtividadeService _atividades;
    private int _versao;

    public AtividadesViewModel(AtividadeService atividades, DateTime? hoje = null)
    {
        _atividades = atividades;
        var dia = (hoje ?? DateTime.Today).Date;
        _de = dia.AddDays(-6); // campos diretos: não disparam o carregamento duas vezes
        _ate = dia;
        Tipos.Add(TodosOsTipos);
        foreach (var t in Enum.GetValues<TipoAtividade>()) Tipos.Add(AtividadeService.NomeDoTipo(t));
        _tipoEscolhido = TodosOsTipos;
        Carregamento = CarregarAsync();
    }

    /// <summary>A primeira carga da lista (os testes esperam por ela).</summary>
    public Task Carregamento { get; }

    public ObservableCollection<LinhaAtividade> Linhas { get; } = new();
    public ObservableCollection<string> Tipos { get; } = new();

    [ObservableProperty] private DateTime? _de;
    [ObservableProperty] private DateTime? _ate;
    [ObservableProperty] private string _tipoEscolhido;
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private string _resumo = "";

    private bool _definindoPeriodo;

    partial void OnDeChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnAteChanged(DateTime? value) { if (!_definindoPeriodo) _ = CarregarAsync(); }
    partial void OnTipoEscolhidoChanged(string value) => _ = CarregarAsync();
    partial void OnBuscaChanged(string value) => _ = CarregarAsync();

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        if (De is not DateTime de || Ate is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");
        if (ate.Date < de.Date)
            throw new RegraDeNegocioException("A data final não pode ser antes da inicial.");

        TipoAtividade? tipo = Enum.GetValues<TipoAtividade>()
            .Cast<TipoAtividade?>().FirstOrDefault(t => AtividadeService.NomeDoTipo(t!.Value) == TipoEscolhido);

        var lista = await _atividades.ListarAsync(new FiltroAtividades(de, ate, tipo, Busca));
        if (versao != _versao) return;

        Linhas.Clear();
        foreach (var a in lista)
            Linhas.Add(new LinhaAtividade(a.DataHora.ToString("dd/MM/yyyy HH:mm"), a.Operador ?? "—",
                AtividadeService.NomeDoTipo(a.Tipo), a.Descricao));

        Mensagem = "";
        Resumo = lista.Count == 0 ? "Nenhuma atividade neste período." : $"{lista.Count} atividade(s)";
    });

    [RelayCommand] private void Hoje() => Definir(DateTime.Today, DateTime.Today);
    [RelayCommand] private void UltimosSeteDias() => Definir(DateTime.Today.AddDays(-6), DateTime.Today);
    [RelayCommand] private void MesAtual() => Definir(new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1), DateTime.Today);

    private void Definir(DateTime de, DateTime ate)
    {
        _definindoPeriodo = true;
        De = de;
        Ate = ate;
        _definindoPeriodo = false;
        _ = CarregarAsync();
    }
}
