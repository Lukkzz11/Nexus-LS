using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record LinhaIntegracao(string Nome, string Situacao);

/// <summary>Página "Integrações": situação dos serviços ligados ao Nexus e a exportação do mês para o contador.</summary>
public partial class ConfiguracoesViewModel
{
    private PacoteContador? _contador;
    private Func<IEnumerable<(string Nome, string Situacao)>>? _situacaoIntegracoes;

    public bool TemIntegracoes => _contador is not null;

    public ObservableCollection<LinhaIntegracao> Integracoes { get; } = new();
    public ObservableCollection<OpcaoMesComissao> MesesContador { get; } = new();

    [ObservableProperty] private OpcaoMesComissao? _mesContador;
    [ObservableProperty] private string _mensagemIntegracoes = "";
    [ObservableProperty] private bool _mensagemIntegracoesEhErro;

    /// <summary>A tela escolhe a pasta (devolve nulo se a pessoa cancelar).</summary>
    public Func<string?> EscolherPastaContador { get; set; } = () =>
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Escolha a pasta para os arquivos do contador" };
        return d.ShowDialog() == true ? d.FolderName : null;
    };

    public void UsarIntegracoes(PacoteContador contador, Func<IEnumerable<(string Nome, string Situacao)>> situacao)
    {
        _contador = contador;
        _situacaoIntegracoes = situacao;
        MesesContador.Clear();
        var primeiro = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        for (var i = 0; i < 24; i++)
        {
            var inicio = primeiro.AddMonths(-i);
            MesesContador.Add(new OpcaoMesComissao(inicio.ToString("MMMM 'de' yyyy", CultureInfo.GetCultureInfo("pt-BR")), inicio, inicio.AddMonths(1).AddDays(-1)));
        }
        MesContador = MesesContador.Count > 1 ? MesesContador[1] : MesesContador[0];   // o mês fechado mais recente
        AtualizarIntegracoes();
        OnPropertyChanged(nameof(TemIntegracoes));
    }

    [RelayCommand]
    private void AtualizarIntegracoes()
    {
        Integracoes.Clear();
        if (_situacaoIntegracoes is null) return;
        try { foreach (var (nome, situacao) in _situacaoIntegracoes()) Integracoes.Add(new LinhaIntegracao(nome, situacao)); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MensagemIntegracoesEhErro = true;
            MensagemIntegracoes = "Não foi possível ler a situação dos serviços: " + ex.Message;
        }
    }

    [RelayCommand]
    private async Task ExportarContadorAsync()
    {
        if (_contador is null || MesContador is null) return;
        var pasta = EscolherPastaContador();
        if (pasta is null) return;
        try
        {
            var arquivos = await _contador.GerarAsync(MesContador.Inicio, MesContador.Fim, pasta);
            MensagemIntegracoesEhErro = false;
            MensagemIntegracoes = $"Pronto: {arquivos.Count} arquivos salvos em {pasta} ({string.Join(", ", arquivos.Select(System.IO.Path.GetFileName))}).";
        }
        catch (RegraDeNegocioException ex)
        {
            MensagemIntegracoesEhErro = true;
            MensagemIntegracoes = ex.Message;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MensagemIntegracoesEhErro = true;
            MensagemIntegracoes = "Não foi possível exportar: " + ex.Message;
        }
    }
}
