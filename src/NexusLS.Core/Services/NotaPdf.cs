using System.Globalization;
using System.Text;

namespace NexusLS.Core.Services;

/// <summary>
/// Gera um PDF simples (texto em fonte de largura fixa, A4) a partir das linhas da nota. Sem biblioteca externa:
/// só o suficiente para guardar ou mandar a nota do cliente. Acentos do português saem em WinAnsi (Windows-1252).
/// </summary>
public static class NotaPdf
{
    private const double Largura = 595, Altura = 842, Margem = 40, Corpo = 10, Entrelinha = 13;

    public static byte[] Gerar(IEnumerable<string> linhas)
    {
        var porPagina = (int)((Altura - 2 * Margem) / Entrelinha);
        var paginas = linhas.Select(l => l.Replace("\t", "    ")).Chunk(porPagina).ToList();
        if (paginas.Count == 0) paginas.Add(new[] { "" });

        // Objetos: 1 catálogo, 2 páginas, 3 fonte, depois (página, conteúdo) para cada página.
        var objetos = new List<byte[]>();
        var ids = paginas.Select((_, i) => 4 + 2 * i).ToList();
        objetos.Add(Texto("<< /Type /Catalog /Pages 2 0 R >>"));
        objetos.Add(Texto($"<< /Type /Pages /Kids [{string.Join(" ", ids.Select(i => $"{i} 0 R"))}] /Count {paginas.Count} >>"));
        objetos.Add(Texto("<< /Type /Font /Subtype /Type1 /BaseFont /Courier /Encoding /WinAnsiEncoding >>"));
        for (var p = 0; p < paginas.Count; p++)
        {
            objetos.Add(Texto(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Largura} {Altura}] /Resources << /Font << /F1 3 0 R >> >> /Contents {ids[p] + 1} 0 R >>")));
            var fluxo = new StringBuilder();
            fluxo.Append(string.Create(CultureInfo.InvariantCulture, $"BT /F1 {Corpo} Tf {Entrelinha} TL {Margem} {Altura - Margem - Corpo} Td\n"));
            foreach (var linha in paginas[p]) fluxo.Append('(').Append(Escapar(linha)).Append(") Tj T*\n");
            fluxo.Append("ET");
            var bytes = Encoding.Latin1.GetBytes(fluxo.ToString());
            objetos.Add(Concat(Texto($"<< /Length {bytes.Length} >>\nstream\n"), bytes, Texto("\nendstream")));
        }

        using var saida = new MemoryStream();
        void Escrever(byte[] b) => saida.Write(b, 0, b.Length);
        Escrever(Texto("%PDF-1.4\n"));
        var posicoes = new List<long>();
        for (var i = 0; i < objetos.Count; i++)
        {
            posicoes.Add(saida.Position);
            Escrever(Texto($"{i + 1} 0 obj\n"));
            Escrever(objetos[i]);
            Escrever(Texto("\nendobj\n"));
        }
        var inicioXref = saida.Position;
        Escrever(Texto($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n"));
        foreach (var pos in posicoes) Escrever(Texto($"{pos:D10} 00000 n \n"));
        Escrever(Texto($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R >>\nstartxref\n{inicioXref}\n%%EOF\n"));
        return saida.ToArray();
    }

    /// <summary>Símbolos comuns que o Windows-1252 tem entre 0x80 e 0x9F (travessão, aspas curvas, marcador, euro).</summary>
    private static readonly Dictionary<char, char> Windows1252 = new()
    {
        ['\u2013'] = '\u0096', ['\u2014'] = '\u0097', ['\u2018'] = '\u0091', ['\u2019'] = '\u0092',
        ['\u201C'] = '\u0093', ['\u201D'] = '\u0094', ['\u2022'] = '\u0095', ['\u20AC'] = '\u0080', ['\u2026'] = '\u0085'
    };

    private static byte[] Texto(string s) => Encoding.Latin1.GetBytes(s);

    private static byte[] Concat(params byte[][] partes) => partes.SelectMany(p => p).ToArray();

    /// <summary>Barra e parênteses precisam de escape; o que não cabe em Windows-1252 vira "?".</summary>
    private static string Escapar(string linha)
    {
        var sb = new StringBuilder();
        foreach (var c in linha)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\').Append(c);
            else if (Windows1252.TryGetValue(c, out var byteAnsi)) sb.Append(byteAnsi);
            else if (c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
