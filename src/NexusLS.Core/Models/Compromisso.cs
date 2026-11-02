namespace NexusLS.Core.Models;

/// <summary>Um compromisso da agenda da loja (entrega, cobrança, reunião, lembrete). Fica só neste computador.</summary>
public class Compromisso
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descricao { get; set; }

    /// <summary>Data e hora de início. "Dia inteiro" guarda 00:00 e <see cref="DiaInteiro"/> verdadeiro.</summary>
    public DateTime Inicio { get; set; }
    public DateTime? Fim { get; set; }
    public bool DiaInteiro { get; set; }

    public bool Concluido { get; set; }
    public DateTime CriadoEm { get; set; }

    /// <summary>Id do evento correspondente no Google Agenda (nulo = nunca enviado). Evita duplicar ao reenviar.</summary>
    public string? GoogleEventoId { get; set; }
}
