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

public record ItemPreviaEtiqueta(
    string Tamanho,
    string CodigoBarras,
    string Referencia,
    string DimensoesMm,
    BitmapSource Imagem);

public partial class DesignerViewModel
{
    [ObservableProperty]
    private bool _previaImpressaoAberta = false;

    public ObservableCollection<ItemPreviaEtiqueta> ItensPreviaImpressao { get; } = new();

    [RelayCommand]
    public void AbrirPreviaImpressao()
    {
        AplicarRegrasInteligentesDeGrade();
        GerarItensPreviaImpressao();
        PreviaImpressaoAberta = true;
    }

    [RelayCommand]
    public void FecharPreviaImpressao()
    {
        PreviaImpressaoAberta = false;
    }

    public void GerarItensPreviaImpressao()
    {
        ItensPreviaImpressao.Clear();

        var prod = ProdutoAtual ?? new Produto
        {
            Nome = "PRODUTO DE TESTE NEXUS",
            PrecoVendaCentavos = 11990,
            PrecoAtacadoCentavos = 7990,
            Referencia = "1402",
            CodigoBarras = null,
            CodigoInterno = "INT-0001"
        };

        var tamanhos = TamanhosParaTesteDisponiveis.Count > 0
            ? TamanhosParaTesteDisponiveis.ToList()
            : new List<string> { "P", "M", "G", "GG" };

        var dim = $"{Modelo.LarguraMm:0.#} × {Modelo.AlturaMm:0.#} mm";
        var elBarcode = Modelo.Elementos.FirstOrDefault(e => e.Tipo == TipoElemento.CodigoBarras);

        foreach (var tam in tamanhos)
        {
            var tamLimpo = string.Equals(tam, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase) ? null : tam;
            var bmp = EditorDesenhoService.RenderizarBitmap(Modelo, prod, tamLimpo, NomeLoja, escalaDpi: 2.5);
            var cod = elBarcode != null
                ? EditorDesenhoService.ResolverCodigo(elBarcode, prod, tamLimpo)
                : (CodigoBarrasTamanho.ResolverReferencia(prod, tamLimpo) ?? CodigoBarrasTamanho.Resolver(prod, tamLimpo) ?? "—");

            ItensPreviaImpressao.Add(new ItemPreviaEtiqueta(
                Tamanho: string.IsNullOrWhiteSpace(tamLimpo) ? "Único" : tamLimpo,
                CodigoBarras: cod,
                Referencia: prod.Referencia ?? "—",
                DimensoesMm: dim,
                Imagem: bmp));
        }
    }

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
