using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace NexusLS.Tests.Infra;

/// <summary>Lê o texto de um PDF do Nexus nos testes: descomprime os fluxos e junta o que está em (...) Tj.</summary>
public static class PdfTexto
{
    public static string Extrair(byte[] pdf)
    {
        var bruto = Encoding.Latin1.GetString(pdf);
        var sb = new StringBuilder();
        foreach (Match m in Regex.Matches(bruto, @"stream\n(.*?)\nendstream", RegexOptions.Singleline))
        {
            var dados = Encoding.Latin1.GetBytes(m.Groups[1].Value);
            try
            {
                using var z = new ZLibStream(new MemoryStream(dados), CompressionMode.Decompress);
                using var saida = new MemoryStream();
                z.CopyTo(saida);
                sb.Append(Encoding.Latin1.GetString(saida.ToArray())).Append('\n');
            }
            catch (InvalidDataException) { sb.Append(m.Groups[1].Value).Append('\n'); }
        }
        var conteudo = sb.ToString();
        var texto = new StringBuilder();
        foreach (Match t in Regex.Matches(conteudo, @"\(((?:\\.|[^\\)])*)\) Tj"))
            texto.Append(Regex.Replace(t.Groups[1].Value, @"\\(.)", "$1")).Append('\n');
        return texto.ToString();
    }
}
