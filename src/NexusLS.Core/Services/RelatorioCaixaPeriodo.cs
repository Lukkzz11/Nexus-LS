using System.Globalization;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um turno de caixa dentro do relatório, com o que se vendeu, os movimentos e a última conferência.</summary>
public record TurnoNoRelatorio(
    int TurnoId, DateTime Abertura, DateTime? Fechamento, string? Operador, long TrocoInicialCentavos,
    int VendasValidas, int VendasCanceladas, long VendasCentavos,
    long SangriasCentavos, long ReforcosCentavos, long DespesasCentavos, long EntradasAvulsasCentavos,
    FechamentoCaixa? Conferencia)
{
    public bool Aberto => Fechamento is null;
}

/// <summary>Relatório do Caixa de um período (dia inicial e final incluídos). Vendas canceladas nunca entram nos totais.</summary>
public record RelatorioCaixaPeriodo(
    DateTime De, DateTime Ate, DateTime EmitidoEm,
    IReadOnlyList<TurnoNoRelatorio> Turnos,
    int VendasQuantidade, long VendasCentavos,
    IReadOnlyList<ResumoForma> VendasPorForma,
    int VendasCanceladasIgnoradas,
    long SangriasCentavos, long ReforcosCentavos, long DespesasCentavos, long EntradasAvulsasCentavos,
    IReadOnlyList<MovimentoCaixa> Movimentos,
    long DiferencaDinheiroCentavos, long DiferencaCartaoCentavos, long DiferencaPixCentavos,
    IReadOnlyList<string> Avisos)
{
    public long DiferencaTotalCentavos => DiferencaDinheiroCentavos + DiferencaCartaoCentavos + DiferencaPixCentavos;
}

/// <summary>
/// Monta o relatório do Caixa por período (turnos, vendas por forma de pagamento, sangrias, reforços, despesas e as
/// diferenças da conferência). Só lê: não grava nada. Recebe o banco e o relógio, então é testável sem janela.
/// </summary>
public class RelatorioCaixaPeriodoService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public RelatorioCaixaPeriodoService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>Texto de erro do período (ou nulo se está bom): data final antes da inicial, no futuro ou mais de 1 ano.</summary>
    public string? ValidarPeriodo(DateTime de, DateTime ate)
    {
        if (ate.Date < de.Date) return "A data inicial não pode ser depois da data final.";
        if (ate.Date > _agora().Date) return "A data final não pode ser no futuro.";
        if (de.Date.AddDays(366) < ate.Date) return "Escolha um período de até 1 ano.";
        return null;
    }

    public async Task<RelatorioCaixaPeriodo> GerarAsync(DateTime de, DateTime ate)
    {
        var erro = ValidarPeriodo(de, ate);
        if (erro is not null) throw new RegraDeNegocioException(erro);
        var inicio = de.Date;
        var fim = ate.Date.AddDays(1);

        var vendas = await new RelatorioService(_fabrica).VendasPorPeriodoAsync(inicio, ate.Date);
        var caixa = new CaixaService(_fabrica, _agora);

        await using var db = await _fabrica.CreateDbContextAsync();
        var turnos = (await db.TurnosCaixa.AsNoTracking().ToListAsync())
            .Where(t => t.DataAbertura >= inicio && t.DataAbertura < fim)
            .OrderBy(t => t.DataAbertura).ThenBy(t => t.Id).ToList();
        var ids = turnos.Select(t => t.Id).ToList();
        var fechamentos = (await db.FechamentosCaixa.AsNoTracking().Where(f => ids.Contains(f.TurnoId)).ToListAsync())
            .GroupBy(f => f.TurnoId).ToDictionary(g => g.Key, g => g.OrderByDescending(f => f.Versao).First());

        var linhas = new List<TurnoNoRelatorio>();
        foreach (var t in turnos)
        {
            var r = await caixa.ResumoAsync(t.Id);
            linhas.Add(new TurnoNoRelatorio(t.Id, t.DataAbertura, t.DataFechamento, t.Operador, t.TrocoInicialCentavos,
                r.VendasValidas, r.VendasCanceladas,
                r.VendasDinheiroCentavos + r.VendasCartaoCentavos + r.VendasPixCentavos + r.VendasChequeCentavos + r.VendasAPrazoCentavos,
                r.SangriasCentavos, r.ReforcosCentavos, r.DespesasCentavos, r.EntradasAvulsasCentavos,
                fechamentos.GetValueOrDefault(t.Id)));
        }

        // Movimentos pelo horário em que aconteceram (não pelo turno), para o período fechar com o relatório de vendas.
        var movimentos = (await db.MovimentosCaixa.AsNoTracking().ToListAsync())
            .Where(m => m.DataHora >= inicio && m.DataHora < fim)
            .OrderBy(m => m.DataHora).ThenBy(m => m.Id).ToList();
        long Mov(TipoMovimentoCaixa tipo) => movimentos.Where(m => m.Tipo == tipo).Sum(m => m.ValorCentavos);

        var conferidos = linhas.Where(l => l.Conferencia is not null).Select(l => l.Conferencia!).ToList();
        var avisos = new List<string>();
        foreach (var l in linhas.Where(l => l.Aberto))
            avisos.Add($"O caixa aberto em {l.Abertura:dd/MM/yyyy HH:mm} ainda não foi fechado: sem conferência.");
        foreach (var l in linhas.Where(l => !l.Aberto && l.Conferencia is null))
            avisos.Add($"O caixa de {l.Abertura:dd/MM/yyyy HH:mm} está fechado sem conferência registrada.");
        if (vendas.QuantidadeCanceladas > 0)
            avisos.Add($"{vendas.QuantidadeCanceladas} venda(s) cancelada(s) no período não entram nos totais.");

        return new RelatorioCaixaPeriodo(inicio, ate.Date, _agora(), linhas,
            vendas.QuantidadeVendas, vendas.TotalCentavos, vendas.PorForma, vendas.QuantidadeCanceladas,
            Mov(TipoMovimentoCaixa.Sangria), Mov(TipoMovimentoCaixa.Reforco), Mov(TipoMovimentoCaixa.Despesa), Mov(TipoMovimentoCaixa.EntradaAvulsa),
            movimentos,
            conferidos.Sum(f => f.DiferencaDinheiroCentavos), conferidos.Sum(f => f.DiferencaCartaoCentavos), conferidos.Sum(f => f.DiferencaPixCentavos),
            avisos);
    }
}

/// <summary>Transforma o relatório em linhas de texto (colunas alinhadas, fonte de largura fixa) e em PDF.</summary>
public static class RelatorioCaixaPdf
{
    private const int Largura = 84;

    public static string NomeDoArquivo(RelatorioCaixaPeriodo r) =>
        $"caixa_{r.De:yyyy-MM-dd}_a_{r.Ate:yyyy-MM-dd}.pdf";

    public static byte[] Gerar(RelatorioCaixaPeriodo r, string nomeLoja) => NotaPdf.Gerar(Linhas(r, nomeLoja));

    private static string R(long centavos) => Dinheiro.Formatar(centavos).Replace(' ', ' ');

    private static string Corta(string? texto, int n) =>
        (texto ?? "").Length <= n ? (texto ?? "") : texto![..(n - 1)] + ".";

    /// <summary>Rótulo à esquerda e valor colado na margem direita.</summary>
    private static string Par(string rotulo, string valor)
    {
        var espaco = Largura - valor.Length - 1;
        return Corta(rotulo, espaco).PadRight(espaco + 1) + valor;
    }

    public static List<string> Linhas(RelatorioCaixaPeriodo r, string nomeLoja)
    {
        var l = new List<string>();
        void Secao(string titulo) { l.Add(""); l.Add(titulo.ToUpper(CultureInfo.CurrentCulture)); l.Add(new string('-', Largura)); }

        l.Add(Corta(nomeLoja, Largura));
        l.Add($"RELATÓRIO DO CAIXA - {r.De:dd/MM/yyyy} a {r.Ate:dd/MM/yyyy}");
        l.Add($"Emitido em {r.EmitidoEm:dd/MM/yyyy HH:mm}");

        Secao("Resumo do período");
        l.Add(Par("Vendas (sem canceladas)", $"{r.VendasQuantidade} venda(s)  {R(r.VendasCentavos)}"));
        l.Add(Par("Sangrias (retiradas)", R(r.SangriasCentavos)));
        l.Add(Par("Reforços", R(r.ReforcosCentavos)));
        l.Add(Par("Despesas pagas pelo caixa", R(r.DespesasCentavos)));
        l.Add(Par("Entradas avulsas", R(r.EntradasAvulsasCentavos)));
        l.Add(Par("Diferença da conferência (total)", R(r.DiferencaTotalCentavos)));

        Secao("Vendas por forma de pagamento");
        if (r.VendasPorForma.Count == 0) l.Add("Sem vendas no período.");
        foreach (var f in r.VendasPorForma)
            l.Add(Par(SelecaoPagamento.Nome(f.Forma), $"{f.Vendas} venda(s)  {R(f.TotalCentavos)}"));
        if (r.VendasPorForma.Count > 0) l.Add(Par("Total vendido", R(r.VendasCentavos)));

        Secao("Turnos de caixa");
        if (r.Turnos.Count == 0) l.Add("Nenhum caixa aberto no período.");
        foreach (var t in r.Turnos)
        {
            var situacao = t.Aberto ? "ABERTO" : $"fechado {t.Fechamento:dd/MM HH:mm}";
            l.Add($"Caixa nº {t.TurnoId} - aberto {t.Abertura:dd/MM/yyyy HH:mm} - {situacao}");
            l.Add($"  Operador: {t.Operador ?? "-"}   Troco inicial: {R(t.TrocoInicialCentavos)}");
            l.Add($"  Vendas: {t.VendasValidas} ({R(t.VendasCentavos)})   Sangrias: {R(t.SangriasCentavos)}   Reforços: {R(t.ReforcosCentavos)}");
            l.Add($"  Despesas: {R(t.DespesasCentavos)}   Entradas avulsas: {R(t.EntradasAvulsasCentavos)}");
            if (t.Conferencia is { } c)
            {
                l.Add($"  Conferência (versão {c.Versao}): dif. dinheiro {R(c.DiferencaDinheiroCentavos)}, cartão {R(c.DiferencaCartaoCentavos)}, Pix {R(c.DiferencaPixCentavos)}");
                if (!string.IsNullOrWhiteSpace(c.Observacao)) l.Add($"  Obs.: {Corta(c.Observacao, Largura - 8)}");
            }
            else l.Add("  Conferência: não registrada");
            l.Add("");
        }
        if (r.Turnos.Count > 0) l.RemoveAt(l.Count - 1);

        Secao("Diferenças do fechamento (conferido - esperado)");
        l.Add(Par("Dinheiro", R(r.DiferencaDinheiroCentavos)));
        l.Add(Par("Cartão", R(r.DiferencaCartaoCentavos)));
        l.Add(Par("Pix", R(r.DiferencaPixCentavos)));
        l.Add(Par("Total", R(r.DiferencaTotalCentavos)));

        Secao("Movimentações do caixa");
        if (r.Movimentos.Count == 0) l.Add("Sem movimentações no período.");
        foreach (var m in r.Movimentos)
        {
            var tipo = m.Tipo switch
            {
                TipoMovimentoCaixa.Sangria => "Sangria",
                TipoMovimentoCaixa.Reforco => "Reforço",
                TipoMovimentoCaixa.Despesa => "Despesa",
                _ => "Entrada avulsa"
            };
            var sinal = m.Tipo is TipoMovimentoCaixa.Sangria or TipoMovimentoCaixa.Despesa ? "-" : "+";
            l.Add(Par($"{m.DataHora:dd/MM HH:mm}  {tipo,-14} {Corta(m.Descricao, 30)}", $"{sinal} {R(m.ValorCentavos)}"));
        }

        if (r.Avisos.Count > 0)
        {
            Secao("Avisos");
            foreach (var a in r.Avisos) l.Add("* " + Corta(a, Largura - 2));
        }

        l.Add("");
        l.Add("Relatório gerencial calculado pelos registros do sistema. Não é documento fiscal.");
        return l;
    }
}
