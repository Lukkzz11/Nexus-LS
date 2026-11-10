namespace NexusLS.Core.Etiquetas;

/// <summary>
/// Campo de dados ZPL (^FD) com escape hexadecimal: sem isso, um "^" ou "~" no código de barras
/// seria lido pela impressora como começo de comando e quebraria a etiqueta.
/// </summary>
public static class CampoZpl
{
    /// <summary>Monta ^FH^FD...^FS escapando ^, ~ e o próprio indicador de hexadecimal (_).</summary>
    public static string Montar(string? texto) =>
        "^FH^FD" + (texto ?? "")
            .Replace("_", "_5F")
            .Replace("^", "_5E")
            .Replace("~", "_7E") + "^FS";
}
