using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Impressao;
using NexusLS.Core.Backup;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoLargura(string Texto, int Colunas);

/// <summary>Dados da loja e do cupom, com uma prévia do cupom que muda enquanto você digita.</summary>
public partial class ConfiguracoesViewModel : ViewModelBase
{
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
        LogoPrevia = CupomMostrarLogo ? LogoCupom.EmCinza(_logoBytes) : null;
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
    partial void OnCepChanged(string value) => AtualizarPrevia();

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
    [ObservableProperty] private string _cupomMensagemCabecalho = "";
    [ObservableProperty] private string _cupomLinhasFinais = "1";
    // Parâmetros de operação
    [ObservableProperty] private string _fundoTrocoPadrao = "";
    [ObservableProperty] private string _estoqueMinimoPadrao = "0";
    [ObservableProperty] private string _margemPadrao = "0";

    partial void OnNomeLojaChanged(string value) => AtualizarPrevia();
    partial void OnDocumentoChanged(string value) => AtualizarPrevia();
    partial void OnEnderecoChanged(string value) => AtualizarPrevia();
    partial void OnTelefoneChanged(string value) => AtualizarPrevia();
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
        Segmento, EnderecoNumero, Bairro, Cep, CupomMostrarLogo, _logoBytes, _logoAlterado);

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
            NomeLoja = string.IsNullOrWhiteSpace(d.NomeLoja) ? "Nexus LS" : d.NomeLoja.Trim(),
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
            LogoCupom = _logoBytes
        };
    }

    private void AtualizarPrevia()
    {
        try { Previa = MontadorCupom.Montar(VendaDePrevia(), ConfigDoFormulario()); }
        catch (RegraDeNegocioException) { /* campo numérico ainda incompleto: mantém a prévia anterior */ }
    }

    /// <summary>A venda de exemplo, com o nome do operador que está no formulário.</summary>
    private Venda VendaDePrevia()
    {
        var venda = MontadorCupom.VendaDeExemplo();
        venda.Operador = string.IsNullOrWhiteSpace(NomeOperador) ? "Caixa" : NomeOperador.Trim();
        return venda;
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
