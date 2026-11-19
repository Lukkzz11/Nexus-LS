using System.Globalization;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Core.Consultor;

/// <summary>
/// Uma regra do Consultor: recebe números já somados e devolve um cartão, ou nulo quando não há o que dizer (inclusive
/// quando faltam dados para a conta ser confiável). Regra pura: sem banco, sem relógio (usa <see cref="DadosConsultor.Hoje"/>).
/// </summary>
public interface IRegraConsultor
{
    string Id { get; }
    AreaConsultor Area { get; }
    Sugestao? Avaliar(DadosConsultor dados);
}

/// <summary>Todas as regras, na ordem em que desempatam o cartão (a de cima aparece antes quando a gravidade é igual).</summary>
public static class RegrasDoConsultor
{
    public static IReadOnlyList<IRegraConsultor> Padrao() =>
    [
        new RegraContasVencidas(),
        new RegraContasAVencer(),
        new RegraSaldoDoMesNegativo(),
        new RegraRiscoDeRuptura(),
        new RegraEstoqueParado(),
        new RegraFaturamentoDaSemana(),
        new RegraTicketMedioCaiu(),
        new RegraDiaMaisFraco(),
        new RegraAniversariantes(),
        new RegraFormaDePagamentoDominante()
    ];
}

/// <summary>Funções de texto e de soma que as regras dividem (tudo em pt-BR, independente da cultura do Windows).</summary>
internal static class TextoConsultor
{
    public static readonly CultureInfo PtBr = new("pt-BR");

    public static string Pct(decimal fracao) => Math.Round(Math.Abs(fracao) * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", PtBr) + "%";

    public static string Data(DateTime d) => d.ToString("dd/MM", PtBr);

    public static string Dias(int n) => n == 1 ? "1 dia" : $"{n} dias";

    public static string Pecas(int n) => n == 1 ? "1 peça" : $"{n.ToString("N0", PtBr)} peças";

    public static string Produtos(int n) => n == 1 ? "1 produto" : $"{n} produtos";

    /// <summary>"A, B e C". Passando do máximo: "A, B, C e mais 2".</summary>
    public static string Lista(IReadOnlyList<string> itens, int maximo = 3)
    {
        if (itens.Count == 0) return "";
        var mostrar = itens.Take(maximo).ToList();
        var resto = itens.Count - mostrar.Count;
        if (resto > 0) return string.Join(", ", mostrar) + $" e mais {resto}";
        return mostrar.Count == 1 ? mostrar[0] : string.Join(", ", mostrar.Take(mostrar.Count - 1)) + " e " + mostrar[^1];
    }

    private static readonly string[] Semana = { "domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado" };
    public static string DiaDaSemana(DayOfWeek d) => Semana[(int)d];

    /// <summary>Soma das vendas dos dias de <paramref name="de"/> a <paramref name="ate"/> (inclusive).</summary>
    public static (int Vendas, long Total) Soma(IReadOnlyList<VendasDoDia> dias, DateTime de, DateTime ate)
    {
        var doPeriodo = dias.Where(d => d.Dia.Date >= de.Date && d.Dia.Date <= ate.Date).ToList();
        return (doPeriodo.Sum(d => d.Vendas), doPeriodo.Sum(d => d.TotalCentavos));
    }

    /// <summary>Verdadeiro quando há venda registrada desde antes de <paramref name="inicio"/>: o histórico da comparação existe.</summary>
    public static bool TemHistoricoDesde(DadosConsultor d, DateTime inicio) => d.PrimeiraVenda is DateTime p && p.Date <= inicio.Date;
}

/// <summary>Contas a pagar já vencidas (regra 6 da proposta). Qualquer conta vencida gera o cartão.</summary>
public sealed class RegraContasVencidas : IRegraConsultor
{
    public const string IdFixo = "contas-vencidas";
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Financeiro;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var vencidas = d.Contas.Where(c => c.Vencimento.Date < d.Hoje.Date).OrderBy(c => c.Vencimento).ThenBy(c => c.Id).ToList();
        if (vencidas.Count == 0) return null;
        var total = vencidas.Sum(c => c.ValorCentavos);
        var maisAntiga = vencidas[0];
        var dias = (d.Hoje.Date - maisAntiga.Vencimento.Date).Days;
        var qtd = vencidas.Count == 1 ? "1 conta a pagar está vencida" : $"{vencidas.Count} contas a pagar estão vencidas";
        return new Sugestao(IdFixo, "Contas vencidas",
            $"{qtd}, somando {Dinheiro.Formatar(total)}. A mais antiga é \"{maisAntiga.Descricao}\" ({Dinheiro.Formatar(maisAntiga.ValorCentavos)}), vencida há {TextoConsultor.Dias(dias)}.",
            GravidadeSugestao.Urgente, AreaConsultor.Financeiro, "Abrir o Financeiro")
        { Destino = DestinoConsultor.Financeiro };
    }
}

/// <summary>Contas a pagar que vencem de hoje até daqui a 7 dias.</summary>
public sealed class RegraContasAVencer : IRegraConsultor
{
    public const string IdFixo = "contas-a-vencer";
    public const int Dias = 7;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Financeiro;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var limite = d.Hoje.Date.AddDays(Dias);
        var aVencer = d.Contas.Where(c => c.Vencimento.Date >= d.Hoje.Date && c.Vencimento.Date <= limite)
            .OrderBy(c => c.Vencimento).ThenBy(c => c.Id).ToList();
        if (aVencer.Count == 0) return null;
        var total = aVencer.Sum(c => c.ValorCentavos);
        var primeira = aVencer[0];
        var quando = primeira.Vencimento.Date == d.Hoje.Date ? "hoje" : $"em {TextoConsultor.Data(primeira.Vencimento)}";
        var qtd = aVencer.Count == 1 ? "1 conta vence" : $"{aVencer.Count} contas vencem";
        return new Sugestao(IdFixo, "Contas a vencer nesta semana",
            $"{qtd} nos próximos {Dias} dias, somando {Dinheiro.Formatar(total)}. A primeira é \"{primeira.Descricao}\" ({Dinheiro.Formatar(primeira.ValorCentavos)}), {quando}.",
            GravidadeSugestao.Atencao, AreaConsultor.Financeiro, "Abrir o Financeiro")
        { Destino = DestinoConsultor.Financeiro };
    }
}

/// <summary>O mês está fechando no vermelho (entradas menores que saídas). Só depois do dia 5, para não assustar com poucos dias de dado.</summary>
public sealed class RegraSaldoDoMesNegativo : IRegraConsultor
{
    public const string IdFixo = "saldo-do-mes-negativo";
    public const int DiaMinimo = 5;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Financeiro;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        if (d.Saldo is not { } s) return null;
        if (d.Hoje.Day < DiaMinimo) return null;
        if (s.EntradasCentavos == 0 && s.SaidasCentavos == 0) return null; // nada lançado: não há o que comparar
        if (s.SaldoCentavos >= 0) return null;
        return new Sugestao(IdFixo, "Saldo do mês negativo",
            $"De {TextoConsultor.Data(s.Inicio)} a {TextoConsultor.Data(s.Fim)} entraram {Dinheiro.Formatar(s.EntradasCentavos)} e saíram {Dinheiro.Formatar(s.SaidasCentavos)}: o saldo está em {Dinheiro.Formatar(s.SaldoCentavos)}.",
            GravidadeSugestao.Atencao, AreaConsultor.Financeiro, "Ver o relatório financeiro")
        { Destino = DestinoConsultor.RelatorioFinanceiro };
    }
}

/// <summary>Produto que vende e está acabando: estoque dá menos de 15 dias no ritmo dos últimos 30. Zerado = urgente.</summary>
public sealed class RegraRiscoDeRuptura : IRegraConsultor
{
    public const string IdFixo = "risco-de-ruptura";
    public const int PecasMinimas = 5;
    public const int DiasDeCobertura = 15;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Estoque;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var risco = d.ProdutosComVenda
            .Where(p => p.PecasVendidasUltimos30Dias >= PecasMinimas)
            .Select(p => (Produto: p, Cobertura: p.Estoque <= 0 ? 0m : p.Estoque / (p.PecasVendidasUltimos30Dias / 30m)))
            .Where(x => x.Cobertura < DiasDeCobertura)
            .OrderBy(x => x.Cobertura).ThenByDescending(x => x.Produto.PecasVendidasUltimos30Dias).ThenBy(x => x.Produto.Nome, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (risco.Count == 0) return null;

        var zerados = risco.Count(x => x.Produto.Estoque <= 0);
        var detalhes = risco.Take(3).Select(x => $"{x.Produto.Nome} ({(x.Produto.Estoque <= 0 ? "sem estoque" : $"{TextoConsultor.Pecas(x.Produto.Estoque)} em estoque")}, {TextoConsultor.Pecas(x.Produto.PecasVendidasUltimos30Dias)} vendidas em 30 dias)").ToList();
        var inicio = risco.Count == 1
            ? "1 produto que vende está acabando"
            : $"{risco.Count} produtos que vendem estão acabando";
        var texto = $"{inicio}: {string.Join("; ", detalhes)}" + (risco.Count > 3 ? $"; e mais {risco.Count - 3}." : ".");
        return new Sugestao(IdFixo, zerados > 0 ? "Produto que vende está em falta" : "Estoque baixo em produto que vende", texto,
            zerados > 0 ? GravidadeSugestao.Urgente : GravidadeSugestao.Atencao, AreaConsultor.Estoque, "Ver o estoque")
        { Destino = DestinoConsultor.Estoque };
    }
}

/// <summary>Estoque parado: produtos que já venderam, têm estoque e não saem há 60 dias ou mais, com custo parado alto (ou muitos itens).</summary>
public sealed class RegraEstoqueParado : IRegraConsultor
{
    public const string IdFixo = "estoque-parado";
    public const int DiasParado = 60;
    public const long CustoMinimoCentavos = 50_000;
    public const int QuantidadeMinima = 10;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Estoque;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var parados = d.Parados.Where(p => p.DiasSemVenda is int n && n >= DiasParado && p.Estoque > 0).ToList();
        if (parados.Count == 0) return null;
        var custo = RelatorioProdutos.CustoTotalParado(parados);
        if ((custo ?? 0) < CustoMinimoCentavos && parados.Count < QuantidadeMinima) return null;

        var semCusto = parados.Count(p => p.CustoParadoCentavos is null);
        var pecas = parados.Sum(p => p.Estoque);
        var nomes = parados.Take(3).Select(p => p.Nome).ToList();
        var texto = $"{TextoConsultor.Produtos(parados.Count)} com estoque ({TextoConsultor.Pecas(pecas)}) não vendem há {DiasParado} dias ou mais"
            + (custo is long c ? $", com {Dinheiro.Formatar(c)} de custo parado" : ", sem custo cadastrado para somar")
            + $". Os que mais prendem dinheiro: {TextoConsultor.Lista(nomes)}."
            + (semCusto > 0 && custo is not null ? $" ({TextoConsultor.Produtos(semCusto)} sem custo cadastrado " + (semCusto == 1 ? "ficou fora da soma.)" : "ficaram fora da soma.)") : "");
        return new Sugestao(IdFixo, "Estoque parado", texto, GravidadeSugestao.Atencao, AreaConsultor.Estoque, "Ver o estoque parado")
        { Destino = DestinoConsultor.EstoqueParado };
    }
}

/// <summary>Faturamento dos últimos 7 dias fechados contra os 7 anteriores: cartão se variou 15% ou mais.</summary>
public sealed class RegraFaturamentoDaSemana : IRegraConsultor
{
    public const string IdFixo = "faturamento-da-semana";
    public const decimal Limite = 0.15m;
    public const int VendasMinimasNaSemanaAnterior = 5;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Vendas;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var hoje = d.Hoje.Date;
        if (!TextoConsultor.TemHistoricoDesde(d, hoje.AddDays(-14))) return null;
        var atual = TextoConsultor.Soma(d.VendasPorDia, hoje.AddDays(-7), hoje.AddDays(-1));
        var anterior = TextoConsultor.Soma(d.VendasPorDia, hoje.AddDays(-14), hoje.AddDays(-8));
        if (anterior.Vendas < VendasMinimasNaSemanaAnterior || anterior.Total <= 0) return null;

        var variacao = (atual.Total - anterior.Total) / (decimal)anterior.Total;
        if (Math.Abs(variacao) < Limite) return null;
        var caiu = variacao < 0;
        var periodo = $"De {TextoConsultor.Data(hoje.AddDays(-7))} a {TextoConsultor.Data(hoje.AddDays(-1))} a loja vendeu {Dinheiro.Formatar(atual.Total)}";
        var texto = $"{periodo}, contra {Dinheiro.Formatar(anterior.Total)} nos 7 dias anteriores: {TextoConsultor.Pct(variacao)} {(caiu ? "a menos" : "a mais")}.";
        return new Sugestao(IdFixo, caiu ? "Vendas da semana abaixo da anterior" : "Vendas da semana acima da anterior", texto,
            caiu ? GravidadeSugestao.Atencao : GravidadeSugestao.Informacao, AreaConsultor.Vendas, "Ver o desempenho de vendas")
        { Destino = DestinoConsultor.Desempenho };
    }
}

/// <summary>Ticket médio dos últimos 7 dias fechados contra as 4 semanas antes: cartão se caiu 10% ou mais.</summary>
public sealed class RegraTicketMedioCaiu : IRegraConsultor
{
    public const string IdFixo = "ticket-medio-caiu";
    public const decimal QuedaMinima = 0.10m;
    public const int VendasMinimasSemana = 10;
    public const int VendasMinimasAntes = 30;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Vendas;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var hoje = d.Hoje.Date;
        if (!TextoConsultor.TemHistoricoDesde(d, hoje.AddDays(-35))) return null;
        var atual = TextoConsultor.Soma(d.VendasPorDia, hoje.AddDays(-7), hoje.AddDays(-1));
        var antes = TextoConsultor.Soma(d.VendasPorDia, hoje.AddDays(-35), hoje.AddDays(-8));
        if (atual.Vendas < VendasMinimasSemana || antes.Vendas < VendasMinimasAntes) return null;

        var ticketAtual = atual.Total / (decimal)atual.Vendas;
        var ticketAntes = antes.Total / (decimal)antes.Vendas;
        if (ticketAntes <= 0) return null;
        var queda = (ticketAntes - ticketAtual) / ticketAntes;
        if (queda < QuedaMinima) return null;
        return new Sugestao(IdFixo, "Ticket médio caiu",
            $"O ticket médio dos últimos 7 dias foi {Dinheiro.Formatar((long)Math.Round(ticketAtual, MidpointRounding.AwayFromZero))} ({atual.Vendas} vendas), contra {Dinheiro.Formatar((long)Math.Round(ticketAntes, MidpointRounding.AwayFromZero))} nas 4 semanas anteriores ({antes.Vendas} vendas): {TextoConsultor.Pct(queda)} a menos. Vale conferir peças por venda e descontos.",
            GravidadeSugestao.Atencao, AreaConsultor.Vendas, "Ver o desempenho de vendas")
        { Destino = DestinoConsultor.Desempenho };
    }
}

/// <summary>Dia da semana mais fraco nas últimas 8 semanas (só entre os dias em que a loja vende), quando fica abaixo de 50% da média.</summary>
public sealed class RegraDiaMaisFraco : IRegraConsultor
{
    public const string IdFixo = "dia-mais-fraco";
    public const decimal LimiteDaMedia = 0.5m;
    public const int VendasMinimas = 40;
    public const int Semanas = 8;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Vendas;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var hoje = d.Hoje.Date;
        var inicio = hoje.AddDays(-7 * Semanas);
        if (!TextoConsultor.TemHistoricoDesde(d, inicio)) return null;
        var dias = d.VendasPorDia.Where(x => x.Dia.Date >= inicio && x.Dia.Date < hoje).ToList();
        if (dias.Sum(x => x.Vendas) < VendasMinimas) return null;

        // Média por dia da semana; o dia em que a loja nunca vendeu nas 8 semanas é dia fechado e não conta.
        var medias = dias.GroupBy(x => x.Dia.DayOfWeek)
            .Where(g => g.Sum(x => x.TotalCentavos) > 0)
            .Select(g => (Dia: g.Key, Media: g.Sum(x => x.TotalCentavos) / (decimal)Semanas))
            .ToList();
        if (medias.Count < 3) return null;

        var mediaGeral = medias.Average(m => m.Media);
        var fraco = medias.OrderBy(m => m.Media).ThenBy(m => (int)m.Dia).First();
        var forte = medias.OrderByDescending(m => m.Media).ThenBy(m => (int)m.Dia).First();
        if (mediaGeral <= 0 || fraco.Media >= mediaGeral * LimiteDaMedia) return null;

        string Valor(decimal v) => Dinheiro.Formatar((long)Math.Round(v, MidpointRounding.AwayFromZero));
        return new Sugestao(IdFixo, "Dia mais fraco da semana",
            $"Nas últimas {Semanas} semanas, {TextoConsultor.DiaDaSemana(fraco.Dia)} vendeu em média {Valor(fraco.Media)}, menos da metade da média dos dias em que a loja vende ({Valor(mediaGeral)}). O dia mais forte foi {TextoConsultor.DiaDaSemana(forte.Dia)}, com {Valor(forte.Media)}.",
            GravidadeSugestao.Informacao, AreaConsultor.Vendas, "Ver o desempenho de vendas")
        { Destino = DestinoConsultor.Desempenho };
    }
}

/// <summary>Clientes que fazem aniversário nos próximos 7 dias.</summary>
public sealed class RegraAniversariantes : IRegraConsultor
{
    public const string IdFixo = "aniversariantes-da-semana";
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Vendas;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        if (d.Aniversariantes.Count == 0) return null;
        string Quando(AvisosAoAbrir.Aniversariante a) => a.DiasAte switch
        {
            0 => "hoje",
            1 => "amanhã",
            _ => $"em {TextoConsultor.Dias(a.DiasAte)}, dia {TextoConsultor.Data(a.Data)}"
        };
        var nomes = d.Aniversariantes.Take(3).Select(a => $"{a.Nome} ({Quando(a)})").ToList();
        var resto = d.Aniversariantes.Count - nomes.Count;
        var inicio = d.Aniversariantes.Count == 1 ? "1 cliente faz" : $"{d.Aniversariantes.Count} clientes fazem";
        return new Sugestao(IdFixo, "Aniversariantes da semana",
            $"{inicio} aniversário nos próximos {AvisosAoAbrir.DiasAvisoAniversario} dias: {string.Join(", ", nomes)}" + (resto > 0 ? $" e mais {resto}." : "."),
            GravidadeSugestao.Informacao, AreaConsultor.Vendas, "Abrir os clientes")
        { Destino = DestinoConsultor.Clientes };
    }
}

/// <summary>Forma de pagamento que domina as vendas dos últimos 30 dias (60% ou mais do valor, com pelo menos 20 vendas).</summary>
public sealed class RegraFormaDePagamentoDominante : IRegraConsultor
{
    public const string IdFixo = "forma-de-pagamento-dominante";
    public const decimal ParticipacaoMinima = 0.60m;
    public const int VendasMinimas = 20;
    public string Id => IdFixo;
    public AreaConsultor Area => AreaConsultor.Vendas;

    public Sugestao? Avaliar(DadosConsultor d)
    {
        var vendas = d.Formas.Sum(f => f.Vendas);
        var total = d.Formas.Sum(f => f.TotalCentavos);
        if (vendas < VendasMinimas || total <= 0) return null;
        var topo = d.Formas.OrderByDescending(f => f.TotalCentavos).ThenBy(f => (int)f.Forma).First();
        var parte = topo.TotalCentavos / (decimal)total;
        if (parte < ParticipacaoMinima) return null;

        var nome = SelecaoPagamento.Nome(topo.Forma);
        var aPrazo = topo.Forma == FormaPagamento.APrazo;
        return new Sugestao(IdFixo, $"{nome} domina as vendas",
            $"Nos últimos 30 dias, {nome} respondeu por {TextoConsultor.Pct(parte)} do valor vendido ({Dinheiro.Formatar(topo.TotalCentavos)} de {Dinheiro.Formatar(total)}, em {topo.Vendas} de {vendas} vendas)."
            + (aPrazo ? " Venda a prazo é dinheiro a receber: vale acompanhar os vencimentos." : ""),
            aPrazo ? GravidadeSugestao.Atencao : GravidadeSugestao.Informacao, AreaConsultor.Vendas, "Ver o desempenho de vendas")
        { Destino = DestinoConsultor.Desempenho };
    }
}
