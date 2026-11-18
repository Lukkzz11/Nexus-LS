using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Na página Integrações: gera o "resumo do dia" em uma página que abre bem no celular (para mandar por WhatsApp, e-mail ou Drive).</summary>
public partial class ConfiguracoesViewModel
{
    private InicioService? _inicioCelular;

    /// <summary>Salva como "resumo-AAAA-MM-DD.html" (guarda o histórico) em vez de sobrescrever "resumo-do-dia.html".</summary>
    [ObservableProperty] private bool _resumoCelularComData;

    public bool TemResumoCelular => _inicioCelular is not null;
    [ObservableProperty] private string _mensagemResumoCelular = "";
    [ObservableProperty] private bool _mensagemResumoCelularEhErro;

    /// <summary>A tela escolhe a pasta (devolve nulo se a pessoa cancelar).</summary>
    public Func<string?> EscolherPastaResumoCelular { get; set; } = () =>
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Escolha a pasta para salvar o resumo do dia" };
        return d.ShowDialog() == true ? d.FolderName : null;
    };

    public void UsarResumoCelular(InicioService inicio) { _inicioCelular = inicio; OnPropertyChanged(nameof(TemResumoCelular)); }

    [RelayCommand]
    private async Task GerarResumoCelularAsync()
    {
        if (_inicioCelular is null) return;
        var pasta = EscolherPastaResumoCelular();
        if (pasta is null) return;
        try
        {
            var resumo = await _inicioCelular.ObterAsync();
            var agora = DateTime.Now;
            var html = ResumoCelular.Html(resumo, string.IsNullOrWhiteSpace(NomeLoja) ? "Nexus" : NomeLoja, agora);
            var arquivo = System.IO.Path.Combine(pasta, ResumoCelular.NomeArquivo(resumo.Dia == default ? agora : resumo.Dia, ResumoCelularComData));
            await System.IO.File.WriteAllTextAsync(arquivo, html, new System.Text.UTF8Encoding(false));
            MensagemResumoCelularEhErro = false;
            MensagemResumoCelular = $"Pronto: {arquivo}. Mande esse arquivo para o celular (WhatsApp, e-mail ou Drive) e abra no navegador.";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            MensagemResumoCelularEhErro = true;
            MensagemResumoCelular = "Não foi possível gerar o resumo: " + ex.Message;
        }
    }
}
