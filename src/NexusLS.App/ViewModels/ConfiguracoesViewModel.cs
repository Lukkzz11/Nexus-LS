using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Impressao;
using NexusLS.Core.Backup;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoLargura(string Texto, int Colunas);

/// <summary>Um modelo de comprovante na lista de escolha.</summary>
public sealed partial class OpcaoModeloCupom : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public OpcaoModeloCupom(int valor, string nome) { Valor = valor; Nome = nome; }
    public int Valor { get; }
    public string Nome { get; }
    public Action<OpcaoModeloCupom>? AoEscolher { get; set; }

    private bool _escolhido;
    public bool Escolhido
    {
        get => _escolhido;
        set { if (SetProperty(ref _escolhido, value) && value) AoEscolher?.Invoke(this); }
    }
}

/// <summary>Um pedaço de linha da prévia do cupom, com o peso e o tamanho da letra já escolhidos.</summary>
public record SegmentoPrevia(string Texto, System.Windows.FontWeight Peso, double Tamanho = 12);

/// <summary>Dados da loja e do cupom, com uma prévia do cupom que muda enquanto você digita.</summary>
public partial class ConfiguracoesViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private readonly ConfiguracaoService _configuracao;
    private readonly CupomService _cupons;

    public ConfiguracoesViewModel(ConfiguracaoService configuracao, CupomService cupons,
        GestorBackup? backup = null, NexusLS.App.Backup.IDialogosBackup? dialogos = null,
        LimparDadosViewModel? limparDados = null, AtualizacaoViewModel? atualizacao = null)
    {
        LimparDados = limparDados;
        Atualizacao = atualizacao;
        _configuracao = configuracao;
        _cupons = cupons;
        _backup = backup;
        _dialogos = dialogos;
        _larguraSelecionada = Larguras[1];
        Modelos[0].Escolhido = true;                                   // antes de ligar o clique: não aplica o ajuste do modelo
        foreach (var m in Modelos) m.AoEscolher = EscolherModelo;
        _impressoraSelecionada = ImpressoraWindows.ImpressoraPadrao;
        Impressoras.Add(ImpressoraWindows.ImpressoraPadrao);
        foreach (var nome in ImpressoraWindows.ListarImpressoras()) Impressoras.Add(nome);
        _ = CarregarAsync();
        CarregarBackup();
    }

    public IReadOnlyList<OpcaoLargura> Larguras { get; } = new[]
    {
        new OpcaoLargura("58 mm · 32 colunas", 32),
        new OpcaoLargura("80 mm · 42 colunas", 42),
        new OpcaoLargura("80 mm · 48 colunas (letra menor)", 48)
    };

    public ObservableCollection<string> Impressoras { get; } = new();

    [ObservableProperty] private string _nomeLoja = "";
    [ObservableProperty] private string _documento = "";
    [ObservableProperty] private string _endereco = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _nomeOperador = "Caixa";
    [ObservableProperty] private string _rodapeCupom = "";
    [ObservableProperty] private string _impressoraSelecionada;
    [ObservableProperty] private OpcaoLargura _larguraSelecionada;
    [ObservableProperty] private bool _imprimirAoFinalizar = true;
    [ObservableProperty] private bool _perguntarTamanho = true;
    [ObservableProperty] private bool _abrirNoPdv;
    [ObservableProperty] private string _previa = "";
    /// <summary>A mesma prévia, linha a linha, com os pedaços em negrito (é o que a tela desenha).</summary>
    [ObservableProperty] private IReadOnlyList<IReadOnlyList<SegmentoPrevia>> _linhasPrevia = Array.Empty<IReadOnlyList<SegmentoPrevia>>();
    [ObservableProperty] private string _segmento = "";
    [ObservableProperty] private string _enderecoNumero = "";
    [ObservableProperty] private string _bairro = "";
    [ObservableProperty] private string _cep = "";
    [ObservableProperty] private bool _cupomMostrarLogo = true;
    [ObservableProperty] private System.Windows.Media.ImageSource? _logoPrevia;
    [ObservableProperty] private bool _temLogo;

    private byte[]? _logoBytes;
    private bool _logoAlterado;

    /// <summary>Escolhe o arquivo do logo (o programa abre a caixa de arquivos; os testes trocam por um caminho fixo).</summary>
    public Func<string?> EscolherArquivoLogo { get; set; } = EscolherArquivoPadrao;

    private static string? EscolherArquivoPadrao()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog { Title = "Escolher o logo do cupom", Filter = "Imagens|*.png;*.jpg;*.jpeg;*.bmp|Todos os arquivos|*.*" };
        return dialogo.ShowDialog() == true ? dialogo.FileName : null;
    }

    /// <summary>Define o logo a partir dos bytes de uma imagem qualquer (a imagem é reduzida e convertida em PNG).</summary>
    public void DefinirLogo(byte[]? arquivo)
    {
        _logoBytes = arquivo is { Length: > 0 } ? LogoCupom.Preparar(arquivo) : null;
        _logoAlterado = true;
        AtualizarLogo();
        AtualizarPrevia();
    }

    private void AtualizarLogo()
    {
        TemLogo = _logoBytes is not null;
        LogoPrevia = CupomMostrarLogo ? LogoCupom.ParaImpressao(_logoBytes) : null;
    }

    [RelayCommand]
    private Task EnviarLogoAsync() => ExecutarAsync(() =>
    {
        var caminho = EscolherArquivoLogo();
        if (string.IsNullOrWhiteSpace(caminho)) return Task.CompletedTask;
        DefinirLogo(System.IO.File.ReadAllBytes(caminho));
        MostrarSucesso("Logo carregado. Clique em Salvar para guardar.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private void RemoverLogo()
    {
        DefinirLogo(null);
        MostrarSucesso("Logo removido. Clique em Salvar para guardar.");
    }

    partial void OnCupomMostrarLogoChanged(bool value) { AtualizarLogo(); AtualizarPrevia(); }
    partial void OnSegmentoChanged(string value) => AtualizarPrevia();
    partial void OnEnderecoNumeroChanged(string value) => AtualizarPrevia();
    partial void OnBairroChanged(string value) => AtualizarPrevia();
    partial void OnCepChanged(string value)
    {
        var f = ValidadorDocumento.FormatarCepDigitando(value);
        if (f != value) Cep = f; else AtualizarPrevia();
    }

    // Empresa
    [ObservableProperty] private string _razaoSocial = "";
    [ObservableProperty] private string _inscricaoEstadual = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _cidadeUf = "";
    [ObservableProperty] private string _chavePix = "";
    // O que mostrar no cupom
    [ObservableProperty] private bool _cupomMostrarRazaoSocial = true;
    [ObservableProperty] private bool _cupomMostrarDocumento = true;
    [ObservableProperty] private bool _cupomMostrarEndereco = true;
    [ObservableProperty] private bool _cupomMostrarTelefone = true;
    [ObservableProperty] private bool _cupomMostrarPix;
    [ObservableProperty] private bool _cupomMostrarCidade = true;
    [ObservableProperty] private bool _cupomMostrarInscricao = true;
    [ObservableProperty] private bool _cupomMostrarEmail;
    [ObservableProperty] private bool _cupomMostrarOperador = true;
    [ObservableProperty] private bool _cupomMostrarCliente = true;
    [ObservableProperty] private bool _cupomMostrarPagamento = true;
    [ObservableProperty] private bool _cupomMostrarAssinatura;
    [ObservableProperty] private int _cupomModelo;
    private bool _sincronizandoModelo;

    /// <summary>Os modelos de comprovante para escolher (Life, Compacto, Comprovante de venda).</summary>
    public IReadOnlyList<OpcaoModeloCupom> Modelos { get; } = CriarModelos();
    private static List<OpcaoModeloCupom> CriarModelos() => new()
    {
        new(0, "Life (padrão do ERP)"), new(1, "Compacto"), new(2, "Comprovante de venda")
    };

    partial void OnCupomModeloChanged(int value)
    {
        _sincronizandoModelo = true;
        foreach (var m in Modelos) m.Escolhido = m.Valor == value;
        _sincronizandoModelo = false;
        AtualizarPrevia();
    }

    /// <summary>A pessoa clicou num modelo: ele vira o modelo atual e ajusta as opções que combinam com ele.</summary>
    private void EscolherModelo(OpcaoModeloCupom modelo)
    {
        if (_sincronizandoModelo) return;
        CupomModelo = modelo.Valor;
        switch (modelo.Valor)
        {
            case 0: CupomTituloEmCaixa = true; CupomMostrarReferencia = false; CupomMostrarAssinaturaCliente = false; break;
            case 1: CupomTituloEmCaixa = false; CupomMostrarReferencia = false; CupomMostrarAssinaturaCliente = false; break;
            default:
                CupomTituloEmCaixa = false; CupomMostrarReferencia = true; CupomMostrarAssinaturaCliente = true;
                if (string.IsNullOrWhiteSpace(CupomTextoGarantia)) CupomTextoGarantia = "Troca em até 7 dias com a etiqueta e este comprovante.";
                break;
        }
    }

    [ObservableProperty] private bool _cupomMostrarReferencia;
    [ObservableProperty] private bool _cupomTituloEmCaixa;
    [ObservableProperty] private bool _cupomMostrarAssinaturaCliente;
    [ObservableProperty] private string _cupomTextoGarantia = "";
    partial void OnCupomMostrarReferenciaChanged(bool value) => AtualizarPrevia();
    partial void OnCupomTituloEmCaixaChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarAssinaturaClienteChanged(bool value) => AtualizarPrevia();
    partial void OnCupomTextoGarantiaChanged(string value) => AtualizarPrevia();
    [ObservableProperty] private string _cupomMensagemCabecalho = "";
    [ObservableProperty] private string _cupomLinhasFinais = "1";
    // Parâmetros de operação
    [ObservableProperty] private string _fundoTrocoPadrao = "";
    [ObservableProperty] private string _estoqueMinimoPadrao = "0";
    [ObservableProperty] private string _margemPadrao = "0";

    partial void OnNomeLojaChanged(string value) => AtualizarPrevia();
    // CNPJ/CPF, telefone e CEP ficam sempre com a máscara (também os valores que já estavam salvos sem ela)
    partial void OnDocumentoChanged(string value)
    {
        var f = ValidadorDocumento.FormatarDigitando(value);
        if (f != value) Documento = f; else AtualizarPrevia();
    }
    partial void OnEnderecoChanged(string value) => AtualizarPrevia();
    partial void OnTelefoneChanged(string value)
    {
        var f = ValidadorDocumento.FormatarTelefoneDigitando(value);
        if (f != value) Telefone = f; else AtualizarPrevia();
    }
    partial void OnRodapeCupomChanged(string value) => AtualizarPrevia();
    partial void OnRazaoSocialChanged(string value) => AtualizarPrevia();
    partial void OnInscricaoEstadualChanged(string value) => AtualizarPrevia();
    partial void OnCidadeUfChanged(string value) => AtualizarPrevia();
    partial void OnChavePixChanged(string value) => AtualizarPrevia();
    partial void OnCupomMostrarRazaoSocialChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarDocumentoChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarEnderecoChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarTelefoneChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarPixChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarCidadeChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarInscricaoChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarEmailChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarOperadorChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarClienteChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarPagamentoChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMostrarAssinaturaChanged(bool value) => AtualizarPrevia();
    partial void OnCupomMensagemCabecalhoChanged(string value) => AtualizarPrevia();
    partial void OnEmailChanged(string value) => AtualizarPrevia();
    partial void OnNomeOperadorChanged(string value) => AtualizarPrevia();
    partial void OnCupomLinhasFinaisChanged(string value) => AtualizarPrevia();
    partial void OnLarguraSelecionadaChanged(OpcaoLargura value) => AtualizarPrevia();

    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var c = await _configuracao.ObterAsync();

        NomeLoja = c.NomeLoja;
        Documento = c.Documento ?? "";
        Endereco = c.Endereco ?? "";
        Telefone = c.Telefone ?? "";
        NomeOperador = c.NomeOperador;
        RodapeCupom = c.MensagemCupom ?? "";
        ImprimirAoFinalizar = c.ImprimirCupomAoFinalizar;
        PerguntarTamanho = c.PerguntarTamanho;
        AbrirNoPdv = c.AbrirNoPdv;
        RazaoSocial = c.RazaoSocial ?? "";
        InscricaoEstadual = c.InscricaoEstadual ?? "";
        Email = c.Email ?? "";
        CidadeUf = c.CidadeUf ?? "";
        Segmento = c.Segmento ?? "";
        EnderecoNumero = c.EnderecoNumero ?? "";
        Bairro = c.Bairro ?? "";
        Cep = c.Cep ?? "";
        CupomMostrarLogo = c.CupomMostrarLogo;
        _logoBytes = c.LogoCupom;
        _logoAlterado = false;
        AtualizarLogo();
        ChavePix = c.ChavePix ?? "";
        CupomMostrarRazaoSocial = c.CupomMostrarRazaoSocial;
        CupomMostrarDocumento = c.CupomMostrarDocumento;
        CupomMostrarEndereco = c.CupomMostrarEndereco;
        CupomMostrarTelefone = c.CupomMostrarTelefone;
        CupomMostrarPix = c.CupomMostrarPix;
        CupomMostrarCidade = c.CupomMostrarCidade;
        CupomMostrarInscricao = c.CupomMostrarInscricao;
        CupomMostrarEmail = c.CupomMostrarEmail;
        CupomMostrarOperador = c.CupomMostrarOperador;
        CupomMostrarCliente = c.CupomMostrarCliente;
        CupomMostrarPagamento = c.CupomMostrarPagamento;
        CupomMostrarAssinatura = c.CupomMostrarAssinatura;
        CupomModelo = c.CupomModelo;
        CupomMostrarReferencia = c.CupomMostrarReferencia;
        CupomTituloEmCaixa = c.CupomTituloEmCaixa;
        CupomMostrarAssinaturaCliente = c.CupomMostrarAssinaturaCliente;
        CupomTextoGarantia = c.CupomTextoGarantia ?? "";
        CupomMensagemCabecalho = c.CupomMensagemCabecalho ?? "";
        CupomLinhasFinais = c.CupomLinhasFinais.ToString();
        FundoTrocoPadrao = c.FundoTrocoPadraoCentavos > 0 ? Dinheiro.FormatarParaEdicao(c.FundoTrocoPadraoCentavos) : "";
        EstoqueMinimoPadrao = c.EstoqueMinimoPadrao.ToString();
        MargemPadrao = c.MargemPadraoPercentual.ToString();
        LarguraSelecionada = Larguras.FirstOrDefault(l => l.Colunas == c.LarguraCupom)
                             ?? new OpcaoLargura($"{c.LarguraCupom} colunas", c.LarguraCupom);

        if (!string.IsNullOrWhiteSpace(c.ImpressoraCupom))
        {
            // Impressora salva que não aparece na lista (desligada ou removida): mantém o nome, para não perdê-lo.
            if (!Impressoras.Contains(c.ImpressoraCupom)) Impressoras.Add(c.ImpressoraCupom);
            ImpressoraSelecionada = c.ImpressoraCupom;
        }
        else
        {
            ImpressoraSelecionada = ImpressoraWindows.ImpressoraPadrao;
        }

        AtualizarPrevia();
    });

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        await _configuracao.SalvarAsync(DadosDoFormulario());
        _logoAlterado = false;
        MostrarSucesso("Configurações salvas.");
    });

    /// <summary>Imprime um cupom de exemplo com o que está na tela (mesmo sem salvar), para testar a impressora.</summary>
    [RelayCommand]
    private Task ImprimirTesteAsync() => ExecutarAsync(async () =>
    {
        await _cupons.ImprimirTesteAsync(ConfigDoFormulario());
        MostrarSucesso("Cupom de teste enviado para a impressora.");
    });

    private DadosLoja DadosDoFormulario() => new(
        NomeLoja, Documento, Endereco, Telefone, RodapeCupom,
        ImpressoraSelecionada == ImpressoraWindows.ImpressoraPadrao ? null : ImpressoraSelecionada,
        LarguraSelecionada.Colunas, ImprimirAoFinalizar, NomeOperador, PerguntarTamanho, AbrirNoPdv,
        RazaoSocial, InscricaoEstadual, Email, CidadeUf, ChavePix,
        CupomMostrarRazaoSocial, CupomMostrarDocumento, CupomMostrarEndereco, CupomMostrarTelefone, CupomMostrarPix,
        NumeroInteiro(CupomLinhasFinais, "As linhas em branco no fim do cupom"),
        DinheiroOuZero(FundoTrocoPadrao),
        NumeroInteiro(EstoqueMinimoPadrao, "O estoque mínimo padrão"),
        NumeroInteiro(MargemPadrao, "A margem padrão"),
        CupomMostrarCidade, CupomMostrarInscricao, CupomMostrarEmail, CupomMostrarOperador, CupomMostrarCliente,
        CupomMostrarPagamento, CupomMostrarAssinatura, CupomMensagemCabecalho,
        Segmento, EnderecoNumero, Bairro, Cep, CupomMostrarLogo, _logoBytes, _logoAlterado,
        CupomMostrarReferencia, CupomTituloEmCaixa, CupomMostrarAssinaturaCliente, CupomTextoGarantia, CupomModelo);

    private static int NumeroInteiro(string texto, string campo) =>
        int.TryParse(string.IsNullOrWhiteSpace(texto) ? "0" : texto.Trim(), out var n)
            ? n
            : throw new RegraDeNegocioException($"{campo} deve ser um número inteiro.");

    private static long DinheiroOuZero(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        return Dinheiro.TentarConverter(texto, out var c) ? c : throw new RegraDeNegocioException("O troco inicial padrão deve ser um valor em reais (ex.: 50,00).");
    }

    private ConfiguracaoLoja ConfigDoFormulario()
    {
        var d = DadosDoFormulario();
        return new ConfiguracaoLoja
        {
            NomeLoja = string.IsNullOrWhiteSpace(d.NomeLoja) ? "Nexus" : d.NomeLoja.Trim(),
            Documento = Vazio(d.Documento),
            Endereco = Vazio(d.Endereco),
            Telefone = Vazio(d.Telefone),
            MensagemCupom = Vazio(d.MensagemCupom),
            ImpressoraCupom = Vazio(d.ImpressoraCupom),
            LarguraCupom = d.LarguraCupom,
            ImprimirCupomAoFinalizar = d.ImprimirCupomAoFinalizar,
            RazaoSocial = Vazio(d.RazaoSocial),
            InscricaoEstadual = Vazio(d.InscricaoEstadual),
            Email = Vazio(d.Email),
            NomeOperador = string.IsNullOrWhiteSpace(d.NomeOperador) ? "Caixa" : d.NomeOperador.Trim(),
            CupomMostrarCidade = d.CupomMostrarCidade,
            CupomMostrarInscricao = d.CupomMostrarInscricao,
            CupomMostrarEmail = d.CupomMostrarEmail,
            CupomMostrarOperador = d.CupomMostrarOperador,
            CupomMostrarCliente = d.CupomMostrarCliente,
            CupomMostrarPagamento = d.CupomMostrarPagamento,
            CupomMostrarAssinatura = d.CupomMostrarAssinatura,
            CupomMensagemCabecalho = Vazio(d.CupomMensagemCabecalho),
            CidadeUf = Vazio(d.CidadeUf),
            ChavePix = Vazio(d.ChavePix),
            CupomMostrarRazaoSocial = d.CupomMostrarRazaoSocial,
            CupomMostrarDocumento = d.CupomMostrarDocumento,
            CupomMostrarEndereco = d.CupomMostrarEndereco,
            CupomMostrarTelefone = d.CupomMostrarTelefone,
            CupomMostrarPix = d.CupomMostrarPix,
            CupomLinhasFinais = d.CupomLinhasFinais,
            Segmento = Vazio(d.Segmento),
            EnderecoNumero = Vazio(d.EnderecoNumero),
            Bairro = Vazio(d.Bairro),
            Cep = Vazio(d.Cep),
            CupomMostrarLogo = d.CupomMostrarLogo,
            CupomModelo = d.CupomModelo,
            CupomMostrarReferencia = d.CupomMostrarReferencia,
            CupomTituloEmCaixa = d.CupomTituloEmCaixa,
            CupomMostrarAssinaturaCliente = d.CupomMostrarAssinaturaCliente,
            CupomTextoGarantia = Vazio(d.CupomTextoGarantia),
            LogoCupom = _logoBytes
        };
    }

    private void AtualizarPrevia()
    {
        try
        {
            var loja = ConfigDoFormulario();
            Previa = MontadorCupom.Montar(VendaDePrevia(), loja);
            var largura = Math.Clamp(loja.LarguraCupom, ConfiguracaoService.LarguraMinima, ConfiguracaoService.LarguraMaxima);
            LinhasPrevia = MontadorCupom.Estilizar(Previa).Select(l => LinhaDaPrevia(l, largura)).ToList();
        }
        catch (RegraDeNegocioException) { /* campo numérico ainda incompleto: mantém a prévia anterior */ }
    }

    /// <summary>
    /// Uma linha da prévia. O nome da loja sai em letra maior (até 17), mas os espaços de centralização encolhem na
    /// mesma medida: a linha continua com a largura do papel e o nome fica no meio, como na impressora.
    /// </summary>
    private static IReadOnlyList<SegmentoPrevia> LinhaDaPrevia(List<SegmentoCupom> linha, int largura)
    {
        var nome = linha.FirstOrDefault(s => s.Grande)?.Texto.Trim() ?? "";
        if (nome.Length == 0)
            return linha.Select(s => new SegmentoPrevia(s.Texto.Length == 0 ? " " : s.Texto, Peso(s.Negrito))).ToList();

        var tamanho = Math.Max(12, Math.Min(17, 12.0 * largura / nome.Length));
        // O nome maior ocupa mais colunas; o que sobra da largura do papel vira espaço dos dois lados.
        var espaco = Math.Max(0, (int)Math.Round((largura - nome.Length * tamanho / 12) / 2));
        return new List<SegmentoPrevia>
        {
            new(new string(' ', espaco), System.Windows.FontWeights.Normal),
            new(nome, System.Windows.FontWeights.Bold, tamanho)
        };
    }

    private static System.Windows.FontWeight Peso(bool negrito) =>
        negrito ? System.Windows.FontWeights.Bold : System.Windows.FontWeights.Normal;

    /// <summary>A venda de exemplo, com o nome do operador que está no formulário.</summary>
    private Venda VendaDePrevia()
    {
        var venda = MontadorCupom.VendaDeExemplo();
        venda.Operador = string.IsNullOrWhiteSpace(NomeOperador) ? "Caixa" : NomeOperador.Trim();
        return venda;
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
