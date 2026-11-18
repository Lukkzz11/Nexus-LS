using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.Movel;

/// <summary>
/// Aba "Vendas" do celular: resumo do dia/semana/mes e pesquisa por referencia/produto.
/// Reaproveita <see cref="ResumoVendasService"/> (mesma regra do PDV). Somente leitura e sem dado
/// pessoal: nao sai nome de cliente, vendedor, custo nem lucro. Fica atras do mesmo PIN de /api.
/// </summary>
public static class VendasMovel
{
    public const int MaximoTexto = 60;
    public const int MaximoVendasNaBusca = 30;

    /// <summary>Periodos aceitos: hoje, semana (ultimos 7 dias) e mes (do dia 1 ate hoje).</summary>
    public static bool TentarResolverPeriodo(string? nome, DateTime hoje, out DesempenhoVendas.Intervalo periodo)
    {
        switch ((nome ?? "").Trim().ToLowerInvariant())
        {
            case "hoje":
                periodo = DesempenhoVendas.Resolver(DesempenhoVendas.TipoPeriodo.Hoje, hoje);
                return true;
            case "semana":
                periodo = DesempenhoVendas.Resolver(DesempenhoVendas.TipoPeriodo.Ultimos7Dias, hoje);
                return true;
            case "mes":
                periodo = DesempenhoVendas.Resolver(DesempenhoVendas.TipoPeriodo.MesAtual, hoje);
                return true;
            default:
                periodo = default!;
                return false;
        }
    }

    public static string NomeDaForma(FormaPagamento forma) => forma switch
    {
        FormaPagamento.Dinheiro => "Dinheiro",
        FormaPagamento.Pix => "Pix",
        FormaPagamento.CartaoDebito => "Cartão de débito",
        FormaPagamento.CartaoCredito => "Cartão de crédito",
        FormaPagamento.Cheque => "Cheque",
        FormaPagamento.APrazo => "A prazo",
        FormaPagamento.ValeTroca => "Vale-troca",
        _ => forma.ToString()
    };

    private static IResult Erro(string mensagem) =>
        Results.Json(new { erro = mensagem }, statusCode: StatusCodes.Status400BadRequest);

    private static object Comparativo(ResumoVendas.Comparativo c) =>
        new { atual = c.Atual, anterior = c.Anterior, variacaoPct = c.VariacaoPct };

    private static object Intervalo(DesempenhoVendas.Intervalo i) =>
        new { inicio = i.Inicio.ToString("yyyy-MM-dd"), fim = i.Fim.ToString("yyyy-MM-dd"), dias = i.Dias };

    public static void Mapear(WebApplication app)
    {
        app.MapGet("/api/vendas/resumo", async (string? periodo, ResumoVendasService servico) =>
        {
            if (!TentarResolverPeriodo(periodo, DateTime.Now, out var intervalo))
                return Erro("Período inválido. Use hoje, semana ou mes.");

            var (r, _) = await servico.GerarAsync(intervalo);

            return Results.Ok(new
            {
                periodo = Intervalo(r.Periodo),
                anterior = Intervalo(r.Anterior),
                totalCentavos = Comparativo(r.Total),
                vendas = Comparativo(r.Vendas),
                pecas = Comparativo(r.Pecas),
                ticketMedioCentavos = Comparativo(r.TicketMedio),
                canceladas = r.Canceladas,
                porForma = r.PorForma.Select(f => new
                {
                    forma = NomeDaForma(f.Forma),
                    valorCentavos = f.ValorCentavos,
                    vendas = f.Vendas
                }),
                maisVendidos = r.MaisVendidosPorQuantidade.Take(5).Select(p => new
                {
                    nome = p.Nome,
                    pecas = p.Pecas,
                    valorCentavos = p.ValorCentavos
                })
            });
        });

        app.MapGet("/api/vendas/busca", async (string? periodo, string? texto, ResumoVendasService servico) =>
        {
            if (!TentarResolverPeriodo(periodo, DateTime.Now, out var intervalo))
                return Erro("Período inválido. Use hoje, semana ou mes.");

            var termo = (texto ?? "").Trim();
            if (termo.Length < 2 || termo.Length > MaximoTexto)
                return Erro("Digite pelo menos 2 letras ou números da referência ou do produto.");

            var (_, b) = await servico.GerarAsync(intervalo, termo);

            return Results.Ok(new
            {
                periodo = Intervalo(intervalo),
                texto = termo,
                achou = b.Achou,
                produtosEncontrados = b.ProdutosEncontrados,
                unidades = b.Unidades,
                valorCentavos = b.ValorCentavos,
                variacoes = b.Variacoes.Select(v => new
                {
                    tamanho = v.Tamanho,
                    unidades = v.Unidades,
                    valorCentavos = v.ValorCentavos
                }),
                vendas = b.Vendas.OrderByDescending(v => v.DataHora).Take(MaximoVendasNaBusca).Select(v => new
                {
                    dataHora = v.DataHora.ToString("yyyy-MM-dd'T'HH:mm"),
                    produto = v.Produto,
                    tamanho = v.Tamanho,
                    quantidade = v.Quantidade,
                    valorCentavos = v.ValorCentavos
                })
            });
        });
    }
}
