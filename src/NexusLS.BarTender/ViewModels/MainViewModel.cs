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
    private bool _temItensNaFila;

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

    private readonly IDialogoService _dialogo;

    public MainViewModel() : this(null, null)
    {
    }

    public MainViewModel(BancoDadosService? dbService, IDialogoService? dialogoService = null)
    {
        _dialogo = dialogoService ?? DialogoService.Instancia;
        _designerVM = new DesignerViewModel(_dialogo)
        {
            SolicitarIrParaImpressao = IrParaFilaImpressaoAtalho
        };

        var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _licencaNuvem = new ServicoLicencaNuvem(
            new FonteLicencaComReserva(new FonteLicencaCloudFunction(http), new FonteLicencaFirestore(http)),
            ServicoLicencaNuvem.CaminhoPadrao());

        _dbService = dbService ?? new BancoDadosService();
        GerenciadorEtiquetasProdutoService.Banco = _dbService;
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
            catch (Exception exLog) { NexusLS.BarTender.App.Log($"MainViewModel #1: {exLog.Message}"); }
        }
        else
        {
            _dialogo.Informar("O aplicativo Atualizador e Ativador (atualizador.exe) não foi localizado nesta pasta.");
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
        catch (Exception exLog) { NexusLS.BarTender.App.Log($"MainViewModel #2: {exLog.Message}"); }
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
        AplicarPedidoDoNexus();
        IniciarVigiaDoBanco();
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

        private System.Windows.Threading.DispatcherTimer? _debounceBuscaTimer;

    partial void OnTextoBuscaChanged(string value)
    {
        if (_debounceBuscaTimer == null)
        {
            _debounceBuscaTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _debounceBuscaTimer.Tick += (_, _) =>
            {
                _debounceBuscaTimer?.Stop();
                ProdutosView.Refresh();
                AtualizarContadores();
            };
        }

        _debounceBuscaTimer.Stop();
        _debounceBuscaTimer.Start();
    }

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
            GerenciadorEtiquetasProdutoService.Invalidar();
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
            MarcarDadosCarregados();

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
    public void IrParaEstudio() => TelaAtiva = "Estudio";

    [RelayCommand]
    public void IrParaCatalogo() => TelaAtiva = "Catalogo";

    [RelayCommand]
    public void IrParaFila() => TelaAtiva = "Fila";

    [RelayCommand]
    public void IrParaFilaImpressaoAtalho()
    {
        UsarModeloDesigner = true;
        AtualizarMiniaturasFila();
        TelaAtiva = "Fila";
    }

    [RelayCommand]
    public void IrParaConfiguracoes() => TelaAtiva = "Configuracoes";

    /// <summary>Pergunta o tamanho a editar (null = cancelou). Troca nos testes.</summary>
    public Func<string, IReadOnlyList<string>, string?> EscolherTamanhoParaEditar { get; set; } = (produto, tamanhos) =>
    {
        var janela = new Views.EscolherTamanhoJanela(produto, tamanhos);
        return janela.ShowDialog() == true ? janela.Escolhido : null;
    };

    [RelayCommand]
    public void UsarProdutoNoEstudio(ProdutoItemViewModel? produtoItem)
    {
        if (produtoItem == null) return;

        // Mostra os tamanhos do produto; ao escolher um, o Estúdio abre a etiqueta já com ele.
        var tamanhos = (produtoItem.Produto.Tamanhos ?? new List<ProdutoTamanho>())
            .Select(t => (t.Tamanho ?? "").Trim())
            .Where(t => t.Length > 0 && !string.Equals(t, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        string? escolhido = null;
        if (tamanhos.Count == 1) escolhido = tamanhos[0];
        else if (tamanhos.Count > 1)
        {
            escolhido = EscolherTamanhoParaEditar(produtoItem.Produto.Nome, tamanhos);
            if (escolhido == null) return; // cancelou
        }

        ProdutoSelecionado = produtoItem;
        DesignerVM.DefinirProduto(produtoItem.Produto, Config.NomeLoja, carregarEtiquetaSalvaSeHouver: true);
        if (escolhido != null && DesignerVM.TamanhosParaTesteDisponiveis.Contains(escolhido))
            DesignerVM.TamanhoParaTeste = escolhido;
        TelaAtiva = "Estudio";
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
