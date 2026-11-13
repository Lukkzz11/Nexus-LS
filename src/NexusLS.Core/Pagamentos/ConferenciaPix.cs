using System.Globalization;
using System.Text;

namespace NexusLS.Core.Pagamentos;

/// <summary>Conferência dos Pix: totais por situação e os pontos que merecem olhar (pago sem venda ligada, pendente antigo).</summary>
public static class ConferenciaPix
{
    public record Resumo(int Quantidade, long TotalCentavos, int Aprovados, long AprovadosCentavos, int Estornados, long EstornadosCentavos,
        int Pendentes, int Expirados, IReadOnlyList<CobrancaPixRegistro> PagosSemVenda, IReadOnlyList<CobrancaPixRegistro> PendentesAntigos)
    {
        public long LiquidoCentavos => AprovadosCentavos;
    }

    public static Resumo Resumir(IEnumerable<CobrancaPixRegistro> cobrancas, DateTime agora)
    {
        var l = cobrancas.ToList();
        var aprov = l.Where(c => c.Status == StatusPix.Aprovado).ToList();
        var estor = l.Where(c => c.Status == StatusPix.Estornado).ToList();
        return new Resumo(l.Count, l.Sum(c => c.ValorCentavos), aprov.Count, aprov.Sum(c => c.ValorCentavos), estor.Count, estor.Sum(c => c.ValorCentavos),
            l.Count(c => c.Status == StatusPix.Pendente), l.Count(c => c.Status == StatusPix.Expirado),
            aprov.Where(c => c.VendaId is null).ToList(),
            l.Where(c => c.Status == StatusPix.Pendente && agora - c.CriadaEm > TimeSpan.FromHours(2)).ToList());
    }

    /// <summary>CSV para o Excel brasileiro (ponto e vírgula, vírgula decimal).</summary>
    public static string Csv(IEnumerable<CobrancaPixRegistro> cobrancas)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        var sb = new StringBuilder("Data;Gateway;Id no gateway;Valor;Situação;Venda\r\n");
        foreach (var c in cobrancas.OrderBy(c => c.CriadaEm))
            sb.Append(c.CriadaEm.ToString("dd/MM/yyyy HH:mm", br)).Append(';').Append(c.Gateway).Append(';').Append(c.IdExterno).Append(';')
              .Append((c.ValorCentavos / 100m).ToString("0.00", br)).Append(';').Append(c.Status).Append(';')
              .Append(c.VendaId?.ToString(CultureInfo.InvariantCulture) ?? "").Append("\r\n");
        return sb.ToString();
    }
}
