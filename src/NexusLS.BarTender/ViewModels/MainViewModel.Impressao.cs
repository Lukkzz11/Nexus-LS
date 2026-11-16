using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
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

public partial class MainViewModel
{
    /// <summary>
    /// Confere a fila antes de mandar para a impressora: código vazio, acento/caractere inválido, barras finas
    /// demais para a etiqueta e código repetido entre itens diferentes. Devolve false se a pessoa desistir.
    /// </summary>
    private bool ValidarFilaAntesDeImprimir()
    {
        var larguraMm = UsarModeloDesigner && DesignerVM.Modelo is { LarguraMm: > 0 } modelo
            ? modelo.LarguraMm
            : Config.Formato.FormatoCore.EtiquetaLarguraMm;

        if (UsarModeloDesigner && DesignerVM.Modelo is { } modeloDesign)
        {
            // No Estúdio o código pode ocupar só uma parte da etiqueta: vale a menor largura de elemento.
            var larguraModelo = modeloDesign.LarguraMm > 0 ? modeloDesign.LarguraMm : larguraMm;
            var menorElemento = modeloDesign.Elementos
                .Where(e => e.Tipo == TipoElemento.CodigoBarras && e.Visivel && e.Largura > 0)
                .Select(e => e.Largura)
                .DefaultIfEmpty(larguraModelo)
                .Min();
            larguraMm = Math.Min(larguraModelo, menorElemento);
        }
        var problemas = new List<string>();
        var codigos = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var item in FilaImpressao)
        {
            var codigo = item.CodigoBarras;
            var erros = CodigoBarrasTamanho.Validar(codigo, larguraMm);
            if (erros.Count > 0)
                problemas.Add($"{item.Nome} [{item.TamanhoExibicao}]: {string.Join(", ", erros)}");

            if (!string.IsNullOrWhiteSpace(codigo))
                codigos[codigo] = codigos.GetValueOrDefault(codigo) + 1;
        }

        // EAN-13 é o código do produto inteiro: repetir entre tamanhos é esperado. Nos códigos curtos, repetir é erro.
        var repetidos = codigos
            .Where(c => c.Value > 1 && !EhEan13(c.Key))
            .Select(c => c.Key)
            .Take(10)
            .ToList();
        if (repetidos.Count > 0)
            problemas.Add($"código repetido em mais de um item: {string.Join(", ", repetidos)}");

        if (problemas.Count == 0) return true;

        var detalhe = string.Join("\n• ", problemas.Take(12));
        if (problemas.Count > 12) detalhe += $"\n• ... e mais {problemas.Count - 12} problema(s).";

        return _dialogo.Confirmar(
            $"Confira antes de imprimir:\n\n• {detalhe}\n\nDeseja imprimir mesmo assim?",
            "Revisão da Fila de Impressão",
            "Imprimir Mesmo Assim",
            "Revisar Fila");
    }

    private static bool EhEan13(string codigo) =>
        codigo.Length == 13 && codigo.All(char.IsAsciiDigit);

    [RelayCommand]
    public void Imprimir()
    {
        if (FilaImpressao.Count == 0)
        {
            _dialogo.Informar("Adicione produtos à fila de impressão antes de imprimir.");
            return;
        }

        if (!ValidarFilaAntesDeImprimir()) return;

        try
        {
            var posInicial = Math.Max(1, PosicaoInicialFolha);
            var imprimiu = UsarModeloDesigner
                ? ImpressaoService.ImprimirDesign(FilaImpressao, DesignerVM.Modelo, Config.NomeLoja, posInicial, ImpressoraSelecionada)
                : ImpressaoService.Imprimir(FilaImpressao, Config, posInicial, ImpressoraSelecionada);

            if (imprimiu)
            {
                _dialogo.Sucesso("Impressão enviada com sucesso!");
                var limpar = _dialogo.Confirmar(
                    "A impressão foi enviada com sucesso.\nDeseja limpar a fila de impressão agora?",
                    "Limpar Fila",
                    "Sim, Limpar",
                    "Manter na Fila");

                if (limpar)
                {
                    FilaImpressao.Clear();
                    AtualizarTotaisFila();
                }
            }
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Erro durante a impressão: {ex.Message}");
        }
    }

    [RelayCommand]
    public void CopiarZpl()
    {
        if (string.IsNullOrWhiteSpace(ZplCodigo)) return;
        Clipboard.SetText(ZplCodigo);
        _dialogo.Sucesso("Comandos ZPL II copiados para a Área de Transferência!");
    }

    [RelayCommand]
    public void SalvarImagem()
    {
        if (PreviewImagem == null) return;

        var sfd = new SaveFileDialog
        {
            Title = "Salvar Prévia da Etiqueta como Imagem",
            Filter = "Imagem PNG (*.png)|*.png",
            FileName = $"etiqueta_{(ProdutoSelecionado?.Referencia ?? "produto")}.png"
        };

        if (sfd.ShowDialog() == true)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(PreviewImagem));
            using var fs = File.OpenWrite(sfd.FileName);
            encoder.Save(fs);

            _dialogo.Sucesso($"Imagem salva em:\n{sfd.FileName}");
        }
    }
}
