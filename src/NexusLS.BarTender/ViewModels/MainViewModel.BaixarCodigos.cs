using System.IO;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusLS.BarTender.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private async Task BaixarCodigosDeBarrasAsync()
    {
        var escolha = new OpenFolderDialog { Title = "Onde salvar os códigos de barras?" };
        if (escolha.ShowDialog() != true) return;

        try
        {
            var pasta = Path.Combine(escolha.FolderName, "Codigos de barras Nexus");
            var produtos = await _dbService.ObterProdutosAsync();
            var salvos = ExportadorCodigosDeBarras.Exportar(produtos, pasta);
            _dialogo.Sucesso($"{salvos} códigos de barras salvos em:\n{pasta}");
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Não foi possível salvar os códigos de barras: {ex.Message}");
        }
    }
}
