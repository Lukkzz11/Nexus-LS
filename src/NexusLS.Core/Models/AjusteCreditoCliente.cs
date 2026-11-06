namespace NexusLS.Core.Models;

/// <summary>
/// Um ajuste no crédito (saldo a favor) de um cliente: de quanto para quanto, o motivo, quem e quando. Nunca é editado nem
/// apagado: o histórico do crédito é a soma destes registros.
/// </summary>
public class AjusteCreditoCliente : EntidadeSincronizavel
{
    public int Id { get; set; }
    public int ClienteId { get; set; }
    public Cliente? Cliente { get; set; }
    public long SaldoAnteriorCentavos { get; set; }
    public long SaldoNovoCentavos { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Operador { get; set; }
    public DateTime CriadoEm { get; set; }

    public long DiferencaCentavos => SaldoNovoCentavos - SaldoAnteriorCentavos;
}
