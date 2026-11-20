using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
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
    /// Abre a tela de Padronizar Etiqueta. Pode ser substituído nos testes unitários.
    /// </summary>
    public Func<PadronizarEtiquetaViewModel, bool> AbrirModalPadronizar { get; set; } =
        vm => Views.JanelaPadronizarEtiqueta.Mostrar(null, vm);

    [RelayCommand]
    public void AbrirPadronizarEtiqueta()
    {
        var todos = TodosProdutos.Select(p => p.Produto).ToList();
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().Count();

        var vm = new PadronizarEtiquetaViewModel(
            DesignerVM.Modelo,
            todos,
            filtrados,
            ProdutoSelecionado?.Produto ?? DesignerVM.ProdutoAtual,
            Config.NomeLoja);

        if (!AbrirModalPadronizar(vm))
            return;

        var modelo = vm.ObterModeloPadronizado();
        if (modelo is null) return;

        PadronizarEtiquetasComModelo(modelo, vm.AplicarApenasFiltrados);
    }

    /// <summary>
    /// Aplica o modelo escolhido a todas as etiquetas (ou às filtradas), garantindo que o Tamanho,
    /// a Referência cadastrada e o Código de Barras (Referência + número interno do tamanho) sejam respeitados.
    /// </summary>
    public int PadronizarEtiquetasComModelo(ModeloEtiquetaDesign modeloOrigem, bool apenasFiltrados = false)
    {
        if (modeloOrigem is null) return 0;

        var modeloPadronizado = PadronizarEtiquetaViewModel.ClonarModelo(modeloOrigem);
        DesignerViewModel.PadronizarModeloParaGrade(
            modeloPadronizado,
            ProdutoSelecionado?.Produto ?? DesignerVM.ProdutoAtual,
            DesignerVM.TamanhosParaTesteDisponiveis);

        var alvos = apenasFiltrados
            ? ProdutosView.Cast<ProdutoItemViewModel>().ToList()
            : TodosProdutos.ToList();

        if (alvos.Count == 0) return 0;

        var ids = alvos.Select(p => p.Id).ToList();
        var totalPadronizados = GerenciadorEtiquetasProdutoService.SalvarEtiquetaEmLote(ids, modeloPadronizado);

        foreach (var item in alvos)
            item.AtualizarInfoEtiqueta();

        // Atualiza também o Estúdio e a Preparação para não voltarem ao modelo antigo
        DesignerVM.Modelo = PadronizarEtiquetaViewModel.ClonarModelo(modeloPadronizado);
        DesignerVM.PresetSelecionado = modeloPadronizado.Nome;
        if (DesignerVM.ProdutoAtual is not null)
            DesignerVM.DefinirProduto(DesignerVM.ProdutoAtual, Config.NomeLoja, carregarEtiquetaSalvaSeHouver: true);

        if (_preparacaoVM?.Selecao is { } selecaoAtiva)
            _preparacaoVM.Carregar(selecaoAtiva, PadronizarEtiquetaViewModel.ClonarModelo(modeloPadronizado), Config.NomeLoja);

        AtualizarMiniaturasFila();
        ProdutosView.Refresh();

        _dialogo.Sucesso(
            $"Padronização concluída em {totalPadronizados} produto(s) com o modelo \"{modeloPadronizado.Nome}\"! " +
            "Tamanho, Referência e Código de Barras (Ref + Tamanho) configurados automaticamente.");

        return totalPadronizados;
    }

    [RelayCommand]
    public async Task GerarCodigoIndividualAsync()
    {
        if (ProdutoSelecionado == null)
        {
            _dialogo.Informar("Selecione um produto na lista primeiro.");
            return;
        }

        try
        {
            var novoCodigo = await _dbService.GerarProximoCodigoEan13Async();
            var sucesso = await _dbService.SalvarCodigoBarrasAsync(ProdutoSelecionado.Id, novoCodigo);
            if (sucesso)
            {
                ProdutoSelecionado.AtualizarCodigo(novoCodigo);
                AtualizarContadores();
                ProdutosView.Refresh();
                AtualizarPreview();

                _dialogo.Sucesso($"Código EAN-13 {novoCodigo} gerado para {ProdutoSelecionado.Nome}!");
            }
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Erro ao gerar código: {ex.Message}");
        }
    }

    [RelayCommand]
    public async Task GerarCodigosEmLoteAsync()
    {
        // Prioriza produtos sem nenhum código ou que não têm nem EAN-13 nem Referência (caem apenas no INT-0000xx)
        var semCodigo = TodosProdutos
            .Where(p => !p.TemCodigoBarras || (string.IsNullOrWhiteSpace(p.CodigoBarras) && string.IsNullOrWhiteSpace(p.Referencia)))
            .ToList();

        if (semCodigo.Count == 0)
        {
            _dialogo.Informar("Todos os produtos já possuem Referência (Ref + Tamanho) ou código EAN-13 cadastrado.");
            return;
        }

        var confirmacao = _dialogo.Confirmar(
            $"Foram encontrados {semCodigo.Count} produto(s) sem Referência e sem código EAN-13 (usando apenas código interno INT).\n\n" +
            "Deseja gerar códigos EAN-13 únicos para todos eles e gravar diretamente no banco de dados do Nexus LS?",
            "Geração em Lote",
            "Gerar Códigos",
            "Cancelar");

        if (!confirmacao) return;

        EstaCarregando = true;
        try
        {
            var ids = semCodigo.Select(p => p.Id).ToList();
            var alterados = await _dbService.GerarCodigosLoteAsync(ids, Config.TipoCodigo);

            // Recarrega do banco preservando filtros e seleção na tela
            await RecarregarDadosAsync();

            _dialogo.Sucesso($"{alterados} códigos de barras gerados e gravados com sucesso no banco de dados!");
        }
        catch (Exception ex)
        {
            _dialogo.Erro($"Falha na geração em lote: {ex.Message}");
        }
        finally
        {
            EstaCarregando = false;
        }
    }
}
