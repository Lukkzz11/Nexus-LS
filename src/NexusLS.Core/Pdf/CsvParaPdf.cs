using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using NexusLS.Core.Models;

namespace NexusLS.Core.Pdf;

/// <summary>
/// Transforma qualquer planilha CSV do Nexus (separador ";", aspas, 1ª linha = cabeçalho) num relatório PDF no padrão visual
/// do programa. É por aqui que todos os relatórios que já exportavam CSV ganham o PDF sem precisar de código próprio.
/// </summary>
public static partial class CsvParaPdf
{
    /// <summary>Quem entrega os dados da loja para o cabeçalho (o programa preenche ao abrir). Nulo = loja genérica.</summary>
    public static Func<ConfiguracaoLoja>? ProvedorLoja { get; set; }

    /// <summary>Chamado com (nome do arquivo, bytes) depois que um relatório vira PDF: o programa guarda uma cópia na gaveta.</summary>
    public static Action<string, byte[]>? AoGerar { get; set; }

    public static byte[] Gerar(string csv, string titulo, string? periodo, ConfiguracaoLoja loja, DateTime? emitidoEm = null)
    {
        var linhas = Ler(csv);
        var doc = new PdfDocumento(titulo, DocumentosPdf.Empresa(loja), emitidoEm, periodo);
        if (linhas.Count == 0)
        {
            doc.Paragrafo("Sem dados para este relatório.");
            return doc.Gerar();
        }

        var cabecalho = linhas[0];
        var corpo = linhas.Skip(1).ToList();
        var n = Math.Max(1, linhas.Max(l => l.Length));
        var colunas = new List<ColunaPdf>();
        for (var c = 0; c < n; c++)
        {
            var titulo1 = c < cabecalho.Length ? cabecalho[c] : "";
            var valores = corpo.Select(l => c < l.Length ? l[c] : "").Where(v => v.Length > 0).Take(60).ToList();
            var numerica = valores.Count > 0 && valores.All(v => Numerico().IsMatch(v));
            var tam = Math.Max(titulo1.Length, valores.Count == 0 ? 0 : valores.Max(v => v.Length));
            colunas.Add(new ColunaPdf(titulo1, Math.Clamp(tam, 4, 36), numerica ? AlinhamentoPdf.Direita : AlinhamentoPdf.Esquerda));
        }
        var tabela = corpo.Select(l => Enumerable.Range(0, n).Select(c => c < l.Length ? l[c] : "").ToArray());
        doc.Secao("Detalhamento").Tabela(colunas, tabela);
        doc.Assinaturas(new[] { "Responsável", "Conferido por" });
        return doc.Gerar();
    }

    /// <summary>Título legível a partir do nome do arquivo ("mais-vendidos-2026-11-01_a_2026-11-19.pdf" → "Mais vendidos").</summary>
    public static (string Titulo, string? Periodo) TituloDoArquivo(string caminho)
    {
        var nome = Path.GetFileNameWithoutExtension(caminho);
        string? periodo = null;
        var m = Periodo().Match(nome);
        if (m.Success)
        {
            var a = DateTime.ParseExact(m.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var b = DateTime.ParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            periodo = a == b ? a.ToString("dd/MM/yyyy") : $"{a:dd/MM/yyyy} a {b:dd/MM/yyyy}";
            nome = nome[..m.Index];
        }
        else
        {
            var d = Data().Match(nome);
            if (d.Success)
            {
                periodo = DateTime.ParseExact(d.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("dd/MM/yyyy");
                nome = nome[..d.Index];
            }
        }
        var limpo = nome.Trim('-', '_', ' ').Replace('-', ' ').Replace('_', ' ').Trim();
        if (limpo.Length == 0) limpo = "relatório";
        return ("Relatório — " + char.ToUpper(limpo[0], CultureInfo.GetCultureInfo("pt-BR")) + limpo[1..], periodo);
    }

    /// <summary>Lê o CSV (separador ";", campos entre aspas com "" para aspas, quebras dentro de aspas).</summary>
    public static List<string[]> Ler(string csv)
    {
        var linhas = new List<string[]>();
        var campos = new List<string>();
        var atual = new StringBuilder();
        var aspas = false;
        var texto = csv.TrimStart('﻿');
        void FimCampo() { campos.Add(Limpar(atual.ToString())); atual.Clear(); }
        void FimLinha()
        {
            FimCampo();
            if (campos.Any(c => c.Length > 0)) linhas.Add(campos.ToArray());
            campos = new List<string>();
        }
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (aspas)
            {
                if (c == '"' && i + 1 < texto.Length && texto[i + 1] == '"') { atual.Append('"'); i++; }
                else if (c == '"') aspas = false;
                else atual.Append(c);
            }
            else if (c == '"') aspas = true;
            else if (c == ';') FimCampo();
            else if (c == '\r') { }
            else if (c == '\n') FimLinha();
            else atual.Append(c);
        }
        if (atual.Length > 0 || campos.Count > 0) FimLinha();
        return linhas;
    }

    /// <summary>O exportador põe um apóstrofo antes de texto que começa com = + - @ (proteção do Excel): no PDF ele sai.</summary>
    private static string Limpar(string valor) =>
        valor.Length > 1 && valor[0] == '\'' && "=+-@".Contains(valor[1]) ? valor[1..] : valor.Trim();

    [GeneratedRegex(@"^[R$\s\d.,%+\-]+$")]
    private static partial Regex Numerico();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})_a_(\d{4}-\d{2}-\d{2})")]
    private static partial Regex Periodo();

    [GeneratedRegex(@"(\d{4}-\d{2}-\d{2})")]
    private static partial Regex Data();
}
