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
        ChavePix = c.ChavePix ?? "";
        CupomMostrarRazaoSocial = c.CupomMostrarRazaoSocial;
        CupomMostrarDocumento = c.CupomMostrarDocumento;
        CupomMostrarEndereco = c.CupomMostrarEndereco;
        CupomMostrarTelefone = c.CupomMostrarTelefone;
        CupomMostrarPix = c.CupomMostrarPix;
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
        NumeroInteiro(MargemPadrao, "A margem padrão"));

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
            CidadeUf = Vazio(d.CidadeUf),
            ChavePix = Vazio(d.ChavePix),
            CupomMostrarRazaoSocial = d.CupomMostrarRazaoSocial,
            CupomMostrarDocumento = d.CupomMostrarDocumento,
            CupomMostrarEndereco = d.CupomMostrarEndereco,
            CupomMostrarTelefone = d.CupomMostrarTelefone,
            CupomMostrarPix = d.CupomMostrarPix,
            CupomLinhasFinais = d.CupomLinhasFinais
        };
    }

    private void AtualizarPrevia()
    {
        try { Previa = MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), ConfigDoFormulario()); }
        catch (RegraDeNegocioException) { /* campo numérico ainda incompleto: mantém a prévia anterior */ }
    }

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
