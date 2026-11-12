namespace NexusLS.Core.Services;

/// <summary>
/// Calculadora de preços profissional (regras puras, em centavos), a mesma do ERP.
/// Custo de aquisição = custo de compra + frete, embalagem e outros custos por peça.
/// Despesas variáveis (impostos, comissão, taxa de pagamento, outras) crescem com a venda: % do PREÇO ou R$ por peça.
/// Margem de contribuição = preço − custo − variáveis. Lucro estimado = contribuição − despesas fixas (estimativa gerencial).
/// Preço sugerido = (custo + R$ por peça) ÷ (1 − variáveis% − fixas% − margem%); só existe se a soma for menor que 100%.
/// </summary>
public static class PrecificacaoPro
{
    /// <summary>Custo adicional por peça: valor em R$ (centavos) ou % do custo de compra.</summary>
    public sealed record CustoAdicional(string Nome, bool Percentual, decimal Valor);

    /// <summary>Tudo o que entra na conta. Percentuais em "%" (8 = 8%); dinheiro em centavos.</summary>
    public sealed record Parametros
    {
        public long CustoCompraCentavos { get; init; }
        public IReadOnlyList<CustoAdicional> Adicionais { get; init; } = Array.Empty<CustoAdicional>();
        public decimal ImpostosPct { get; init; }
        public decimal ComissaoPct { get; init; }
        public decimal OutrasVariaveisPct { get; init; }
        public long OutrasVariaveisCentavos { get; init; }
        public decimal TaxaPagamentoPct { get; init; }
        public decimal FixasPct { get; init; }
        public long FixasMensaisCentavos { get; init; }
        public decimal PecasPorMes { get; init; }
        public decimal MargemPct { get; init; }
    }

    public sealed record Analise(
        long PrecoCentavos, long CustoAquisicaoCentavos, long CustoCompraCentavos, long CustosAdicionaisCentavos,
        long ImpostosCentavos, long ComissaoCentavos, long TaxaPagamentoCentavos, long OutrasPctCentavos, long OutrasReaisCentavos,
        long VariaveisCentavos, long ContribuicaoCentavos, decimal ContribuicaoPct,
        long FixasPctCentavos, long RateioFixoCentavos, long FixasCentavos,
        long LucroCentavos, decimal MargemPct, decimal? MarkupPct);

    public sealed record Sugestao(bool Ok, IReadOnlyList<string> Erros, IReadOnlyList<string> Avisos,
        Analise? Analise, long PrecoSemArredondarCentavos, decimal SomaPercentuais);

    // ------------------------------------------------------------------ custo

    /// <summary>Custo de aquisição: compra + adicionais. Devolve (compra, extra, total).</summary>
    public static (long Compra, long Extra, long Total) CustoAquisicao(Parametros p)
    {
        long extra = 0;
        foreach (var a in p.Adicionais)
        {
            if (a.Valor <= 0) continue;
            extra += a.Percentual ? Arredondar(p.CustoCompraCentavos * a.Valor / 100m) : (long)a.Valor;
        }
        return (p.CustoCompraCentavos, extra, p.CustoCompraCentavos + extra);
    }

    /// <summary>Despesas fixas em centavos por peça (valor mensal ÷ peças por mês); zero se faltar um dos dois.</summary>
    public static long RateioFixoPorPeca(Parametros p) =>
        p.FixasMensaisCentavos > 0 && p.PecasPorMes > 0 ? Arredondar(p.FixasMensaisCentavos / p.PecasPorMes) : 0;

    public static (List<string> Erros, List<string> Avisos) Validar(Parametros p)
    {
        var erros = new List<string>();
        var avisos = new List<string>();
        if (p.CustoCompraCentavos <= 0) erros.Add("Informe o custo de compra do produto.");
        void Neg(decimal v, string nome) { if (v < 0) erros.Add($"{nome}: não aceita valor negativo."); }
        Neg(p.ImpostosPct, "Impostos"); Neg(p.ComissaoPct, "Comissão"); Neg(p.OutrasVariaveisPct, "Outras despesas variáveis (%)");
        Neg(p.TaxaPagamentoPct, "Taxa de pagamento"); Neg(p.FixasPct, "Despesas fixas (%)"); Neg(p.MargemPct, "Margem");
        if (p.OutrasVariaveisCentavos < 0 || p.FixasMensaisCentavos < 0 || p.PecasPorMes < 0)
            erros.Add("Valores em R$ e quantidades não podem ser negativos.");
        foreach (var a in p.Adicionais)
            if (a.Valor < 0) erros.Add($"{(string.IsNullOrWhiteSpace(a.Nome) ? "Custo adicional" : a.Nome)}: não aceita valor negativo.");
        if (p.FixasMensaisCentavos > 0 && p.PecasPorMes <= 0)
            avisos.Add("Há despesa fixa mensal, mas faltam as peças vendidas por mês: o rateio por peça não foi considerado.");
        if (erros.Count == 0 && CustoAquisicao(p).Total <= 0) erros.Add("O custo total precisa ser maior que zero.");
        return (erros.Distinct().ToList(), avisos);
    }

    // ------------------------------------------------------------------ análise de um preço

    /// <summary>Decompõe um preço em custos, despesas, contribuição e lucro. Cada linha é arredondada ao centavo, então os totais fecham.</summary>
    public static Analise Analisar(long precoCentavos, Parametros p)
    {
        var custo = CustoAquisicao(p);
        long Parte(decimal pct) => Arredondar(precoCentavos * pct / 100m);
        var impostos = Parte(p.ImpostosPct);
        var comissao = Parte(p.ComissaoPct);
        var pagamento = Parte(p.TaxaPagamentoPct);
        var outrasPct = Parte(p.OutrasVariaveisPct);
        var outrasReais = p.OutrasVariaveisCentavos;
        var variaveis = impostos + comissao + pagamento + outrasPct + outrasReais;
        var contribuicao = precoCentavos - custo.Total - variaveis;
        var fixasPct = Parte(p.FixasPct);
        var rateio = RateioFixoPorPeca(p);
        var fixas = fixasPct + rateio;
        var lucro = contribuicao - fixas;
        return new Analise(precoCentavos, custo.Total, custo.Compra, custo.Extra,
            impostos, comissao, pagamento, outrasPct, outrasReais, variaveis, contribuicao,
            precoCentavos > 0 ? (decimal)contribuicao * 100m / precoCentavos : 0,
            fixasPct, rateio, fixas, lucro,
            precoCentavos > 0 ? (decimal)lucro * 100m / precoCentavos : 0,
            custo.Total > 0 ? (decimal)(precoCentavos - custo.Total) * 100m / custo.Total : null);
    }

    // ------------------------------------------------------------------ preço sugerido

    /// <summary>Termina em ,90 sem ficar abaixo do calculado.</summary>
    private static long TerminarEm90(long centavos)
    {
        var reais = centavos / 100;
        var alvo = reais * 100 + 90;
        return alvo >= centavos ? alvo : (reais + 1) * 100 + 90;
    }

    /// <summary>Preço de venda para a margem pedida. Recusa (Ok = false) se os percentuais somam 100% ou mais.</summary>
    public static Sugestao Precificar(Parametros p, bool terminarEm90 = false)
    {
        var (erros, avisos) = Validar(p);
        if (erros.Count > 0) return new Sugestao(false, erros, avisos, null, 0, 0);

        var despesasPct = p.ImpostosPct + p.ComissaoPct + p.OutrasVariaveisPct + p.TaxaPagamentoPct + p.FixasPct;
        var soma = despesasPct + p.MargemPct;
        if (soma >= 100m)
            return new Sugestao(false, new[]
            {
                $"Despesas percentuais ({Pct(despesasPct)}%) e margem ({Pct(p.MargemPct)}%) somam {Pct(soma)}%. " +
                "Com 100% ou mais, nenhum preço cobre isso: reduza a margem ou as despesas."
            }, avisos, null, 0, soma);

        var custo = CustoAquisicao(p);
        var fixoReais = p.OutrasVariaveisCentavos + RateioFixoPorPeca(p);
        var bruto = (custo.Total + fixoReais) / (1m - soma / 100m);
        var semArredondar = (long)Math.Ceiling(bruto - 0.000000001m);
        var preco = terminarEm90 ? TerminarEm90(semArredondar) : semArredondar;
        return new Sugestao(true, Array.Empty<string>(), avisos, Analisar(preco, p), semArredondar, soma);
    }

    /// <summary>Menor preço em que o lucro estimado não fica negativo (margem 0); nulo se não for possível.</summary>
    public static long? PrecoDeEquilibrio(Parametros p)
    {
        var s = Precificar(p with { MargemPct = 0 });
        return s.Ok ? s.Analise!.PrecoCentavos : null;
    }

    // ------------------------------------------------------------------ promoção

    public sealed record Alerta(bool Erro, string Texto);

    public sealed record Promocao(bool Ok, string? Erro, long DescontoCentavos, decimal DescontoPct, long PrecoPromocionalCentavos,
        Analise? Antes, Analise? Depois, long DiferencaLucroCentavos, decimal DiferencaMargemPontos, long? PrecoDeEquilibrioCentavos,
        decimal? VendaExtraNecessariaPct, IReadOnlyList<Alerta> Alertas);

    private static Promocao FalhaPromocao(string erro) =>
        new(false, erro, 0, 0, 0, null, null, 0, 0, null, null, Array.Empty<Alerta>());

    /// <summary>Simula um desconto (em % ou em R$) e avisa de prejuízo, preço abaixo do equilíbrio, do mínimo ou da margem mínima.</summary>
    public static Promocao SimularPromocao(long precoOriginalCentavos, bool descontoEmValor, decimal desconto, Parametros p,
        long precoMinimoCentavos = 0, decimal margemMinimaPct = 0)
    {
        if (precoOriginalCentavos <= 0) return FalhaPromocao("Informe o preço original.");
        if (desconto < 0) return FalhaPromocao("O desconto não pode ser negativo.");
        if (!descontoEmValor && desconto >= 100) return FalhaPromocao("O desconto precisa ser menor que 100%.");
        var descontoC = descontoEmValor ? (long)desconto : Arredondar(precoOriginalCentavos * desconto / 100m);
        if (descontoC >= precoOriginalCentavos)
            return FalhaPromocao("O desconto é maior ou igual ao preço: o preço promocional ficaria zerado.");

        var promo = precoOriginalCentavos - descontoC;
        var antes = Analisar(precoOriginalCentavos, p);
        var depois = Analisar(promo, p);
        var equilibrio = PrecoDeEquilibrio(p);
        var alertas = new List<Alerta>();
        if (depois.LucroCentavos < 0)
            alertas.Add(new Alerta(true, $"Neste preço você tem prejuízo de {TextoBr.Numero(Math.Abs(depois.LucroCentavos) / 100m)} por peça (considerando os custos informados)."));
        else if (equilibrio is long eq && depois.PrecoCentavos < eq)
            alertas.Add(new Alerta(true, "Preço abaixo do preço de equilíbrio."));
        if (precoMinimoCentavos > 0 && depois.PrecoCentavos < precoMinimoCentavos)
            alertas.Add(new Alerta(false, "Preço abaixo do mínimo que você definiu."));
        if (margemMinimaPct > 0 && depois.MargemPct < margemMinimaPct)
            alertas.Add(new Alerta(false, "Margem abaixo da mínima que você definiu."));
        if (depois.CustoAquisicaoCentavos <= 0)
            alertas.Add(new Alerta(false, "Sem custo informado: o lucro mostrado não é confiável."));

        // quanto a venda precisa crescer para o lucro total ficar igual ao do preço normal
        decimal? extra = antes.LucroCentavos > 0 && depois.LucroCentavos > 0
            ? ((decimal)antes.LucroCentavos / depois.LucroCentavos - 1m) * 100m : null;
        return new Promocao(true, null, descontoC, (decimal)descontoC * 100m / precoOriginalCentavos, promo, antes, depois,
            depois.LucroCentavos - antes.LucroCentavos, depois.MargemPct - antes.MargemPct, equilibrio, extra, alertas);
    }

    // ------------------------------------------------------------------ formas de pagamento

    /// <summary>Taxa por forma de pagamento; Taxa nulo = "não configurada" (nunca se inventa taxa, conta 0% só para a conta).</summary>
    public sealed record CenarioPagamento(string Rotulo, decimal? TaxaPct);

    public sealed record LinhaPagamento(string Rotulo, bool Configurada, decimal TaxaPct, long TaxaCentavos,
        long RecebidoCentavos, long LucroCentavos, decimal MargemPct);

    /// <summary>Compara o mesmo preço em cada forma de pagamento (recebido = preço − taxa; não considera prazo nem antecipação).</summary>
    public static IReadOnlyList<LinhaPagamento> CompararPagamentos(long precoCentavos, Parametros p, IEnumerable<CenarioPagamento> cenarios) =>
        cenarios.Select(c =>
        {
            var taxa = c.TaxaPct ?? 0;
            var a = Analisar(precoCentavos, p with { TaxaPagamentoPct = taxa });
            return new LinhaPagamento(c.Rotulo, c.TaxaPct is not null, taxa, a.TaxaPagamentoCentavos,
                precoCentavos - a.TaxaPagamentoCentavos, a.LucroCentavos, a.MargemPct);
        }).ToList();

    // ------------------------------------------------------------------ quantidades

    public sealed record LinhaQuantidade(int Quantidade, long FaturamentoCentavos, long CustoMercadoriasCentavos,
        long VariaveisCentavos, long ContribuicaoCentavos, long FixasCentavos, long LucroCentavos);

    public sealed record Quantidades(IReadOnlyList<LinhaQuantidade> Linhas, long LucroPorPecaCentavos,
        long? QuantidadeParaMeta, bool MetaImpossivel, long? EquilibrioMensalPecas);

    /// <summary>Resultado por quantidade vendida; opcionalmente quantas peças bastam para uma meta de lucro e o equilíbrio mensal das fixas.</summary>
    public static Quantidades SimularQuantidades(long precoCentavos, Parametros p, IEnumerable<int> quantidades, long metaLucroCentavos = 0)
    {
        var a = Analisar(precoCentavos, p);
        var linhas = quantidades.Where(q => q >= 1).Select(q => new LinhaQuantidade(q, a.PrecoCentavos * q,
            a.CustoAquisicaoCentavos * q, a.VariaveisCentavos * q, a.ContribuicaoCentavos * q, a.FixasCentavos * q, a.LucroCentavos * q)).ToList();
        long? qtdMeta = metaLucroCentavos > 0 && a.LucroCentavos > 0
            ? (long)Math.Ceiling((decimal)metaLucroCentavos / a.LucroCentavos) : null;
        var contribAposPct = a.ContribuicaoCentavos - a.FixasPctCentavos;
        long? equilibrio = p.FixasMensaisCentavos > 0 && contribAposPct > 0
            ? (long)Math.Ceiling((decimal)p.FixasMensaisCentavos / contribAposPct) : null;
        return new Quantidades(linhas, a.LucroCentavos, qtdMeta, metaLucroCentavos > 0 && a.LucroCentavos <= 0, equilibrio);
    }

    // ------------------------------------------------------------------ custo de lote

    public sealed record ItemLote(string Nome, int Quantidade, long CustoUnitarioCentavos);

    /// <summary>Despesa do lote (frete etc.), rateada pelo valor de compra de cada item ou igualmente por peça.</summary>
    public sealed record DespesaLote(string Nome, long ValorCentavos, bool PorQuantidade);

    public sealed record ItemLoteRateado(string Nome, int Quantidade, long CustoUnitarioCentavos, long RateioUnitarioCentavos,
        long CustoComRateioCentavos, long TotalItemCentavos);

    public sealed record ResultadoLote(IReadOnlyList<ItemLoteRateado> Itens, long TotalLoteCentavos, long TotalDespesasCentavos);

    /// <summary>Distribui as despesas do lote entre as peças; a soma dos rateios fecha exatamente (o resto de centavos vai para o último item).</summary>
    public static ResultadoLote RatearLote(IReadOnlyList<ItemLote> itens, IReadOnlyList<DespesaLote> despesas)
    {
        if (itens.Any(i => i.Quantidade < 0 || i.CustoUnitarioCentavos < 0))
            throw new RegraDeNegocioException("Quantidade e custo não podem ser negativos.");
        if (despesas.Any(d => d.ValorCentavos < 0))
            throw new RegraDeNegocioException("As despesas do lote não podem ser negativas.");
        var validos = itens.Where(i => i.Quantidade >= 1).ToList();
        if (validos.Count == 0) throw new RegraDeNegocioException("Informe pelo menos um item com quantidade e custo.");

        var compra = validos.Select(i => i.CustoUnitarioCentavos * i.Quantidade).ToList();
        var totalCompra = compra.Sum();
        var totalPecas = validos.Sum(i => (long)i.Quantidade);
        var rateios = new long[validos.Count];

        foreach (var d in despesas.Where(d => d.ValorCentavos > 0))
        {
            var porValor = !d.PorQuantidade && totalCompra > 0;
            long distribuido = 0;
            for (var i = 0; i < validos.Count; i++)
            {
                var parte = i == validos.Count - 1 ? d.ValorCentavos - distribuido
                    : Arredondar(porValor ? (decimal)d.ValorCentavos * compra[i] / totalCompra : (decimal)d.ValorCentavos * validos[i].Quantidade / totalPecas);
                distribuido += parte;
                rateios[i] += parte;
            }
        }

        var resultado = validos.Select((it, i) =>
        {
            var unit = Arredondar((decimal)rateios[i] / it.Quantidade);
            return new ItemLoteRateado(it.Nome, it.Quantidade, it.CustoUnitarioCentavos, unit,
                it.CustoUnitarioCentavos + unit, compra[i] + rateios[i]);
        }).ToList();
        return new ResultadoLote(resultado, totalCompra + rateios.Sum(), rateios.Sum());
    }

    // ------------------------------------------------------------------ produto do cadastro

    /// <summary>O que falta no cadastro do produto para precificar com segurança.</summary>
    public static IReadOnlyList<string> AvaliarProduto(long custoCentavos, long varejoCentavos, long atacadoCentavos)
    {
        var avisos = new List<string>();
        if (custoCentavos <= 0) avisos.Add("Sem custo cadastrado: informe o custo de compra para o cálculo valer.");
        if (varejoCentavos <= 0) avisos.Add("Sem preço de varejo cadastrado.");
        if (varejoCentavos > 0 && atacadoCentavos > varejoCentavos) avisos.Add("O preço de atacado está maior que o de varejo.");
        if (custoCentavos > 0 && varejoCentavos > 0 && varejoCentavos < custoCentavos) avisos.Add("O preço de varejo atual está abaixo do custo cadastrado.");
        return avisos;
    }

    private static long Arredondar(decimal v) => (long)Math.Round(v, MidpointRounding.AwayFromZero);

    private static string Pct(decimal v) => Math.Round(v, 1).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
