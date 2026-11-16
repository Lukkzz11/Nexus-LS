using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
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
        var semCodigo = TodosProdutos.Where(p => !p.TemCodigoBarras).ToList();
        if (semCodigo.Count == 0)
        {
            _dialogo.Informar("Todos os produtos já têm código na etiqueta (EAN-13, referência + tamanho, código curto ou interno).");
            return;
        }

        var confirmacao = _dialogo.Confirmar(
            $"Foram encontrados {semCodigo.Count} produtos sem nenhum código: nem EAN-13, nem referência, nem código interno.\n\n" +
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

            // Recarrega do banco para garantir integridade
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
