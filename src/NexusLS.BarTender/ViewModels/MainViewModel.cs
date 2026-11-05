using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BancoDadosService _dbService;

    [ObservableProperty]
    private string _caminhoBanco = "";

    [ObservableProperty]
    private bool _bancoConectado;

    [ObservableProperty]
    private string _statusBanco = "Verificando banco de dados...";

    [ObservableProperty]
    private string _filtroTipo = "Todos"; // Todos, SemCodigo, ComCodigo

    [ObservableProperty]
    private string _textoBusca = "";

    [ObservableProperty]
    private ProdutoItemViewModel? _produtoSelecionado;

    [ObservableProperty]
    private ConfiguracaoEtiqueta _config = new();

    [ObservableProperty]
    private BitmapSource? _previewImagem;

    [ObservableProperty]
    private string _zplCodigo = "";

    [ObservableProperty]
    private double _zoomNivel = 1.5;

    [ObservableProperty]
    private int _totalProdutos;

    [ObservableProperty]
    private int _produtosComCodigo;

    [ObservableProperty]
    private int _produtosSemCodigo;

    [ObservableProperty]
    private int _totalEtiquetasFila;

    [ObservableProperty]
    private int _totalPaginasFila;

    [ObservableProperty]
    private bool _estaCarregando;

    public ObservableCollection<ProdutoItemViewModel> TodosProdutos { get; } = new();
    public ICollectionView ProdutosView { get; }
    public ObservableCollection<ItemFila> FilaImpressao { get; } = new();
    public IReadOnlyList<FormatoOpcao> FormatosDisponiveis => FormatoOpcao.ListaPadrao;

    public MainViewModel(BancoDadosService? dbService = null)
    {
        _dbService = dbService ?? new BancoDadosService();
        _caminhoBanco = _dbService.CaminhoAtual;

        ProdutosView = CollectionViewSource.GetDefaultView(TodosProdutos);
        ProdutosView.Filter = FiltrarProduto;

        FilaImpressao.CollectionChanged += (_, _) => AtualizarTotaisFila();

        Config.PropertyChanged += (_, _) => AtualizarPreview();

        _ = InicializarAsync();
    }

    private async Task InicializarAsync()
    {
        await RecarregarDadosAsync();
    }

    private bool FiltrarProduto(object obj)
    {
        if (obj is not ProdutoItemViewModel item) return false;

        // Filtro por tipo de código
        if (FiltroTipo == "SemCodigo" && item.TemCodigoBarras) return false;
        if (FiltroTipo == "ComCodigo" && !item.TemCodigoBarras) return false;

        // Filtro por texto
        if (string.IsNullOrWhiteSpace(TextoBusca)) return true;

        var busca = TextoBusca.Trim().ToLowerInvariant();
        return item.Nome.ToLowerInvariant().Contains(busca)
            || (item.Referencia?.ToLowerInvariant().Contains(busca) ?? false)
            || (item.Categoria?.ToLowerInvariant().Contains(busca) ?? false)
            || (item.CodigoInterno?.ToLowerInvariant().Contains(busca) ?? false)
            || (item.CodigoBarras?.ToLowerInvariant().Contains(busca) ?? false);
    }

    partial void OnTextoBuscaChanged(string value) => ProdutosView.Refresh();

    partial void OnFiltroTipoChanged(string value) => ProdutosView.Refresh();

    partial void OnProdutoSelecionadoChanged(ProdutoItemViewModel? value)
    {
        AtualizarPreview();
    }

    [RelayCommand]
    public async Task RecarregarDadosAsync()
    {
        EstaCarregando = true;
        try
        {
            CaminhoBanco = _dbService.CaminhoAtual;
            BancoConectado = _dbService.BancoExiste();

            if (!BancoConectado)
            {
                StatusBanco = $"Arquivo não encontrado: {Path.GetFileName(CaminhoBanco)}";
                TodosProdutos.Clear();
                AtualizarContadores();
                return;
            }

            var loja = await _dbService.ObterConfiguracaoLojaAsync();
            if (loja != null && !string.IsNullOrWhiteSpace(loja.NomeLoja))
            {
                Config.NomeLoja = loja.NomeLoja;
            }

            var produtos = await _dbService.ObterProdutosAsync();
            TodosProdutos.Clear();
            foreach (var p in produtos)
            {
                TodosProdutos.Add(new ProdutoItemViewModel(p));
            }

            AtualizarContadores();
            StatusBanco = $"Conectado ao Nexus LS ({TodosProdutos.Count} produtos ativos)";

            if (ProdutoSelecionado == null && TodosProdutos.Count > 0)
            {
                ProdutoSelecionado = TodosProdutos[0];
            }
            else
            {
                AtualizarPreview();
            }
        }
        catch (Exception ex)
        {
            StatusBanco = $"Erro ao conectar: {ex.Message}";
        }
        finally
        {
            EstaCarregando = false;
        }
    }

    private void AtualizarContadores()
    {
        TotalProdutos = TodosProdutos.Count;
        ProdutosComCodigo = TodosProdutos.Count(p => p.TemCodigoBarras);
        ProdutosSemCodigo = TodosProdutos.Count(p => !p.TemCodigoBarras);
    }

    [RelayCommand]
    public async Task SelecionarBancoAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecionar Banco de Dados do Nexus LS (SQLite)",
            Filter = "Banco SQLite (*.db;*.sqlite)|*.db;*.sqlite|Todos os arquivos (*.*)|*.*",
            InitialDirectory = Path.GetDirectoryName(CaminhoBanco) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        if (dialog.ShowDialog() == true)
        {
            _dbService.DefinirCaminho(dialog.FileName);
            await RecarregarDadosAsync();
        }
    }

    [RelayCommand]
    public async Task GerarCodigoIndividualAsync()
    {
        if (ProdutoSelecionado == null)
        {
            MessageBox.Show("Selecione um produto na lista primeiro.", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
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

                MessageBox.Show(
                    $"Código de barras gerado e salvo com sucesso!\n\nProduto: {ProdutoSelecionado.Nome}\nCódigo EAN-13: {novoCodigo}",
                    "Nexus BarTender",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro ao gerar código: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public async Task GerarCodigosEmLoteAsync()
    {
        var semCodigo = TodosProdutos.Where(p => !p.TemCodigoBarras).ToList();
        if (semCodigo.Count == 0)
        {
            MessageBox.Show("Todos os produtos cadastrados já possuem código de barras!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmacao = MessageBox.Show(
            $"Foram encontrados {semCodigo.Count} produtos sem código de barras.\n\n" +
            "Deseja gerar códigos EAN-13 únicos para todos eles e gravar diretamente no banco de dados do Nexus LS?",
            "Confirmação - Geração em Lote",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmacao != MessageBoxResult.Yes) return;

        EstaCarregando = true;
        try
        {
            var ids = semCodigo.Select(p => p.Id).ToList();
            var alterados = await _dbService.GerarCodigosLoteAsync(ids, Config.TipoCodigo);

            // Recarrega do banco para garantir integridade
            await RecarregarDadosAsync();

            MessageBox.Show(
                $"{alterados} códigos de barras foram gerados e gravados com sucesso no banco de dados do Nexus LS!",
                "Geração Concluída",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Falha na geração em lote: {ex.Message}", "Erro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            EstaCarregando = false;
        }
    }

    [RelayCommand]
    public void AdicionarSelecionadoFila()
    {
        if (ProdutoSelecionado == null) return;

        var existente = FilaImpressao.FirstOrDefault(i => i.Produto.Id == ProdutoSelecionado.Id);
        if (existente != null)
        {
            existente.Quantidade++;
        }
        else
        {
            FilaImpressao.Add(new ItemFila(ProdutoSelecionado.Produto, 1));
        }

        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void AdicionarTodosFila()
    {
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().ToList();
        if (filtrados.Count == 0) return;

        foreach (var p in filtrados)
        {
            var existente = FilaImpressao.FirstOrDefault(i => i.Produto.Id == p.Id);
            if (existente != null)
            {
                existente.Quantidade++;
            }
            else
            {
                FilaImpressao.Add(new ItemFila(p.Produto, 1));
            }
        }

        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void PuxarEstoqueAtual()
    {
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().ToList();
        if (filtrados.Count == 0) return;

        var adicionados = 0;
        var totalEtiquetas = 0;

        foreach (var p in filtrados)
        {
            var qtd = p.EstoqueAtual > 0 ? p.EstoqueAtual : 1;
            var existente = FilaImpressao.FirstOrDefault(i => i.Produto.Id == p.Id);
            if (existente != null)
            {
                existente.Quantidade = qtd;
            }
            else
            {
                FilaImpressao.Add(new ItemFila(p.Produto, qtd));
            }
            adicionados++;
            totalEtiquetas += qtd;
        }

        AtualizarTotaisFila();

        MessageBox.Show(
            $"Fila atualizada com base no estoque!\n\n{adicionados} produtos adicionados/atualizados, totalizando {totalEtiquetas} etiquetas.",
            "Nexus BarTender",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    [RelayCommand]
    public void IncrementarItem(ItemFila? item)
    {
        if (item == null) return;
        item.Quantidade++;
        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void DecrementarItem(ItemFila? item)
    {
        if (item == null) return;
        if (item.Quantidade > 1)
        {
            item.Quantidade--;
            AtualizarTotaisFila();
        }
        else
        {
            RemoverItemFila(item);
        }
    }

    [RelayCommand]
    public void RemoverItemFila(ItemFila? item)
    {
        if (item == null) return;
        FilaImpressao.Remove(item);
        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void LimparFila()
    {
        if (FilaImpressao.Count == 0) return;

        var confirmacao = MessageBox.Show(
            "Deseja limpar todos os itens da fila de impressão?",
            "Limpar Fila",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmacao == MessageBoxResult.Yes)
        {
            FilaImpressao.Clear();
            AtualizarTotaisFila();
        }
    }

    private void AtualizarTotaisFila()
    {
        TotalEtiquetasFila = FilaImpressao.Sum(i => i.Quantidade);
        var porPagina = Config.Formato.FormatoCore.EtiquetasPorPagina;
        TotalPaginasFila = porPagina > 0
            ? (int)Math.Ceiling((double)TotalEtiquetasFila / porPagina)
            : TotalEtiquetasFila;
    }

    [RelayCommand]
    public void Imprimir()
    {
        if (FilaImpressao.Count == 0)
        {
            MessageBox.Show("Adicione produtos à fila de impressão antes de imprimir.", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var imprimiu = ImpressaoService.Imprimir(FilaImpressao, Config);
            if (imprimiu)
            {
                var limpar = MessageBox.Show(
                    "Impressão enviada com sucesso!\n\nDeseja limpar a fila de impressão agora?",
                    "Nexus BarTender",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (limpar == MessageBoxResult.Yes)
                {
                    FilaImpressao.Clear();
                    AtualizarTotaisFila();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erro durante a impressão: {ex.Message}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    public void CopiarZpl()
    {
        if (string.IsNullOrWhiteSpace(ZplCodigo)) return;
        Clipboard.SetText(ZplCodigo);
        MessageBox.Show("Comandos ZPL II copiados para a Área de Transferência!", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
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

            MessageBox.Show($"Imagem salva em:\n{sfd.FileName}", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    [RelayCommand]
    public void ZoomMais() => ZoomNivel = Math.Min(3.0, ZoomNivel + 0.25);

    [RelayCommand]
    public void ZoomMenos() => ZoomNivel = Math.Max(0.5, ZoomNivel - 0.25);

    [RelayCommand]
    public void ZoomPadrao() => ZoomNivel = 1.5;

    public void AtualizarPreview()
    {
        try
        {
            var prod = ProdutoSelecionado?.Produto ?? new Produto
            {
                Nome = "CAMISETA OVERSIZED ALGODÃO EGÍPCIO",
                Referencia = "NX-9021",
                CodigoInterno = "INT-000100",
                CodigoBarras = "2000000000186",
                PrecoVendaCentavos = 14990,
                PrecoAtacadoCentavos = 9990
            };

            PreviewImagem = DesenhoEtiquetaService.RenderizarBitmap(prod, "M", Config, escalaDpi: 2.5);
            ZplCodigo = DesenhoEtiquetaService.GerarZpl(prod, Config);
            AtualizarTotaisFila();
        }
        catch
        {
            // Evita crash na prévia se algum dado for inválido
        }
    }
}
