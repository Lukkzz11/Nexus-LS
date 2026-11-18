using System.Globalization;
using System.Net;
using System.Text;

namespace NexusLS.Core.Services;

/// <summary>
/// Página única (HTML, sem internet nem programas extras) com o resumo do dia, feita para ler no celular.
/// Só números do dia: vendas, caixa, contas, crediário e estoque baixo. Não tem senha nem dados de clientes por nome.
/// </summary>
public static class ResumoCelular
{
    public const string NomeArquivoPadrao = "resumo-do-dia.html";

    /// <summary>Nome do arquivo: "resumo-do-dia.html" ou, com data, "resumo-AAAA-MM-DD.html".</summary>
    public static string NomeArquivo(DateTime dia, bool comData) =>
        comData ? $"resumo-{dia:yyyy-MM-dd}.html" : NomeArquivoPadrao;

    /// <summary>Comparação com ontem em texto ("+12% em relação a ontem"); nulo se ontem não teve venda.</summary>
    public static string? ComparacaoComOntem(long hoje, long ontem)
    {
        if (ontem <= 0) return null;
        var pct = (int)Math.Round((hoje - ontem) * 100.0 / ontem, MidpointRounding.AwayFromZero);
        return pct == 0 ? "igual a ontem" : (pct > 0 ? "+" : "") + pct + "% em relação a ontem";
    }

    public static string Html(ResumoInicio r, string nomeLoja, DateTime geradoEm)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"pt-BR\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>Resumo do dia</title><style>")
          .Append(":root{color-scheme:light dark;--f:#f5f6f8;--c:#fff;--t:#14171c;--s:#5f6875;--b:#e3e6ea;--p:#1a7f4b;--n:#c0392b}")
          .Append("@media(prefers-color-scheme:dark){:root{--f:#101317;--c:#1a1e24;--t:#eef0f3;--s:#9aa3af;--b:#2a3039;--p:#4cc38a;--n:#ff7b6b}}")
          .Append("body{margin:0;background:var(--f);color:var(--t);font:16px system-ui,sans-serif}")
          .Append("main{max-width:520px;margin:0 auto;padding:16px}h1{font-size:20px;margin:4px 0}")
          .Append(".s{color:var(--s);font-size:13px}.k{background:var(--c);border:1px solid var(--b);border-radius:12px;padding:14px;margin-top:12px}")
          .Append(".v{font-size:28px;font-weight:700}.l{display:flex;justify-content:space-between;gap:12px;padding:6px 0;border-top:1px solid var(--b)}")
          .Append(".bar{height:4px;background:var(--b);border-radius:2px;margin:-2px 0 4px}.bar i{display:block;height:100%;background:var(--p);border-radius:2px}")
          .Append(".l:first-of-type{border-top:0}.n{color:var(--n)}.p{color:var(--p)}h2{font-size:15px;margin:0 0 6px}")
          .Append("</style></head><body><main>");
        sb.Append("<h1>").Append(E(nomeLoja)).Append("</h1><div class=\"s\">Resumo de ")
          .Append(r.Dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)).Append(" · gerado às ").Append(geradoEm.ToString("HH:mm", CultureInfo.InvariantCulture)).Append("</div>");

        sb.Append("<div class=\"k\"><h2>Vendas de hoje</h2><div class=\"v\">").Append(Dinheiro.Formatar(r.TotalHojeCentavos)).Append("</div>")
          .Append("<div class=\"s\">").Append(r.VendasHoje).Append(" venda(s) · ticket médio ").Append(Dinheiro.Formatar(r.TicketMedioHojeCentavos))
          .Append(" · ontem ").Append(Dinheiro.Formatar(r.TotalOntemCentavos));
        if (r.PecasHoje > 0) sb.Append(" · ").Append(r.PecasHoje).Append(" peça(s)");
        if (r.CanceladasHoje > 0) sb.Append(" · ").Append(r.CanceladasHoje).Append(" cancelada(s)");
        sb.Append("</div>");
        if (ComparacaoComOntem(r.TotalHojeCentavos, r.TotalOntemCentavos) is { } cmp)
            sb.Append("<div class=\"").Append(r.TotalHojeCentavos >= r.TotalOntemCentavos ? "p" : "n").Append("\">").Append(cmp).Append("</div>");
        sb.Append("</div>");

        sb.Append("<div class=\"k\"><h2>Caixa</h2>")
          .Append(r.CaixaAberto
              ? "<span class=\"p\">Aberto" + (r.CaixaAbertoDesde is { } desde ? " desde " + desde.ToString("HH:mm", CultureInfo.InvariantCulture) : "") + "</span>"
              : "<span class=\"s\">Fechado</span>")
          .Append("</div>");

        if (r.UltimosDias.Count > 0)
        {
            sb.Append("<div class=\"k\"><h2>Últimos dias</h2>");
            var maior = r.UltimosDias.Max(x => x.TotalCentavos);
            foreach (var d in r.UltimosDias.Reverse())
                sb.Append("<div class=\"l\"><span>").Append(d.Dia.ToString("dd/MM", CultureInfo.InvariantCulture)).Append(" · ").Append(d.Vendas)
                  .Append(" venda(s)</span><b>").Append(Dinheiro.Formatar(d.TotalCentavos)).Append("</b></div>")
                  .Append("<div class=\"bar\"><i style=\"width:").Append(maior > 0 ? Math.Max(1, (int)(d.TotalCentavos * 100 / maior)) : 0).Append("%\"></i></div>");
            sb.Append("</div>");
        }

        if (r.MaisVendidosSemana.Count > 0)
        {
            sb.Append("<div class=\"k\"><h2>Mais vendidos da semana</h2>");
            foreach (var p in r.MaisVendidosSemana)
                sb.Append("<div class=\"l\"><span>").Append(E(p.Nome)).Append("</span><b>").Append(p.Quantidade).Append(" un</b></div>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"k\"><h2>Contas e crediário</h2>");
        sb.Append("<div class=\"l\"><span>Contas a pagar (7 dias)</span><b>").Append(r.ContasAPagarQuantidade).Append(" · ")
          .Append(Dinheiro.Formatar(r.ContasAPagarTotalCentavos)).Append("</b></div>");
        sb.Append("<div class=\"l\"><span>Contas atrasadas</span><b").Append(r.ContasAtrasadasCentavos > 0 ? " class=\"n\"" : "").Append('>')
          .Append(Dinheiro.Formatar(r.ContasAtrasadasCentavos)).Append("</b></div>");
        sb.Append("<div class=\"l\"><span>Crediário em aberto</span><b>").Append(r.ClientesComDivida).Append(" cliente(s) · ")
          .Append(Dinheiro.Formatar(r.DividaTotalCentavos)).Append("</b></div>");
        sb.Append("<div class=\"l\"><span>Crediário vencido</span><b").Append(r.DividaVencidaCentavos > 0 ? " class=\"n\"" : "").Append('>')
          .Append(r.ClientesComDividaVencida).Append(" · ").Append(Dinheiro.Formatar(r.DividaVencidaCentavos)).Append("</b></div></div>");

        if (r.ContasAPagar.Count > 0)
        {
            sb.Append("<div class=\"k\"><h2>Próximas contas a pagar</h2>");
            foreach (var c in r.ContasAPagar)
                sb.Append("<div class=\"l\"><span>").Append(E(c.Descricao)).Append(" · ").Append(c.Vencimento.ToString("dd/MM"))
                  .Append(c.Atrasada ? " (atrasada)" : "").Append("</span><b").Append(c.Atrasada ? " class=\"n\"" : "").Append('>')
                  .Append(Dinheiro.Formatar(c.ValorCentavos)).Append("</b></div>");
            sb.Append("</div>");
        }

        sb.Append("<div class=\"k\"><h2>Estoque baixo</h2>");
        if (r.ProdutosAbaixoDoMinimo == 0) sb.Append("<span class=\"p\">Nenhum produto abaixo do mínimo.</span>");
        else
        {
            sb.Append("<div class=\"s\">").Append(r.ProdutosAbaixoDoMinimo).Append(" produto(s) abaixo do mínimo</div>");
            foreach (var p in r.PioresEstoques)
                sb.Append("<div class=\"l\"><span>").Append(E(p.Nome)).Append("</span><b>").Append(p.Estoque).Append(" / mín. ").Append(p.Minimo).Append("</b></div>");
        }
        sb.Append("</div><p class=\"s\">Foto do momento em que foi gerado. Para atualizar, gere de novo no Nexus.</p></main></body></html>");
        return sb.ToString();
    }
}
