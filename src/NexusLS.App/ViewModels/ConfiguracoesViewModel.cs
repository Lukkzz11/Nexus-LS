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
        GestorBackup? backup = null, NexusLS.App.Backup.IDialogosBackup? dialogos = null)
    {
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

    partial void OnNomeLojaChanged(string value) => AtualizarPrevia();
    partial void OnDocumentoChanged(string value) => AtualizarPrevia();
    partial void OnEnderecoChanged(string value) => AtualizarPrevia();
    partial void OnTelefoneChanged(string value) => AtualizarPrevia();
    partial void OnRodapeCupomChanged(string value) => AtualizarPrevia();
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
        LarguraSelecionada.Colunas, ImprimirAoFinalizar, NomeOperador, PerguntarTamanho, AbrirNoPdv);

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
            ImprimirCupomAoFinalizar = d.ImprimirCupomAoFinalizar
        };
    }

    private void AtualizarPrevia() =>
        Previa = MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), ConfigDoFormulario());

    private static string? Vazio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
