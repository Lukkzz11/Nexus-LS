using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusLS.BarTender.Models;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Atualizacao;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly BancoDadosService _dbService;
    private readonly ServicoLicencaNuvem _licencaNuvem;
    private ProdutoItemViewModel? _produtoDaAssinatura;

    [ObservableProperty]
    private string _licencaTexto = "Verificando licença...";

    [ObservableProperty]
    private bool _licencaAtiva = true;

    [ObservableProperty]
    private bool _temAtualizacaoDisponivel;

    [ObservableProperty]
    private string _mensagemAtualizacao = "";

    [ObservableProperty]
    private string _caminhoBanco = "";

    [ObservableProperty]
    private bool _bancoConectado;

    [ObservableProperty]
    private string _statusBanco = "Verificando banco de dados...";

    [ObservableProperty]
    private string _telaAtiva = "Estudio"; // "Estudio", "Catalogo", "Fila", "Configuracoes"

    [ObservableProperty]
    private string _filtroTipo = "Todos"; // Todos, SemCodigo, ComCodigo

    [ObservableProperty]
    private string _textoBusca = "";

    [ObservableProperty]
    private ProdutoItemViewModel? _produtoSelecionado;

    [ObservableProperty]
    private ConfiguracaoEtiqueta _config = new();

    [ObservableProperty]
    private DesignerViewModel _designerVM = new();

    [ObservableProperty]
    private bool _usarModeloDesigner = true;

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

    [ObservableProperty]
    private string _categoriaSelecionada = "Todas";

    [ObservableProperty]
    private string? _impressoraSelecionada;

    [ObservableProperty]
    private int _posicaoInicialFolha = 1;

    public ObservableCollection<string> CategoriasDisponiveis { get; } = new() { "Todas" };
    public ObservableCollection<string> ImpressorasInstaladas { get; } = new();

    public ObservableCollection<ProdutoItemViewModel> TodosProdutos { get; } = new();
    public ICollectionView ProdutosView { get; }
    public ObservableCollection<ItemFila> FilaImpressao { get; } = new();
    public IReadOnlyList<FormatoOpcao> FormatosDisponiveis => FormatoOpcao.ListaPadrao;

    public MainViewModel() : this(null)
    {
    }

    public MainViewModel(BancoDadosService? dbService)
    {
        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _licencaNuvem = new ServicoLicencaNuvem(
            new FonteLicencaComReserva(new FonteLicencaCloudFunction(http), new FonteLicencaFirestore(http)),
            ServicoLicencaNuvem.CaminhoPadrao());

        _dbService = dbService ?? new BancoDadosService();
        _caminhoBanco = _dbService.CaminhoAtual;

        ProdutosView = CollectionViewSource.GetDefaultView(TodosProdutos);
        ProdutosView.Filter = FiltrarProduto;

        FilaImpressao.CollectionChanged += (_, _) => AtualizarTotaisFila();

        Config.PropertyChanged += (_, _) => AtualizarPreview();

        CarregarImpressoras();
        AtualizarLicenca();
        _ = InicializarAsync();
    }

    public void AtualizarLicenca()
    {
        var local = _licencaNuvem.AvaliarLocal();
        if (local.Estado != EstadoLicencaNuvem.NaoEncontrada)
        {
            LicencaTexto = local.Mensagem;
            LicencaAtiva = local.Ativa;
        }
        else
        {
            LicencaTexto = "Licença: Modo de Demonstração / Avaliação";
            LicencaAtiva = true;
        }
    }

    [RelayCommand]
    public void AbrirAtualizadorLicenca()
    {
        var caminhos = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "atualizador.exe"),
            Path.Combine(AppContext.BaseDirectory, "..", "atualizador.exe"),
            @"C:\Nexus\atualizador.exe",
            @"C:\NexusLS\atualizador.exe",
            @"C:\NexusLS\publicar\atualizador.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "NexusLS", "atualizador.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "atualizador.exe")
        };
        var exe = caminhos.FirstOrDefault(File.Exists);
        if (exe != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    Arguments = "--bartender",
                    WorkingDirectory = Path.GetDirectoryName(exe)!
                });
            }
            catch { }
        }
        else
        {
            MessageBox.Show("O aplicativo Atualizador e Ativador (atualizador.exe) não foi localizado nesta pasta.", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public async Task VerificarAtualizacaoAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("NexusBarTender/1.0");
            var repo = ConfiguracaoAtualizacao.Repositorio();
            var fonte = new FonteGitHub(http, repo, "NexusBarTender.exe", "NexusBarTender.exe.sha256");
            var versaoAtual = VersaoPrograma.Atual();
            var servico = new AtualizadorService(fonte, AtualizadorService.PastaPadrao(), versaoAtual);
            var res = await servico.VerificarAsync();
            if (res.Estado == EstadoVerificacao.Disponivel && res.Nova != null)
            {
                TemAtualizacaoDisponivel = true;
                MensagemAtualizacao = $"Nova versão {res.Nova.Versao} disponível!";
            }
        }
        catch { }
    }

    public void CarregarImpressoras()
    {
        ImpressorasInstaladas.Clear();
        try
        {
            foreach (string printer in System.Drawing.Printing.PrinterSettings.InstalledPrinters)
            {
                ImpressorasInstaladas.Add(printer);
            }
            var padrao = new System.Drawing.Printing.PrinterSettings().PrinterName;
            ImpressoraSelecionada = ImpressorasInstaladas.Contains(padrao) ? padrao : ImpressorasInstaladas.FirstOrDefault();
        }
        catch
        {
            ImpressorasInstaladas.Add("Impressora Padrão do Windows");
            ImpressoraSelecionada = "Impressora Padrão do Windows";
        }
    }

    private async Task InicializarAsync()
    {
        await RecarregarDadosAsync();
        _ = VerificarAtualizacaoAsync();
    }

    private bool FiltrarProduto(object obj)
    {
        if (obj is not ProdutoItemViewModel item) return false;

        // Filtro por categoria
        if (CategoriaSelecionada != "Todas" && !string.Equals(item.Categoria, CategoriaSelecionada, StringComparison.OrdinalIgnoreCase))
            return false;

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

    partial void OnCategoriaSelecionadaChanged(string value) => ProdutosView.Refresh();

    partial void OnProdutoSelecionadoChanged(ProdutoItemViewModel? value)
    {
        if (_produtoDaAssinatura != null)
            _produtoDaAssinatura.PropertyChanged -= ProdutoSelecionado_PropertyChanged;
        _produtoDaAssinatura = value;
        if (_produtoDaAssinatura != null)
            _produtoDaAssinatura.PropertyChanged += ProdutoSelecionado_PropertyChanged;

        AtualizarPreview();
        DesignerVM.DefinirProduto(value?.Produto, Config.NomeLoja);
    }

    private void ProdutoSelecionado_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProdutoItemViewModel.TamanhoSelecionado))
            AtualizarPreview();
    }

    [RelayCommand]
    public async Task RecarregarDadosAsync()
    {
        App.Log("RecarregarDadosAsync BEGIN");
        EstaCarregando = true;
        try
        {
            CaminhoBanco = _dbService.CaminhoAtual;
            BancoConectado = _dbService.BancoExiste();
            App.Log($"RecarregarDadosAsync: BancoConectado={BancoConectado}");

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
                DesignerVM.NomeLoja = loja.NomeLoja;
            }

            var produtos = await _dbService.ObterProdutosAsync();
            App.Log($"RecarregarDadosAsync: Obtidos {produtos.Count} produtos do SQLite");
            TodosProdutos.Clear();
            foreach (var p in produtos)
            {
                TodosProdutos.Add(new ProdutoItemViewModel(p));
            }

            AtualizarContadores();
            StatusBanco = $"Conectado ao Nexus LS ({TodosProdutos.Count} produtos ativos)";

            CategoriasDisponiveis.Clear();
            CategoriasDisponiveis.Add("Todas");
            var cats = produtos.Select(p => p.Categoria).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().OrderBy(c => c);
            foreach (var c in cats) CategoriasDisponiveis.Add(c!);

            DesignerVM.SincronizarProdutos(TodosProdutos);

            if (ProdutoSelecionado == null && TodosProdutos.Count > 0)
            {
                App.Log("RecarregarDadosAsync: Definindo ProdutoSelecionado = primeiro");
                ProdutoSelecionado = TodosProdutos[0];
            }
            else
            {
                AtualizarPreview();
            }
            App.Log("RecarregarDadosAsync SUCCESS");
        }
        catch (Exception ex)
        {
            App.Log($"RecarregarDadosAsync EXCEPTION: {ex}");
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
            MessageBox.Show("Todos os produtos já têm código na etiqueta (EAN-13, referência + tamanho, código curto ou interno).", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var confirmacao = MessageBox.Show(
            $"Foram encontrados {semCodigo.Count} produtos sem nenhum código: nem EAN-13, nem referência, nem código interno.\n\n" +
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

        foreach (var tamanho in TamanhosParaAdicionar(ProdutoSelecionado))
            AdicionarNaFila(ProdutoSelecionado.Produto, tamanho, 1);

        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void AdicionarTodosFila()
    {
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().ToList();
        if (filtrados.Count == 0) return;

        foreach (var p in filtrados)
            foreach (var tamanho in TamanhosParaAdicionar(p))
                AdicionarNaFila(p.Produto, tamanho, 1);

        AtualizarTotaisFila();
    }

    [RelayCommand]
    public void PuxarEstoqueAtual()
    {
        var filtrados = ProdutosView.Cast<ProdutoItemViewModel>().ToList();
        if (filtrados.Count == 0) return;

        var linhas = 0;

        foreach (var p in filtrados)
        {
            var tamanhos = TamanhosParaAdicionar(p);
            var porTamanho = TemGrade(p.Produto) &&
                string.Equals(p.TamanhoSelecionado, ProdutoItemViewModel.TodosOsTamanhos, StringComparison.OrdinalIgnoreCase);
            var adicionou = 0;

            // Numa puxada da grade inteira, a fila do produto passa a refletir só os tamanhos com estoque.
            if (porTamanho)
            {
                var permitidos = new HashSet<string>(tamanhos, StringComparer.OrdinalIgnoreCase);
                foreach (var existente in FilaImpressao.Where(i => i.Produto.Id == p.Id).ToList())
                    if (!permitidos.Contains(existente.Tamanho ?? ""))
                        FilaImpressao.Remove(existente);
            }

            foreach (var tamanho in tamanhos)
            {
                var cadastrado = LocalizarTamanho(p.Produto, tamanho);
                var estoque = cadastrado?.Estoque ?? p.EstoqueAtual;

                // Numa grade inteira, tamanho zerado não gera etiqueta.
                if (porTamanho && estoque <= 0) continue;

                DefinirNaFila(p.Produto, tamanho, Math.Max(1, estoque));
                adicionou++;
                linhas++;
            }

            // Produto sem grade continua com o comportamento antigo: nunca fica zerado na fila.
            if (adicionou == 0 && !porTamanho)
            {
                DefinirNaFila(p.Produto, "", 1);
                linhas++;
            }
        }

        AtualizarTotaisFila();

        MessageBox.Show(
            $"Fila atualizada com base no estoque!\n\n{linhas} linha(s) na fila, totalizando {TotalEtiquetasFila} etiquetas.",
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
    public void AdicionarItemComQuantidadeFila(ProdutoItemViewModel? produtoItem)
    {
        if (produtoItem == null) return;
        var qtd = Math.Max(1, produtoItem.QuantidadeAdicionar);

        foreach (var tamanho in TamanhosParaAdicionar(produtoItem))
            AdicionarNaFila(produtoItem.Produto, tamanho, qtd);

        AtualizarTotaisFila();
    }

    /// <summary>Produto tem pelo menos um tamanho de verdade (P, M, G, 38...), além de "Sem tamanho".</summary>
    private static bool TemGrade(Produto produto) =>
        produto.Tamanhos?.Any(t => !EhSemTamanho(t.Tamanho)) == true;

    private static bool EhSemTamanho(string? tamanho) =>
        string.IsNullOrWhiteSpace(tamanho) ||
        string.Equals(tamanho.Trim(), Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase);

    /// <summary>Tamanhos reais da grade, na ordem cadastrada (a mesma que forma o código de barras).</summary>
    private static List<string> TamanhosDaGrade(Produto produto)
    {
        var lista = (produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Where(t => !EhSemTamanho(t.Tamanho))
            .OrderBy(t => t.Ordem > 0 ? t.Ordem : int.MaxValue)
            .ThenBy(t => t.Tamanho)
            .Select(t => t.Tamanho.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Se o produto também tem estoque "Sem tamanho", ele entra no fim (código sem número de tamanho).
        if (produto.Tamanhos?.Any(t => EhSemTamanho(t.Tamanho) && t.Estoque > 0) == true)
            lista.Add("");

        return lista;
    }

    /// <summary>
    /// O que deve entrar na fila para este produto: o tamanho escolhido na tela ou, quando está em
    /// "Todos os tamanhos", um item por tamanho da grade.
    /// </summary>
    private static IReadOnlyList<string> TamanhosParaAdicionar(ProdutoItemViewModel item)
    {
        var escolhido = item.TamanhoSelecionado?.Trim() ?? "";
        if (escolhido.Length > 0 &&
            !string.Equals(escolhido, ProdutoItemViewModel.TodosOsTamanhos, StringComparison.OrdinalIgnoreCase))
        {
            return new[] { EhSemTamanho(escolhido) ? "" : escolhido };
        }

        var grade = TamanhosDaGrade(item.Produto);
        return grade.Count > 0 ? grade : new[] { "" };
    }

    /// <summary>Linha da grade que corresponde ao tamanho ("" e "Sem tamanho" são a mesma linha).</summary>
    private static ProdutoTamanho? LocalizarTamanho(Produto produto, string tamanho) =>
        produto.Tamanhos?.FirstOrDefault(t =>
            EhSemTamanho(tamanho) ? EhSemTamanho(t.Tamanho)
                : string.Equals(t.Tamanho?.Trim(), tamanho.Trim(), StringComparison.OrdinalIgnoreCase));

    private ItemFila? LocalizarNaFila(Produto produto, string tamanho) =>
        FilaImpressao.FirstOrDefault(i =>
            i.Produto.Id == produto.Id &&
            string.Equals(i.Tamanho ?? "", tamanho ?? "", StringComparison.OrdinalIgnoreCase));

    private ItemFila AdicionarNaFila(Produto produto, string tamanho, int quantidade)
    {
        var existente = LocalizarNaFila(produto, tamanho);
        if (existente != null)
        {
            existente.Quantidade += Math.Max(1, quantidade);
            return existente;
        }

        var novo = new ItemFila(produto, quantidade, tamanho);
        FilaImpressao.Add(novo);
        return novo;
    }

    private void DefinirNaFila(Produto produto, string tamanho, int quantidade)
    {
        var existente = LocalizarNaFila(produto, tamanho);
        if (existente != null)
            existente.Quantidade = Math.Max(1, quantidade);
        else
            FilaImpressao.Add(new ItemFila(produto, quantidade, tamanho));
    }

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

        return MessageBox.Show(
            $"Confira antes de imprimir:\n\n• {detalhe}\n\nDeseja imprimir mesmo assim?",
            "Revisão da fila de impressão",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private static bool EhEan13(string codigo) =>
        codigo.Length == 13 && codigo.All(char.IsAsciiDigit);

    [RelayCommand]
    public void Imprimir()
    {
        if (FilaImpressao.Count == 0)
        {
            MessageBox.Show("Adicione produtos à fila de impressão antes de imprimir.", "Nexus BarTender", MessageBoxButton.OK, MessageBoxImage.Information);
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
    public void IrParaEstudio() => TelaAtiva = "Estudio";

    [RelayCommand]
    public void IrParaCatalogo() => TelaAtiva = "Catalogo";

    [RelayCommand]
    public void IrParaFila() => TelaAtiva = "Fila";

    [RelayCommand]
    public void IrParaConfiguracoes() => TelaAtiva = "Configuracoes";

    [RelayCommand]
    public void UsarProdutoNoEstudio(ProdutoItemViewModel? produtoItem)
    {
        if (produtoItem != null)
        {
            ProdutoSelecionado = produtoItem;
            DesignerVM.DefinirProduto(produtoItem.Produto, Config.NomeLoja, carregarEtiquetaSalvaSeHouver: true);
            TelaAtiva = "Estudio";
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

            var tamanhoPreview = "M";
            var selecionado = ProdutoSelecionado;
            if (selecionado is not null)
            {
                if (string.Equals(selecionado.TamanhoSelecionado, ProdutoItemViewModel.TodosOsTamanhos, StringComparison.OrdinalIgnoreCase))
                    tamanhoPreview = TamanhosDaGrade(selecionado.Produto).FirstOrDefault(t => t.Length > 0) ?? "";
                else
                    tamanhoPreview = EhSemTamanho(selecionado.TamanhoSelecionado) ? "" : selecionado.TamanhoSelecionado;
            }

            PreviewImagem = DesenhoEtiquetaService.RenderizarBitmap(prod, tamanhoPreview, Config, escalaDpi: 2.5);
            ZplCodigo = DesenhoEtiquetaService.GerarZpl(prod, Config, 1, tamanhoPreview);
            AtualizarTotaisFila();
        }
        catch
        {
            // Evita crash na prévia se algum dado for inválido
        }
    }
}
