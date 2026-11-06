using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoMovimento(string Texto, TipoMovimentoCaixa Tipo);

/// <summary>Uma linha do histórico de fechamentos, já com os textos prontos para a tela.</summary>
public record LinhaFechamento(FechamentoCaixa Fechamento, string Quando, string Diferenca, string Observacao)
{
    public int TurnoId => Fechamento.TurnoId;
    public int Versao => Fechamento.Versao;
}

/// <summary>
/// Abrir o caixa (troco inicial), registrar sangria/reforço/despesa/entrada e fechar conferindo o que deveria ter
/// em dinheiro, cartão e Pix. Diferença exige uma observação. Fechamentos antigos podem ser conferidos de novo (nova versão).
/// </summary>
public partial class FechamentoViewModel : ViewModelBase
{
    private readonly CaixaService _caixa;

    private readonly ConfiguracaoService? _configuracao;
    private readonly RelatorioCaixaPeriodoService? _relatorioPeriodo;

    // ----- Relatório do Caixa por período (PDF) -----
    /// <summary>A tela define a caixa "Salvar como" do Windows (nulo = cancelou).</summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    [ObservableProperty] private DateTime? _relatorioDe;
    [ObservableProperty] private DateTime? _relatorioAte;
    public bool TemRelatorioPeriodo => _relatorioPeriodo is not null;

    [RelayCommand]
    private Task GerarRelatorioPdfAsync() => ExecutarAsync(async () =>
    {
        if (_relatorioPeriodo is null) return;
        if (RelatorioDe is not DateTime de || RelatorioAte is not DateTime ate)
            throw new RegraDeNegocioException("Escolha as duas datas do período.");

        var relatorio = await _relatorioPeriodo.GerarAsync(de, ate);
        var caminho = EscolherArquivoParaSalvar(RelatorioCaixaPdf.NomeDoArquivo(relatorio));
        if (caminho is null) return;

        var loja = _configuracao is null ? "Nexus LS" : (await _configuracao.ObterAsync()).NomeLoja;
        await System.IO.File.WriteAllBytesAsync(caminho, RelatorioCaixaPdf.Gerar(relatorio, loja));
        MostrarSucesso($"Relatório salvo: {caminho}");
    });

    public FechamentoViewModel(CaixaService caixa, ConfiguracaoService? configuracao = null,
        RelatorioCaixaPeriodoService? relatorioPeriodo = null, DateTime? hoje = null)
    {
        _relatorioPeriodo = relatorioPeriodo;
        var dia = (hoje ?? DateTime.Today).Date;
        _relatorioDe = new DateTime(dia.Year, dia.Month, 1);
        _relatorioAte = dia;
        _caixa = caixa;
        _configuracao = configuracao;
        _movimentoSelecionado = OpcoesMovimento[0];
        _ = AtualizarAsync();
    }

    public IReadOnlyList<OpcaoMovimento> OpcoesMovimento { get; } = new[]
    {
        new OpcaoMovimento("Sangria (tirar dinheiro)", TipoMovimentoCaixa.Sangria),
        new OpcaoMovimento("Reforço (colocar dinheiro)", TipoMovimentoCaixa.Reforco),
        new OpcaoMovimento("Despesa paga em dinheiro", TipoMovimentoCaixa.Despesa),
        new OpcaoMovimento("Entrada avulsa", TipoMovimentoCaixa.EntradaAvulsa)
    };

    public ObservableCollection<string> Lancamentos { get; } = new();
    public ObservableCollection<LinhaFechamento> Historico { get; } = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeConferir))] [NotifyPropertyChangedFor(nameof(PodeAbrir))]
    [NotifyPropertyChangedFor(nameof(SituacaoTexto))]
    private bool _turnoAberto;

    /// <summary>Turno que está sendo conferido: o aberto, ou um fechado cuja conferência será refeita.</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PodeConferir))] [NotifyPropertyChangedFor(nameof(Refazendo))]
    private int? _turnoEmConferencia;

    [ObservableProperty] private string _trocoInicialTexto = "";
    [ObservableProperty] private OpcaoMovimento _movimentoSelecionado;
    [ObservableProperty] private string _movimentoValorTexto = "";
    [ObservableProperty] private string _movimentoDescricao = "";

    [ObservableProperty] private string _cabecalho = "";
    [ObservableProperty] private string _resumoVendas = "";
    [ObservableProperty] private string _esperadoDinheiro = "";
    [ObservableProperty] private string _esperadoCartao = "";
    [ObservableProperty] private string _esperadoPix = "";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TextoDiferenca))] private string _conferidoDinheiroTexto = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TextoDiferenca))] private string _conferidoCartaoTexto = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TextoDiferenca))] private string _conferidoPixTexto = "";
    [ObservableProperty] private string _observacao = "";

    private ResumoTurno? _resumo;

    /// <summary>A tela define como perguntar "confirma?" (assim o ViewModel não depende de MessageBox). Nos testes, responde sim.</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    /// <summary>Texto curto da situação do caixa, para o cabeçalho do Caixa.</summary>
    public string SituacaoTexto => !TurnoAberto || _resumo is null
        ? "Caixa fechado"
        : $"Caixa nº {_resumo.Turno.Id} aberto desde {_resumo.Turno.DataAbertura:dd/MM HH:mm}";

    public bool PodeAbrir => !TurnoAberto;
    public bool PodeConferir => TurnoEmConferencia is not null;
    public bool Refazendo => TurnoEmConferencia is not null && !TurnoAberto;

    /// <summary>Mostra a diferença em tempo real, para o operador ver antes de fechar.</summary>
    public string TextoDiferenca
    {
        get
        {
            if (_resumo is null) return "";
            var partes = new List<string>();
            Parte(partes, "Dinheiro", ConferidoDinheiroTexto, _resumo.EsperadoDinheiroCentavos);
            Parte(partes, "Cartão", ConferidoCartaoTexto, _resumo.EsperadoCartaoCentavos);
            Parte(partes, "Pix", ConferidoPixTexto, _resumo.EsperadoPixCentavos);
            return string.Join("   ·   ", partes);
        }
    }

    private static void Parte(List<string> partes, string nome, string texto, long esperado)
    {
        if (!Dinheiro.TentarConverter(texto, out var conferido)) return;
        var dif = conferido - esperado;
        partes.Add(dif == 0 ? $"{nome}: confere" : $"{nome}: {(dif > 0 ? "sobra" : "falta")} {Dinheiro.Formatar(Math.Abs(dif))}");
    }

    [RelayCommand]
    private Task AtualizarAsync() => ExecutarAsync(async () =>
    {
        var aberto = await _caixa.ObterAbertoAsync();
        TurnoAberto = aberto is not null;
        if (aberto is null && _configuracao is not null && string.IsNullOrWhiteSpace(TrocoInicialTexto))
        {
            var padrao = (await _configuracao.ObterAsync()).FundoTrocoPadraoCentavos;
            if (padrao > 0) TrocoInicialTexto = Dinheiro.FormatarParaEdicao(padrao);   // já sugere; dá para mudar
        }
        if (aberto is not null) TurnoEmConferencia = aberto.Id;
        else if (!Refazendo) TurnoEmConferencia = null;

        await CarregarResumoAsync();

        Historico.Clear();
        foreach (var f in await _caixa.HistoricoAsync())
        {
            var dif = f.DiferencaTotalCentavos;
            Historico.Add(new LinhaFechamento(f,
                $"{f.DataHora:dd/MM/yyyy HH:mm} · caixa nº {f.TurnoId} · versão {f.Versao}",
                dif == 0 ? "Sem diferença" : $"{(dif > 0 ? "Sobrou" : "Faltou")} {Dinheiro.Formatar(Math.Abs(dif))}",
                f.Observacao ?? ""));
        }
    });

    private async Task CarregarResumoAsync()
    {
        _resumo = null;
        OnPropertyChanged(nameof(SituacaoTexto));
        Lancamentos.Clear();
        if (TurnoEmConferencia is not int id)
        {
            Cabecalho = "Nenhum caixa aberto.";
            ResumoVendas = ""; EsperadoDinheiro = EsperadoCartao = EsperadoPix = "";
            OnPropertyChanged(nameof(TextoDiferenca));
            return;
        }

        var r = _resumo = await _caixa.ResumoAsync(id);
        OnPropertyChanged(nameof(SituacaoTexto));
        Cabecalho = r.Turno.Aberto
            ? $"Caixa nº {r.Turno.Id} aberto em {r.Turno.DataAbertura:dd/MM/yyyy HH:mm} · troco inicial {Dinheiro.Formatar(r.Turno.TrocoInicialCentavos)}"
            : $"Caixa nº {r.Turno.Id} · {r.Turno.DataAbertura:dd/MM HH:mm} a {r.Turno.DataFechamento:dd/MM HH:mm} (conferindo de novo)";
        ResumoVendas = $"{r.VendasValidas} venda(s)"
                       + (r.VendasCanceladas > 0 ? $" ({r.VendasCanceladas} cancelada(s) não contam)" : "")
                       + (r.VendasChequeCentavos > 0 ? $" · Cheque: {Dinheiro.Formatar(r.VendasChequeCentavos)}" : "")
                       + (r.VendasAPrazoCentavos > 0 ? $" · A prazo: {Dinheiro.Formatar(r.VendasAPrazoCentavos)}" : "");
        EsperadoDinheiro =
            $"Dinheiro esperado: {Dinheiro.Formatar(r.EsperadoDinheiroCentavos)}  (troco {Dinheiro.Formatar(r.Turno.TrocoInicialCentavos)}"
            + $" + vendas {Dinheiro.Formatar(r.VendasDinheiroCentavos)} + reforços/entradas {Dinheiro.Formatar(r.ReforcosCentavos + r.EntradasAvulsasCentavos)}"
            + $" − sangrias/despesas {Dinheiro.Formatar(r.SangriasCentavos + r.DespesasCentavos)})";
        EsperadoCartao = $"Cartão esperado: {Dinheiro.Formatar(r.EsperadoCartaoCentavos)}";
        EsperadoPix = $"Pix esperado: {Dinheiro.Formatar(r.EsperadoPixCentavos)}";

        foreach (var m in r.Movimentos)
            Lancamentos.Add($"{m.DataHora:HH:mm} · {Nome(m.Tipo)} · {Dinheiro.Formatar(m.ValorCentavos)}"
                            + (string.IsNullOrWhiteSpace(m.Descricao) ? "" : $" · {m.Descricao}"));
        OnPropertyChanged(nameof(TextoDiferenca));
    }

    private static string Nome(TipoMovimentoCaixa tipo) => tipo switch
    {
        TipoMovimentoCaixa.Sangria => "Sangria",
        TipoMovimentoCaixa.Reforco => "Reforço",
        TipoMovimentoCaixa.Despesa => "Despesa",
        _ => "Entrada avulsa"
    };

    [RelayCommand]
    private Task AbrirCaixaAsync() => ExecutarAsync(async () =>
    {
        long troco = 0;
        if (!string.IsNullOrWhiteSpace(TrocoInicialTexto) && !Dinheiro.TentarConverter(TrocoInicialTexto, out troco))
            throw new RegraDeNegocioException("Troco inicial inválido. Digite um valor como 50,00.");

        await _caixa.AbrirAsync(troco);
        TrocoInicialTexto = "";
        await AtualizarAsync();
        MostrarSucesso("Caixa aberto.");
    });

    [RelayCommand]
    private Task RegistrarMovimentoAsync() => ExecutarAsync(async () =>
    {
        if (!Dinheiro.TentarConverter(MovimentoValorTexto, out var valor))
            throw new RegraDeNegocioException("Valor inválido. Digite um valor como 20,00.");

        await _caixa.RegistrarMovimentoAsync(MovimentoSelecionado.Tipo, valor, MovimentoDescricao);
        MovimentoValorTexto = "";
        MovimentoDescricao = "";
        await AtualizarAsync();
        MostrarSucesso("Movimento registrado.");
    });

    [RelayCommand]
    private Task FecharAsync() => ExecutarAsync(async () =>
    {
        if (TurnoEmConferencia is not int id) return;

        var dinheiro = Valor(ConferidoDinheiroTexto, "dinheiro");
        var cartao = Valor(ConferidoCartaoTexto, "cartão");
        var pix = Valor(ConferidoPixTexto, "Pix");

        var f = await _caixa.FecharAsync(id, new ConferenciaCaixa(dinheiro, cartao, pix, Observacao));

        ConferidoDinheiroTexto = ConferidoCartaoTexto = ConferidoPixTexto = Observacao = "";
        TurnoEmConferencia = null;
        await AtualizarAsync();
        MostrarSucesso(f.DiferencaTotalCentavos == 0
            ? $"Caixa nº {id} conferido, sem diferença (versão {f.Versao})."
            : $"Caixa nº {id} conferido com diferença de {Dinheiro.Formatar(f.DiferencaTotalCentavos)} (versão {f.Versao}).");
    });

    /// <summary>
    /// O botão "Fechar caixa" do Caixa: um clique. Mostra o resumo do dia e pede a confirmação. O que foi contado nos
    /// campos de conferência vale; o campo que ficou vazio é dado como "confere com o esperado". As regras de sempre
    /// continuam no serviço: fechamento imutável, diferença exige observação, trilha na mesma transação.
    /// </summary>
    [RelayCommand]
    private Task FecharRapidoAsync() => ExecutarAsync(async () =>
    {
        if (TurnoEmConferencia is not int id || _resumo is not { } r)
        {
            MostrarErro("Não há caixa aberto para fechar.");
            return;
        }

        var dinheiro = Contado(ConferidoDinheiroTexto, "dinheiro", r.EsperadoDinheiroCentavos);
        var cartao = Contado(ConferidoCartaoTexto, "cartão", r.EsperadoCartaoCentavos);
        var pix = Contado(ConferidoPixTexto, "Pix", r.EsperadoPixCentavos);

        if ((Diferente(r, dinheiro, cartao, pix) || Refazendo) && Observacao.Trim().Length < 3)
        {
            MostrarErro(Refazendo
                ? "Escreva a observação da conferência refeita antes de fechar."
                : "Há diferença no caixa. Escreva a observação (aba Conferência) antes de fechar.");
            return;
        }

        var resumo = $"Fechar o caixa nº {id}?{Environment.NewLine}{Environment.NewLine}"
                     + $"{ResumoVendas}{Environment.NewLine}"
                     + $"Dinheiro: {Dinheiro.Formatar(dinheiro)} (esperado {Dinheiro.Formatar(r.EsperadoDinheiroCentavos)}){Environment.NewLine}"
                     + $"Cartão: {Dinheiro.Formatar(cartao)} (esperado {Dinheiro.Formatar(r.EsperadoCartaoCentavos)}){Environment.NewLine}"
                     + $"Pix: {Dinheiro.Formatar(pix)} (esperado {Dinheiro.Formatar(r.EsperadoPixCentavos)})";
        var diferenca = Diferenca(r, dinheiro, cartao, pix);
        if (Diferente(r, dinheiro, cartao, pix))
            resumo += $"{Environment.NewLine}{Environment.NewLine}Diferença de {Dinheiro.Formatar(diferenca)}, registrada com a observação: {Observacao.Trim()}";
        else
            resumo += $"{Environment.NewLine}{Environment.NewLine}Sem diferença. Os campos vazios foram dados como \"confere\".";

        if (!Confirmar(resumo)) return;

        var f = await _caixa.FecharAsync(id, new ConferenciaCaixa(dinheiro, cartao, pix, Observacao));

        ConferidoDinheiroTexto = ConferidoCartaoTexto = ConferidoPixTexto = Observacao = "";
        TurnoEmConferencia = null;
        await AtualizarAsync();
        MostrarSucesso(f.DiferencaTotalCentavos == 0
            ? $"Caixa nº {id} fechado, sem diferença."
            : $"Caixa nº {id} fechado com diferença de {Dinheiro.Formatar(f.DiferencaTotalCentavos)}.");
    });

    private static bool Diferente(ResumoTurno r, long dinheiro, long cartao, long pix) =>
        dinheiro != r.EsperadoDinheiroCentavos || cartao != r.EsperadoCartaoCentavos || pix != r.EsperadoPixCentavos;

    private static long Diferenca(ResumoTurno r, long dinheiro, long cartao, long pix) =>
        (dinheiro - r.EsperadoDinheiroCentavos) + (cartao - r.EsperadoCartaoCentavos) + (pix - r.EsperadoPixCentavos);

    /// <summary>Campo vazio = confere com o esperado (só no fechamento rápido).</summary>
    private static long Contado(string texto, string meio, long esperado) =>
        string.IsNullOrWhiteSpace(texto) ? esperado : Valor(texto, meio);

    /// <summary>Vazio conta como zero (por exemplo, não houve venda no cartão).</summary>
    private static long Valor(string texto, string meio)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        if (!Dinheiro.TentarConverter(texto, out var v))
            throw new RegraDeNegocioException($"Valor de {meio} inválido. Digite um valor como 150,00.");
        return v;
    }

    /// <summary>Escolhe um fechamento do histórico para conferir de novo (gera uma nova versão).</summary>
    [RelayCommand]
    private Task ConferirDeNovoAsync(LinhaFechamento? linha) => ExecutarAsync(async () =>
    {
        if (linha is null || TurnoAberto) { if (TurnoAberto) MostrarErro("Feche o caixa aberto antes de refazer uma conferência."); return; }
        TurnoEmConferencia = linha.TurnoId;
        await CarregarResumoAsync();
    });
}
