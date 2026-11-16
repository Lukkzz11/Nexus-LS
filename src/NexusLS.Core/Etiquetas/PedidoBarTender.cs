namespace NexusLS.Core.Etiquetas;

/// <summary>Um item que o Nexus manda imprimir no BarTender: produto, tamanho ("" = sem tamanho) e cópias.</summary>
public sealed record ItemPedidoBarTender(int ProdutoId, string Tamanho, int Quantidade);

/// <summary>
/// Combinado entre o Nexus e o Nexus BarTender para "imprimir etiquetas lá": o Nexus abre o BarTender com
/// um argumento <c>--item=ID|TAMANHO|QTD</c> por linha da fila, e o BarTender lê isso e abre direto na Fila.
/// Fica no Core para os dois lados usarem exatamente o mesmo formato.
/// </summary>
public static class PedidoBarTender
{
    public const string NomeExe = "NexusBarTender.exe";
    private const string Prefixo = "--item=";
    private const int MaximoPorItem = 9999;

    /// <summary>Argumentos de linha de comando para os itens (itens inválidos são ignorados).</summary>
    public static IReadOnlyList<string> Montar(IEnumerable<ItemPedidoBarTender> itens) => itens
        .Where(i => i.ProdutoId > 0 && i.Quantidade > 0)
        .Select(i => $"{Prefixo}{i.ProdutoId}|{LimparTamanho(i.Tamanho)}|{Math.Min(i.Quantidade, MaximoPorItem)}")
        .ToList();

    /// <summary>Itens pedidos nos argumentos; lista vazia quando não há nenhum pedido válido.</summary>
    public static IReadOnlyList<ItemPedidoBarTender> Ler(IEnumerable<string>? args)
    {
        var itens = new List<ItemPedidoBarTender>();
        foreach (var arg in args ?? Array.Empty<string>())
        {
            if (arg is null || !arg.StartsWith(Prefixo, StringComparison.OrdinalIgnoreCase)) continue;

            var partes = arg[Prefixo.Length..].Split('|');
            if (partes.Length != 3) continue;
            if (!int.TryParse(partes[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0) continue;
            if (!int.TryParse(partes[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var qtd) || qtd <= 0) continue;

            itens.Add(new ItemPedidoBarTender(id, partes[1].Trim(), Math.Min(qtd, MaximoPorItem)));
        }
        return itens;
    }

    /// <summary>
    /// Onde está o NexusBarTender.exe: na pasta do programa que chama, na pasta de instalação do Nexus
    /// (%LOCALAPPDATA%\Programs\Nexus) e na pasta de publicação do desenvolvedor. Nulo se não achar.
    /// </summary>
    public static string? LocalizarExe(string? pastaDoPrograma = null, string? pastaLocalAppData = null)
    {
        var baseDir = pastaDoPrograma ?? AppContext.BaseDirectory;
        var local = pastaLocalAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var candidatos = new List<string>
        {
            Path.Combine(baseDir, NomeExe),
            Path.Combine(local, "Programs", "Nexus", NomeExe),
            Path.Combine(local, "Programs", "NexusLS", NomeExe)
        };
        if (pastaDoPrograma is null && pastaLocalAppData is null)
            candidatos.Add(@"C:\NexusLS\publicar\" + NomeExe);

        return candidatos.FirstOrDefault(File.Exists);
    }

    private static string LimparTamanho(string? tamanho) =>
        (tamanho ?? "").Replace("|", " ").Trim();
}
