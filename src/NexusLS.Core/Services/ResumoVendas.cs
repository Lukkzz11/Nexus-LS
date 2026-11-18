using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// "Resumo completo de vendas" de um período (com comparação ao período anterior de mesmo tamanho) e a pesquisa por
/// referência/produto. Só usa o que o banco já tem. Vendas canceladas ficam fora de todos os totais e só aparecem
/// contadas à parte. Funções puras sobre as vendas (testáveis); a leitura do banco fica em <see cref="ResumoVendasService"/>.
/// </summary>
public static class ResumoVendas
{
    public sealed record Comparativo(long Atual, long Anterior, decimal? VariacaoPct);
    public sealed record FormaTotal(FormaPagamento Forma, long ValorCentavos, int Vendas);
    public sealed record DiaTotal(DayOfWeek Dia, long ValorCentavos, int Vendas);
    public sealed record HoraTotal(int Hora, long ValorCentavos, int Vendas);
    public sealed record ProdutoTotal(int ProdutoId, string Nome, int Pecas, long ValorCentavos);
    public sealed record VendedorTotal(string Vendedor, long ValorCentavos, int Vendas);

    public sealed record Completo(
        DesempenhoVendas.Intervalo Periodo, DesempenhoVendas.Intervalo Anterior,
        Comparativo Total, Comparativo Vendas, Comparativo Pecas, Comparativo TicketMedio,
        long DescontosCentavos, long AcrescimosCentavos, int Canceladas, long CanceladasCentavos,
        IReadOnlyList<FormaTotal> PorForma,
        IReadOnlyList<DiaTotal> PorDiaSemana, IReadOnlyList<DiaTotal> MelhoresDias,
        IReadOnlyList<HoraTotal> MelhoresHorarios,
        IReadOnlyList<ProdutoTotal> MaisVendidosPorQuantidade, IReadOnlyList<ProdutoTotal> MaisVendidosPorValor,
        IReadOnlyList<VendedorTotal>? PorVendedor);

    public sealed record VariacaoBusca(string Tamanho, int Unidades, long ValorCentavos);
    public sealed record VendaBusca(DateTime DataHora, int VendaId, string Produto, string Tamanho, int Quantidade, long ValorCentavos);

    public sealed record ResultadoBusca(string Texto, int ProdutosEncontrados, int Unidades, long ValorCentavos,
        IReadOnlyList<VariacaoBusca> Variacoes, IReadOnlyList<VendaBusca> Vendas)
    {
        public bool Achou => Vendas.Count > 0;
    }

    /// <summary>Dados do cadastro usados pela pesquisa.</summary>
    public sealed record ProdutoCadastro(int Id, string Nome, string? Referencia, string? CodigoInterno, string? CodigoBarras);

    // ------------------------------------------------------------------ texto

    /// <summary>Minúsculas, sem acento e sem espaços nas pontas: base da pesquisa.</summary>
    public static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var d = texto.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    // ------------------------------------------------------------------ resumo

    private static IEnumerable<Venda> Entre(IEnumerable<Venda> vendas, DesempenhoVendas.Intervalo p, bool canceladas) => vendas
        .Where(v => v.Cancelada == canceladas && v.DataHora.Date >= p.Inicio.Date && v.DataHora.Date <= p.Fim.Date);

    private static int Pecas(Venda v) => v.Itens.Where(i => i.Quantidade > 0).Sum(i => i.Quantidade);

    private static long Ticket(long total, int vendas) =>
        vendas > 0 ? (long)Math.Round((decimal)total / vendas, MidpointRounding.AwayFromZero) : 0;

    private static Comparativo Comparar(long atual, long anterior) => new(atual, anterior, DesempenhoVendas.Variacao(atual, anterior));

    /// <param name="vendas">Todas as vendas que cobrem o período e o anterior (inclusive canceladas).</param>
    public static Completo Gerar(IEnumerable<Venda> vendas, DesempenhoVendas.Intervalo periodo)
    {
        var todas = vendas as IReadOnlyCollection<Venda> ?? vendas.ToList();
        var anterior = DesempenhoVendas.Anterior(periodo);
        var atuais = Entre(todas, periodo, false).ToList();
        var antes = Entre(todas, anterior, false).ToList();
        var canceladas = Entre(todas, periodo, true).ToList();

        long total = atuais.Sum(v => v.TotalCentavos), totalAntes = antes.Sum(v => v.TotalCentavos);
        int pecas = atuais.Sum(Pecas), pecasAntes = antes.Sum(Pecas);

        var porForma = atuais.GroupBy(v => v.FormaPagamento)
            .Select(g => new FormaTotal(g.Key, g.Sum(v => v.TotalCentavos), g.Count()))
            .OrderByDescending(f => f.ValorCentavos).ToList();

        var dias = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
        var porDia = dias.Select(d =>
        {
            var doDia = atuais.Where(v => v.DataHora.DayOfWeek == d).ToList();
            return new DiaTotal(d, doDia.Sum(v => v.TotalCentavos), doDia.Count);
        }).ToList();

        var horarios = atuais.GroupBy(v => v.DataHora.Hour)
            .Select(g => new HoraTotal(g.Key, g.Sum(v => v.TotalCentavos), g.Count()))
            .OrderByDescending(h => h.ValorCentavos).ThenBy(h => h.Hora).Take(5).ToList();

        var linhas = DesempenhoVendas.ExpandirLinhas(atuais, new Dictionary<int, (string?, long)>());
        var produtos = linhas.Where(l => l.ProdutoId is not null).GroupBy(l => l.ProdutoId!.Value)
            .Select(g => new ProdutoTotal(g.Key, g.OrderByDescending(l => l.VendaId).First().Nome, g.Sum(l => l.Quantidade), g.Sum(l => l.ValorCentavos)))
            .ToList();

        return new Completo(periodo, anterior,
            Comparar(total, totalAntes),
            Comparar(atuais.Count, antes.Count),
            Comparar(pecas, pecasAntes),
            Comparar(Ticket(total, atuais.Count), Ticket(totalAntes, antes.Count)),
            atuais.Sum(v => v.DescontoCentavos), atuais.Sum(v => v.AcrescimoCentavos),
            canceladas.Count, canceladas.Sum(v => v.TotalCentavos),
            porForma, porDia,
            porDia.Where(d => d.Vendas > 0).OrderByDescending(d => d.ValorCentavos).ThenBy(d => d.Dia).Take(3).ToList(),
            horarios,
            produtos.OrderByDescending(p => p.Pecas).ThenByDescending(p => p.ValorCentavos).ThenBy(p => p.Nome).Take(10).ToList(),
            produtos.OrderByDescending(p => p.ValorCentavos).ThenByDescending(p => p.Pecas).ThenBy(p => p.Nome).Take(10).ToList(),
            PorVendedor(atuais));
    }

    /// <summary>Por quem vendeu; nulo quando ninguém pode ser identificado (operador genérico ou vazio).</summary>
    private static List<VendedorTotal>? PorVendedor(List<Venda> atuais)
    {
        var ident = atuais.Select(v => (Venda: v, Id: DesempenhoVendas.IdentidadeDoOperador(v.Operador))).ToList();
        if (!ident.Any(x => x.Id.Confiavel)) return null;
        return ident.GroupBy(x => x.Id.Nome)
            .Select(g => new VendedorTotal(g.Key, g.Sum(x => x.Venda.TotalCentavos), g.Count()))
            .OrderByDescending(x => x.ValorCentavos).ToList();
    }

    // ------------------------------------------------------------------ pesquisa por referência/produto

    /// <summary>
    /// Vendas do período (sem as canceladas) em que apareceu o texto procurado: referência, código interno, código de barras
    /// ou nome (sem acento, sem diferenciar maiúsculas). Valores já com o desconto/acréscimo da venda rateado.
    /// </summary>
    public static ResultadoBusca Pesquisar(IEnumerable<Venda> vendas, IEnumerable<ProdutoCadastro> cadastro,
        DesempenhoVendas.Intervalo periodo, string? texto)
    {
        var termo = Normalizar(texto);
        if (termo.Length == 0) return new ResultadoBusca("", 0, 0, 0, Array.Empty<VariacaoBusca>(), Array.Empty<VendaBusca>());

        var tokens = termo.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var achados = cadastro.Where(p =>
                new[] { p.Referencia, p.CodigoInterno, p.CodigoBarras }.Any(c => Normalizar(c).Contains(termo)) ||
                TemTodos(Normalizar(p.Nome), tokens))
            .Select(p => p.Id).ToHashSet();

        var achados2 = new List<VendaBusca>();
        var produtosVistos = new HashSet<int>();
        foreach (var v in Entre(vendas, periodo, false).OrderBy(v => v.DataHora).ThenBy(v => v.Id))
        {
            var itens = v.Itens.Where(i => i.Quantidade > 0).ToList();
            var somaBruto = itens.Sum(i => i.TotalCentavos);
            if (itens.Count == 0 || somaBruto <= 0) continue;
            foreach (var (item, valor) in DesempenhoVendas.ValoresLiquidos(v, itens, somaBruto))
            {
                if (!achados.Contains(item.ProdutoId) && !TemTodos(Normalizar(item.NomeProduto), tokens)) continue;
                produtosVistos.Add(item.ProdutoId);
                achados2.Add(new VendaBusca(v.DataHora, v.Id, item.NomeProduto,
                    item.Tamanho == Tamanhos.SemTamanho ? "" : item.Tamanho, item.Quantidade, valor));
            }
        }

        var variacoes = achados2.GroupBy(a => a.Tamanho)
            .Select(g => new VariacaoBusca(g.Key, g.Sum(a => a.Quantidade), g.Sum(a => a.ValorCentavos)))
            .OrderByDescending(x => x.Unidades).ThenBy(x => x.Tamanho, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new ResultadoBusca(texto!.Trim(), produtosVistos.Count, achados2.Sum(a => a.Quantidade), achados2.Sum(a => a.ValorCentavos),
            variacoes, achados2);
    }

    private static bool TemTodos(string nome, string[] tokens) => tokens.Length > 0 && tokens.All(nome.Contains);
}

/// <summary>Lê as vendas do banco e entrega o resumo completo e a pesquisa.</summary>
public class ResumoVendasService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public ResumoVendasService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    public async Task<(ResumoVendas.Completo Resumo, ResumoVendas.ResultadoBusca Busca)> GerarAsync(DesempenhoVendas.Intervalo periodo, string? pesquisa = null)
    {
        var anterior = DesempenhoVendas.Anterior(periodo);
        var de = anterior.Inicio.Date;
        var ate = periodo.Fim.Date.AddDays(1);
        await using var db = await _fabrica.CreateDbContextAsync();
        var vendas = await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => v.DataHora >= de && v.DataHora < ate).ToListAsync();
        var cadastro = string.IsNullOrWhiteSpace(pesquisa)
            ? new List<ResumoVendas.ProdutoCadastro>()
            : (await db.Produtos.AsNoTracking().Select(p => new { p.Id, p.Nome, p.Referencia, p.CodigoInterno, p.CodigoBarras }).ToListAsync())
                .Select(p => new ResumoVendas.ProdutoCadastro(p.Id, p.Nome, p.Referencia, p.CodigoInterno, p.CodigoBarras)).ToList();
        return (ResumoVendas.Gerar(vendas, periodo), ResumoVendas.Pesquisar(vendas, cadastro, periodo, pesquisa));
    }
}
