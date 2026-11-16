using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class DesignerViewModel
{
    // =========================================================================
    // IMPRESSÃO DE TESTE DIRETA
    // =========================================================================
    [RelayCommand]
    public void ImprimirTeste()
    {
        try
        {
            var prod = ProdutoAtual ?? new Produto
            {
                Nome = "PRODUTO DE TESTE NEXUS",
                PrecoVendaCentavos = 11990,
                PrecoAtacadoCentavos = 7990,
                Referencia = "REF-2026",
                CodigoBarras = "7891234567890",
                CodigoInterno = "0001"
            };

            var tamanhoTeste = !string.IsNullOrWhiteSpace(TamanhoParaTeste) && TamanhoParaTeste != "Sem tamanho"
                ? TamanhoParaTeste
                : "";
            var itemTeste = new ItemFila(prod, 1, tamanhoTeste);

            var sucesso = ImpressaoService.ImprimirDesign(new[] { itemTeste }, Modelo, NomeLoja);
            if (sucesso)
            {
                _dialogo.Sucesso("Impressão enviada com sucesso!");
            }
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Erro ao imprimir teste: {ex.Message}");
        }
    }
}
