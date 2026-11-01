using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Services;
using static NexusLS.Core.Services.PrecificacaoPro;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Calculadora de preços profissional: o mesmo cálculo do ERP (preço pela margem, promoção, formas de pagamento,
/// quantidades e custo de lote). Os campos são texto, como o operador digita; as contas ficam em <see cref="PrecificacaoPro"/>.
/// </summary>
public partial class CalculadoraPrecosViewModel : ViewModelBase
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // ---- custo e despesas (valem para todas as abas)
    [ObservableProperty] private string _custoCompra = "";
    [ObservableProperty] private string _freteReais = "";
    [ObservableProperty] private string _embalagemReais = "";
    [ObservableProperty] private string _outrosCustosPct = "";
    [ObservableProperty] private string _impostosPct = "";
    [ObservableProperty] private string _comissaoPct = "";
    [ObservableProperty] private string _outrasVariaveisPct = "";
    [ObservableProperty] private string _outrasVariaveisReais = "";
    [ObservableProperty] private string _fixasPct = "";
    [ObservableProperty] private string _fixasMensais = "";
    [ObservableProperty] private string _pecasPorMes = "";
    [ObservableProperty] private string _margemPct = "";
    [ObservableProperty] private string _taxaPagamentoPct = "";
    [ObservableProperty] private bool _terminarEm90;

    // ---- promoção
    [ObservableProperty] private string _precoOriginal = "";
    [ObservableProperty] private string _desconto = "";
    [ObservableProperty] private bool _descontoEmReais;
    [ObservableProperty] private string _precoMinimo = "";
    [ObservableProperty] private string _margemMinima = "";

    // ---- pagamentos (um por linha: "Rótulo; taxa%"; taxa vazia = não configurada)
    [ObservableProperty] private string _precoPagamento = "";
    [ObservableProperty] private string _formasPagamento = "Dinheiro;\nPix;\nCartão de débito;\nCrédito à vista;\nCrédito em 2x;\nCrédito em 3x;";

    // ---- quantidades
    [ObservableProperty] private string _precoQuantidades = "";
    [ObservableProperty] private string _quantidades = "1, 10, 50, 100";
    [ObservableProperty] private string _metaLucro = "";

    // ---- lote (itens: "nome; quantidade; custo unitário"; despesas: "nome; valor; proporcional|quantidade")
    [ObservableProperty] private string _itensLote = "";
    [ObservableProperty] private string _despesasLote = "Frete; ; proporcional";

    // ---- resultados
    [ObservableProperty] private string _resultadoPreco = "";
    [ObservableProperty] private string _resultadoPromocao = "";
    [ObservableProperty] private string _resultadoPagamentos = "";
    [ObservableProperty] private string _resultadoQuantidades = "";
    [ObservableProperty] private string _resultadoLote = "";

    // ================= entrada =================

    private static decimal? Numero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var s = texto.Replace("R$", "").Replace("%", "").Replace(" ", "").Replace(" ", "");
        if (!s.Contains(',') && System.Text.RegularExpressions.Regex.IsMatch(s, @"^\d+\.\d{1,2}$")) s = s.Replace('.', ',');
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands, PtBr, out var v) ? v : throw new RegraDeNegocioException($"\"{texto}\" não é um número válido.");
    }

    private static decimal Num(string? texto) => Numero(texto) ?? 0;

    private static long Centavos(string? texto) => (long)Math.Round(Num(texto) * 100m, MidpointRounding.AwayFromZero);

    private Parametros MontarParametros(bool comMargem = false)
    {
        var adicionais = new List<CustoAdicional>
        {
            new("Frete", false, Centavos(FreteReais)),
            new("Embalagem", false, Centavos(EmbalagemReais)),
            new("Outros custos", true, Num(OutrosCustosPct))
        };
        return new Parametros
        {
            CustoCompraCentavos = Centavos(CustoCompra),
            Adicionais = adicionais,
            ImpostosPct = Num(ImpostosPct),
            ComissaoPct = Num(ComissaoPct),
            OutrasVariaveisPct = Num(OutrasVariaveisPct),
            OutrasVariaveisCentavos = Centavos(OutrasVariaveisReais),
            TaxaPagamentoPct = Num(TaxaPagamentoPct),
            FixasPct = Num(FixasPct),
            FixasMensaisCentavos = Centavos(FixasMensais),
            PecasPorMes = Num(PecasPorMes),
            MargemPct = comMargem ? Num(MargemPct) : 0
        };
    }

    private static string R(long centavos) => Dinheiro.Formatar(centavos);
    private static string P(decimal v) => v.ToString("0.##", PtBr) + "%";

    private static void Detalhe(StringBuilder sb, Analise a)
    {
        sb.AppendLine($"Preço de venda: {R(a.PrecoCentavos)}");
        sb.AppendLine($"Custo de aquisição: {R(a.CustoAquisicaoCentavos)} (compra {R(a.CustoCompraCentavos)} + adicionais {R(a.CustosAdicionaisCentavos)})");
        sb.AppendLine($"Despesas variáveis: {R(a.VariaveisCentavos)} (impostos {R(a.ImpostosCentavos)}, comissão {R(a.ComissaoCentavos)}, pagamento {R(a.TaxaPagamentoCentavos)}, outras {R(a.OutrasPctCentavos + a.OutrasReaisCentavos)})");
        sb.AppendLine($"Margem de contribuição: {R(a.ContribuicaoCentavos)} ({P(a.ContribuicaoPct)})");
        sb.AppendLine($"Despesas fixas: {R(a.FixasCentavos)} (percentual {R(a.FixasPctCentavos)}, rateio por peça {R(a.RateioFixoCentavos)})");
        sb.AppendLine($"Lucro estimado: {R(a.LucroCentavos)}   Margem: {P(a.MargemPct)}   Markup: {(a.MarkupPct is decimal m ? P(m) : "—")}");
    }

    // ================= comandos =================

    [RelayCommand]
    private Task CalcularPrecoAsync() => ExecutarAsync(() =>
    {
        var s = Precificar(MontarParametros(comMargem: true), TerminarEm90);
        var sb = new StringBuilder();
        if (!s.Ok) foreach (var e in s.Erros) sb.AppendLine(e);
        else
        {
            foreach (var a in s.Avisos) sb.AppendLine("Atenção: " + a);
            Detalhe(sb, s.Analise!);
            var eq = PrecoDeEquilibrio(MontarParametros());
            if (eq is long e) sb.AppendLine($"Preço de equilíbrio (lucro zero): {R(e)}");
            sb.AppendLine("Lucro estimado é uma estimativa para decidir preço, não o lucro líquido contábil.");
        }
        ResultadoPreco = sb.ToString().TrimEnd();
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task CalcularPromocaoAsync() => ExecutarAsync(() =>
    {
        var valor = Num(Desconto);
        var r = SimularPromocao(Centavos(PrecoOriginal), DescontoEmReais, DescontoEmReais ? valor * 100m : valor, MontarParametros(),
            Centavos(PrecoMinimo), Num(MargemMinima));
        var sb = new StringBuilder();
        if (!r.Ok) sb.AppendLine(r.Erro);
        else
        {
            sb.AppendLine($"Preço promocional: {R(r.PrecoPromocionalCentavos)} (desconto de {R(r.DescontoCentavos)}, {P(r.DescontoPct)})");
            sb.AppendLine($"Lucro por peça: {R(r.Antes!.LucroCentavos)} → {R(r.Depois!.LucroCentavos)} (diferença {R(r.DiferencaLucroCentavos)})");
            sb.AppendLine($"Margem: {P(r.Antes.MargemPct)} → {P(r.Depois.MargemPct)} ({r.DiferencaMargemPontos:+0.##;-0.##} pontos)");
            if (r.PrecoDeEquilibrioCentavos is long eq) sb.AppendLine($"Preço de equilíbrio: {R(eq)}");
            if (r.VendaExtraNecessariaPct is decimal x) sb.AppendLine($"Para ganhar o mesmo, é preciso vender {P(x)} mais peças.");
            foreach (var a in r.Alertas) sb.AppendLine((a.Erro ? "ERRO: " : "Atenção: ") + a.Texto);
        }
        ResultadoPromocao = sb.ToString().TrimEnd();
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task CompararPagamentosAsync() => ExecutarAsync(() =>
    {
        var cenarios = new List<CenarioPagamento>();
        foreach (var linha in FormasPagamento.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var partes = linha.Split(';');
            cenarios.Add(new CenarioPagamento(partes[0].Trim(), partes.Length > 1 ? Numero(partes[1]) : null));
        }
        var linhas = PrecificacaoPro.CompararPagamentos(Centavos(PrecoPagamento), MontarParametros(), cenarios);
        var sb = new StringBuilder();
        foreach (var l in linhas)
            sb.AppendLine($"{l.Rotulo}: {(l.Configurada ? "taxa " + P(l.TaxaPct) : "taxa não configurada")} — taxa {R(l.TaxaCentavos)}, recebe {R(l.RecebidoCentavos)}, lucro {R(l.LucroCentavos)} ({P(l.MargemPct)})");
        if (linhas.Any(l => !l.Configurada)) sb.AppendLine("Formas sem taxa contam como 0% só para a conta; informe a taxa depois do ponto e vírgula.");
        sb.AppendLine("O recebido é o preço menos a taxa; não considera prazo de recebimento nem antecipação.");
        ResultadoPagamentos = sb.ToString().TrimEnd();
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task SimularQuantidadesAsync() => ExecutarAsync(() =>
    {
        var qtds = Quantidades.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(q => (int)Math.Floor(Num(q))).ToList();
        var r = PrecificacaoPro.SimularQuantidades(Centavos(PrecoQuantidades), MontarParametros(), qtds, Centavos(MetaLucro));
        var sb = new StringBuilder();
        sb.AppendLine($"Lucro por peça: {R(r.LucroPorPecaCentavos)}");
        foreach (var l in r.Linhas)
            sb.AppendLine($"{l.Quantidade} peça(s): faturamento {R(l.FaturamentoCentavos)}, custo {R(l.CustoMercadoriasCentavos)}, variáveis {R(l.VariaveisCentavos)}, contribuição {R(l.ContribuicaoCentavos)}, fixas {R(l.FixasCentavos)}, lucro {R(l.LucroCentavos)}");
        if (r.MetaImpossivel) sb.AppendLine("A meta de lucro não é alcançável: neste preço o lucro por peça não é positivo.");
        else if (r.QuantidadeParaMeta is long q) sb.AppendLine($"Para a meta de lucro: {q} peça(s).");
        if (r.EquilibrioMensalPecas is long e) sb.AppendLine($"Ponto de equilíbrio das despesas fixas: {e} peça(s) por mês.");
        ResultadoQuantidades = sb.ToString().TrimEnd();
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task RatearLoteAsync() => ExecutarAsync(() =>
    {
        var itens = new List<ItemLote>();
        foreach (var linha in ItensLote.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = linha.Split(';');
            if (p.Length < 3) throw new RegraDeNegocioException($"Item \"{linha}\": use nome; quantidade; custo unitário.");
            itens.Add(new ItemLote(p[0].Trim(), (int)Math.Floor(Num(p[1])), Centavos(p[2])));
        }
        var despesas = new List<DespesaLote>();
        foreach (var linha in DespesasLote.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = linha.Split(';');
            if (p.Length < 2 || string.IsNullOrWhiteSpace(p[1])) continue;
            var porQtd = p.Length > 2 && p[2].Trim().StartsWith("q", StringComparison.OrdinalIgnoreCase);
            despesas.Add(new DespesaLote(p[0].Trim(), Centavos(p[1]), porQtd));
        }
        var r = RatearLote(itens, despesas);
        var sb = new StringBuilder();
        foreach (var i in r.Itens)
            sb.AppendLine($"{i.Nome}: {i.Quantidade} × {R(i.CustoUnitarioCentavos)} + rateio {R(i.RateioUnitarioCentavos)} = {R(i.CustoComRateioCentavos)} por peça (total {R(i.TotalItemCentavos)})");
        sb.AppendLine($"Despesas do lote: {R(r.TotalDespesasCentavos)}   Total do lote: {R(r.TotalLoteCentavos)}");
        ResultadoLote = sb.ToString().TrimEnd();
        return Task.CompletedTask;
    });
}
