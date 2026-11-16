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
    [RelayCommand]
    public void SalvarEtiquetaNoProduto()
    {
        if (ProdutoAtual == null)
        {
            _dialogo.Informar("Nenhum produto selecionado no catálogo para cadastrar esta etiqueta.");
            return;
        }

        try
        {
            var ok = GerenciadorEtiquetasProdutoService.SalvarEtiquetaProduto(ProdutoAtual.Id, Modelo);
            if (ok)
            {
                ProdutoTemEtiquetaExclusiva = true;

                // Atualizar o item correspondente no catálogo sincronizado
                var itemCat = ListaProdutosCatalog.FirstOrDefault(i => i.Id == ProdutoAtual.Id);
                itemCat?.AtualizarInfoEtiqueta();

                _dialogo.Sucesso($"Etiqueta cadastrada com sucesso para {ProdutoAtual.Nome}!");
            }
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Erro ao salvar etiqueta do produto: {ex.Message}");
        }
    }

    [RelayCommand]
    public void RestaurarEtiquetaPadraoProduto()
    {
        if (ProdutoAtual == null) return;

        var confirmar = _dialogo.Confirmar(
            $"Deseja remover a etiqueta exclusiva e voltar a usar a etiqueta padrão para o produto:\n\n{ProdutoAtual.Nome}?",
            "Restaurar Padrão",
            "Sim, Restaurar",
            "Cancelar");

        if (confirmar)
        {
            GerenciadorEtiquetasProdutoService.RemoverEtiquetaProduto(ProdutoAtual.Id);
            ProdutoTemEtiquetaExclusiva = false;

            var itemCat = ListaProdutosCatalog.FirstOrDefault(i => i.Id == ProdutoAtual.Id);
            itemCat?.AtualizarInfoEtiqueta();

            Modelo = ModeloEtiquetaDesign.CriarPadrao50x30();
            PresetSelecionado = Modelo.Nome;
            InscreverEventosModelo();
            AtualizarPreview();

            _dialogo.Sucesso("O produto agora usará a etiqueta padrão do sistema.");
        }
    }

    [RelayCommand]
    public void SalvarProjetoRapido()
    {
        if (!string.IsNullOrEmpty(CaminhoArquivoAtual) && File.Exists(CaminhoArquivoAtual))
        {
            try
            {
                Modelo.Salvar(CaminhoArquivoAtual);
                _dialogo.Sucesso("Projeto salvo com sucesso!");
                return;
            }
            catch (Exception exLog) { NexusLS.BarTender.App.Log($"DesignerViewModel.Arquivo #1: {exLog.Message}"); }
        }
        SalvarProjetoComo();
    }

    [RelayCommand]
    public void SalvarProjetoComo()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Salvar Modelo de Etiqueta (JSON)",
            Filter = "Modelo de Etiqueta (*.json)|*.json",
            FileName = $"{Modelo.Nome.ToLowerInvariant().Replace(" ", "_").Replace("/", "_").Replace("&", "e")}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                Modelo.Salvar(sfd.FileName);
                CaminhoArquivoAtual = sfd.FileName;
                _dialogo.Sucesso("Modelo de etiqueta salvo com sucesso!");
            }
            catch (Exception ex)
            {
                _dialogo.Erro($"Erro ao salvar modelo: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void DesmarcarSelecao()
    {
        SelecionarElemento(null);
    }

    [RelayCommand]
    public void SalvarModelo()
    {
        var sfd = new SaveFileDialog
        {
            Title = "Salvar Modelo de Etiqueta (JSON)",
            Filter = "Modelo de Etiqueta (*.json)|*.json",
            FileName = $"{Modelo.Nome.ToLowerInvariant().Replace(" ", "_").Replace("/", "_").Replace("&", "e")}.json"
        };

        if (sfd.ShowDialog() == true)
        {
            try
            {
                Modelo.Salvar(sfd.FileName);
                _dialogo.Sucesso("Modelo de etiqueta salvo com sucesso!");
            }
            catch (Exception ex)
            {
                _dialogo.Erro($"Erro ao salvar modelo: {ex.Message}");
            }
        }
    }

    [RelayCommand]
    public void CarregarModelo()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Abrir Modelo de Etiqueta (JSON)",
            Filter = "Modelo de Etiqueta (*.json)|*.json"
        };

        if (ofd.ShowDialog() == true)
        {
            try
            {
                Modelo = ModeloEtiquetaDesign.Carregar(ofd.FileName);
                InscreverEventosModelo();
                ElementoSelecionado = null;
                AtualizarPreview();
                _dialogo.Sucesso("Modelo carregado com sucesso!");
            }
            catch (Exception ex)
            {
                _dialogo.Erro($"Erro ao carregar modelo: {ex.Message}");
            }
        }
    }
}
