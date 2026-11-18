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
}
