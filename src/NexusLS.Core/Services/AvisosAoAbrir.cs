using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Monta as frases dos avisos que o Nexus mostra ao abrir. Só texto: não consulta nem grava nada.</summary>
public static class AvisosAoAbrir
{
    /// <summary>Frase sobre as contas a pagar (vencidas e a vencer), ou nulo se não há nada.</summary>
    public static string? Contas(IEnumerable<Lancamento> contas, int dias, DateTime hoje)
    {
        var abertas = contas.Where(l => l.Tipo == TipoLancamento.Despesa && l.PagoEm is null && !l.Cancelado).ToList();
        var vencidas = abertas.Where(l => l.Vencimento.Date < hoje.Date).ToList();
        var aVencer = abertas.Where(l => l.Vencimento.Date >= hoje.Date && l.Vencimento.Date <= hoje.Date.AddDays(dias)).ToList();
        var partes = new List<string>();
        if (vencidas.Count > 0) partes.Add($"{vencidas.Count} vencida(s), {Dinheiro.Formatar(vencidas.Sum(l => l.ValorCentavos))}");
        if (aVencer.Count > 0)
            partes.Add($"{aVencer.Count} vencendo {(dias == 0 ? "hoje" : $"em até {dias} dia(s)")}, {Dinheiro.Formatar(aVencer.Sum(l => l.ValorCentavos))}");
        return partes.Count == 0 ? null : "Contas a pagar: " + string.Join("; ", partes) + ".";
    }

    /// <summary>Frase sobre o crediário vencido, ou nulo se ninguém está devendo vencido.</summary>
    public static string? Crediario(int clientesComVencida, long vencidaCentavos) =>
        clientesComVencida <= 0 || vencidaCentavos <= 0 ? null
            : $"Crediário vencido: {clientesComVencida} cliente(s), {Dinheiro.Formatar(vencidaCentavos)}.";

    /// <summary>Frase sobre o backup se o último é mais velho que <paramref name="dias"/> (ou nunca houve), senão nulo.</summary>
    public static string? Backup(DateTime? ultimo, DateTime agora, int dias)
    {
        if (ultimo is null) return "Nenhum backup foi feito ainda.";
        var passados = (int)Math.Floor((agora - ultimo.Value).TotalDays);
        return passados >= dias ? $"O último backup foi há {passados} dia(s)." : null;
    }

    /// <summary>Dias à frente (além de hoje) que o aviso de aniversariantes olha: a semana toda.</summary>
    public const int DiasAvisoAniversario = 7;

    /// <summary>Um aniversário próximo: quem, em que dia cai este ano e quantos anos faz.</summary>
    public record Aniversariante(string Nome, DateTime Data, int DiasAte, int Idade);

    /// <summary>
    /// Funcionários ativos que fazem aniversário de hoje até hoje + <paramref name="dias"/>, do mais próximo ao mais distante.
    /// Quem nasceu em 29/02 comemora em 28/02 nos anos não bissextos. Passa a virada do ano (31/12 olha para janeiro).
    /// </summary>
    public static List<Aniversariante> AniversariantesProximos(IEnumerable<Funcionario> funcionarios, DateTime hoje, int dias = DiasAvisoAniversario)
    {
        var resultado = new List<Aniversariante>();
        foreach (var f in funcionarios)
        {
            if (!f.Ativo || f.DataNascimento is not DateTime nasc || nasc.Date > hoje.Date) continue;
            foreach (var ano in new[] { hoje.Year, hoje.Year + 1 })
            {
                var dia = nasc.Month == 2 && nasc.Day == 29 && !DateTime.IsLeapYear(ano) ? 28 : nasc.Day;
                var data = new DateTime(ano, nasc.Month, dia);
                var ate = (data - hoje.Date).Days;
                if (ate < 0) continue;
                if (ate <= dias) resultado.Add(new Aniversariante(f.Nome, data, ate, ano - nasc.Year));
                break;
            }
        }
        return resultado.OrderBy(a => a.DiasAte).ThenBy(a => a.Nome, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Linhas do aviso de aniversariantes da semana (a primeira é o título), ou lista vazia se não há ninguém.</summary>
    public static List<string> Aniversariantes(IEnumerable<Funcionario> funcionarios, DateTime hoje, int dias = DiasAvisoAniversario)
    {
        var lista = AniversariantesProximos(funcionarios, hoje, dias);
        if (lista.Count == 0) return new();
        var linhas = new List<string> { lista.Any(a => a.DiasAte == 0) ? "Aniversariantes: tem gente fazendo anos hoje!" : "Aniversariantes da semana:" };
        foreach (var a in lista)
        {
            var quando = a.DiasAte == 0 ? "hoje" : a.DiasAte == 1 ? "amanhã" : $"{a.Data:dd/MM}";
            linhas.Add($"    • {a.Nome}, {quando} ({a.Idade} anos)");
        }
        return linhas;
    }
}
