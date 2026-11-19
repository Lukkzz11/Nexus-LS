using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace NexusLS.Core.Pdf;

/// <summary>Quem emite o documento: sai no cabeçalho de todas as páginas.</summary>
public sealed record EmpresaPdf(string Nome, string? Linha1 = null, string? Linha2 = null, byte[]? LogoJpeg = null);

public enum AlinhamentoPdf { Esquerda, Direita, Centro }

public sealed record ColunaPdf(string Titulo, double Peso, AlinhamentoPdf Alinhamento = AlinhamentoPdf.Esquerda);

/// <summary>
/// Gerador de PDF no padrão visual único do Nexus (A4, cabeçalho da empresa, título do documento, tabelas, quadro de
/// totais, campos de assinatura e rodapé com "Página x de y"). Sem biblioteca externa: escreve o PDF na mão, com as
/// fontes padrão Helvetica (acentos do português em WinAnsi). Todo relatório e comprovante do programa passa por aqui,
/// por isso todos têm a mesma cara.
/// </summary>
public sealed class PdfDocumento
{
    private const double Largura = 595.28, Altura = 841.89, Margem = 42, AlturaCabecalho = 74, AlturaRodape = 34;
    private const double Corpo = 9.5, Entrelinha = 13;

    private readonly string _titulo;
    private readonly string? _subtitulo;
    private readonly EmpresaPdf _empresa;
    private readonly DateTime _emitidoEm;
    private readonly List<StringBuilder> _paginas = new();
    private double _y; // distância do topo da página

    private (int W, int H)? _logo;

    public PdfDocumento(string titulo, EmpresaPdf empresa, DateTime? emitidoEm = null, string? subtitulo = null)
    {
        _titulo = titulo;
        _subtitulo = subtitulo;
        _empresa = empresa;
        _emitidoEm = emitidoEm ?? DateTime.Now;
        if (empresa.LogoJpeg is { Length: > 0 } && TentarLerJpeg(empresa.LogoJpeg, out var w, out var h)) _logo = (w, h);
        NovaPagina();
    }

    private double LimiteInferior => Altura - Margem - AlturaRodape;
    private double LarguraUtil => Largura - 2 * Margem;
    private StringBuilder Atual => _paginas[^1];

    // ---------------------------------------------------------------- blocos de conteúdo

    /// <summary>Título de seção (faixa cinza com texto em negrito).</summary>
    public PdfDocumento Secao(string texto)
    {
        Garantir(30);
        _y += 6;
        Atual.Append("0.93 g\n").Append(Ret(Margem, _y, LarguraUtil, 17)).Append(" re f\n0 g\n");
        Texto(Margem + 6, _y + 12, texto.ToUpperInvariant(), true, 9);
        _y += 17 + 6;
        return this;
    }

    /// <summary>Parágrafo com quebra de linha automática.</summary>
    public PdfDocumento Paragrafo(string texto, bool negrito = false, double tamanho = Corpo, AlinhamentoPdf alinhamento = AlinhamentoPdf.Esquerda)
    {
        var linhas = Quebrar(texto, LarguraUtil, negrito, tamanho);
        var altura = tamanho * 1.35;
        foreach (var l in linhas)
        {
            Garantir(altura);
            var x = alinhamento switch
            {
                AlinhamentoPdf.Direita => Largura - Margem - Medir(l, negrito, tamanho),
                AlinhamentoPdf.Centro => Margem + (LarguraUtil - Medir(l, negrito, tamanho)) / 2,
                _ => Margem
            };
            Texto(x, _y + tamanho, l, negrito, tamanho);
            _y += altura;
        }
        _y += 3;
        return this;
    }

    public PdfDocumento Espaco(double pontos)
    {
        _y += pontos;
        return this;
    }

    /// <summary>Pares "rótulo: valor" em colunas (dados do cliente, da venda, do período...).</summary>
    public PdfDocumento Campos(IReadOnlyList<(string Rotulo, string Valor)> pares, int colunas = 2)
    {
        colunas = Math.Clamp(colunas, 1, 3);
        var larg = LarguraUtil / colunas;
        for (var i = 0; i < pares.Count; i += colunas)
        {
            var linha = pares.Skip(i).Take(colunas).ToList();
            var alturas = linha.Select(p => Quebrar(p.Valor, larg - 8, false, Corpo).Count).ToList();
            var altura = 11 + alturas.Max() * 12.5 + 3;
            Garantir(altura);
            for (var c = 0; c < linha.Count; c++)
            {
                var x = Margem + c * larg;
                Texto(x, _y + 8, linha[c].Rotulo, false, 7.5, cinza: true);
                var y = _y + 20;
                foreach (var l in Quebrar(linha[c].Valor, larg - 8, false, Corpo)) { Texto(x, y, l, false, Corpo); y += 12.5; }
            }
            _y += altura;
        }
        return this;
    }

    /// <summary>Tabela com cabeçalho que se repete a cada página e linha de total opcional.</summary>
    public PdfDocumento Tabela(IReadOnlyList<ColunaPdf> colunas, IEnumerable<string[]> linhas, string[]? total = null)
    {
        var soma = colunas.Sum(c => c.Peso);
        var larguras = colunas.Select(c => c.Peso / soma * LarguraUtil).ToArray();

        void Cabecalho()
        {
            Garantir(22);
            Atual.Append("0.85 g\n").Append(Ret(Margem, _y, LarguraUtil, 17)).Append(" re f\n0 g\n");
            var x = Margem;
            for (var i = 0; i < colunas.Count; i++)
            {
                TextoNaCelula(x, _y + 12, larguras[i], colunas[i].Titulo, true, 8.5, colunas[i].Alinhamento);
                x += larguras[i];
            }
            _y += 17;
        }

        void Linha(string[] valores, bool negrito, bool linhaSuperior)
        {
            var quebras = new List<string>[colunas.Count];
            var maximo = 1;
            for (var i = 0; i < colunas.Count; i++)
            {
                quebras[i] = Quebrar(i < valores.Length ? valores[i] : "", larguras[i] - 8, negrito, 9);
                maximo = Math.Max(maximo, quebras[i].Count);
            }
            var altura = maximo * 11.5 + 6;
            if (_y + altura > LimiteInferior) { NovaPagina(); Cabecalho(); }
            if (linhaSuperior) Atual.Append("0.2 G 0.8 w\n").Append(Num(Margem)).Append(' ').Append(Num(Altura - _y)).Append(" m ")
                .Append(Num(Margem + LarguraUtil)).Append(' ').Append(Num(Altura - _y)).Append(" l S\n");
            var x = Margem;
            for (var i = 0; i < colunas.Count; i++)
            {
                var y = _y + 11;
                foreach (var l in quebras[i]) { TextoNaCelula(x, y, larguras[i], l, negrito, 9, colunas[i].Alinhamento); y += 11.5; }
                x += larguras[i];
            }
            _y += altura;
            if (!linhaSuperior) Atual.Append("0.88 G 0.5 w\n").Append(Num(Margem)).Append(' ').Append(Num(Altura - _y)).Append(" m ")
                .Append(Num(Margem + LarguraUtil)).Append(' ').Append(Num(Altura - _y)).Append(" l S\n");
        }

        Cabecalho();
        var vazio = true;
        foreach (var l in linhas) { Linha(l, false, false); vazio = false; }
        if (vazio) { Garantir(20); Texto(Margem + 6, _y + 13, "Nenhum registro no período.", false, 9, cinza: true); _y += 20; }
        if (total is not null) Linha(total, true, true);
        _y += 6;
        return this;
    }

    /// <summary>Quadro de totais alinhado à direita. O último par vai em destaque (ex.: TOTAL).</summary>
    public PdfDocumento Resumo(IReadOnlyList<(string Rotulo, string Valor)> pares, bool destacarUltimo = true)
    {
        const double larg = 230;
        var altura = pares.Count * 15 + 8;
        Garantir(altura);
        var x = Largura - Margem - larg;
        Atual.Append("0.2 G 0.8 w\n").Append(Ret(x, _y, larg, altura)).Append(" re S\n");
        var y = _y + 14;
        for (var i = 0; i < pares.Count; i++)
        {
            var destaque = destacarUltimo && i == pares.Count - 1;
            Texto(x + 8, y, pares[i].Rotulo, destaque, destaque ? 10.5 : 9);
            Texto(x + larg - 8 - Medir(pares[i].Valor, destaque, destaque ? 10.5 : 9), y, pares[i].Valor, destaque, destaque ? 10.5 : 9);
            y += 15;
        }
        _y += altura + 8;
        return this;
    }

    /// <summary>
    /// Campos de assinatura lado a lado ("Assinatura do cliente", "Responsável"...). Nunca fica partido entre páginas.
    /// O texto de declaração (opcional) sai logo acima.
    /// </summary>
    public PdfDocumento Assinaturas(IReadOnlyList<string> rotulos, string? declaracao = null, bool comData = true)
    {
        var altura = 70 + (declaracao is null ? 0 : Quebrar(declaracao, LarguraUtil, false, Corpo).Count * 13 + 6);
        Garantir(altura);
        if (declaracao is not null) Paragrafo(declaracao);
        _y += 34;
        var larg = LarguraUtil / rotulos.Count;
        for (var i = 0; i < rotulos.Count; i++)
        {
            var x = Margem + i * larg + 10;
            var fim = Margem + (i + 1) * larg - 10;
            Atual.Append("0.2 G 0.7 w\n").Append(Num(x)).Append(' ').Append(Num(Altura - _y)).Append(" m ")
                .Append(Num(fim)).Append(' ').Append(Num(Altura - _y)).Append(" l S\n");
            TextoNaCelula(x, _y + 11, fim - x, rotulos[i], false, 8.5, AlinhamentoPdf.Centro);
            if (comData) TextoNaCelula(x, _y + 23, fim - x, "Data: ____/____/________", false, 8, AlinhamentoPdf.Centro, cinza: true);
        }
        _y += 34;
        return this;
    }

    // ---------------------------------------------------------------- geração do arquivo

    public byte[] Gerar()
    {
        var total = _paginas.Count;
        var nomesFontes = "<< /Font << /F1 3 0 R /F2 4 0 R >>" + (_logo is null ? "" : " /XObject << /Im1 5 0 R >>") + " >>";
        var objetos = new List<byte[]>();
        const int primeiraPagina = 7; // 1 catálogo, 2 páginas, 3-4 fontes, 5 logo, 6 info
        objetos.Add(Ascii("<< /Type /Catalog /Pages 2 0 R >>"));
        objetos.Add(Ascii("<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, total).Select(i => $"{primeiraPagina + 2 * i} 0 R")) + $"] /Count {total} >>"));
        objetos.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"));
        objetos.Add(Ascii("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>"));
        objetos.Add(_logo is { } l && _empresa.LogoJpeg is { } jpg
            ? Concat(Ascii($"<< /Type /XObject /Subtype /Image /Width {l.W} /Height {l.H} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpg.Length} >>\nstream\n"), jpg, Ascii("\nendstream"))
            : Ascii("<< >>"));

        // Info (objeto 6) e depois as páginas a partir do 7.
        var info = $"<< /Title ({Escapar(_titulo)}) /Producer (Nexus LS) /CreationDate (D:{_emitidoEm:yyyyMMddHHmmss}) >>";
        objetos.Add(Ascii(info));

        for (var p = 0; p < total; p++)
        {
            var conteudo = new StringBuilder(_paginas[p].ToString());
            DesenharRodape(conteudo, p + 1, total);
            var bytes = Comprimir(Encoding.Latin1.GetBytes(conteudo.ToString()));
            objetos.Add(Ascii(string.Create(CultureInfo.InvariantCulture,
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {Largura} {Altura}] /Resources {nomesFontes} /Contents {primeiraPagina + 2 * p + 1} 0 R >>")));
            objetos.Add(Concat(Ascii($"<< /Length {bytes.Length} /Filter /FlateDecode >>\nstream\n"), bytes, Ascii("\nendstream")));
        }

        using var saida = new MemoryStream();
        void Escrever(byte[] b) => saida.Write(b, 0, b.Length);
        Escrever(Ascii("%PDF-1.4\n"));
        var posicoes = new List<long>();
        for (var i = 0; i < objetos.Count; i++)
        {
            posicoes.Add(saida.Position);
            Escrever(Ascii($"{i + 1} 0 obj\n"));
            Escrever(objetos[i]);
            Escrever(Ascii("\nendobj\n"));
        }
        var xref = saida.Position;
        Escrever(Ascii($"xref\n0 {objetos.Count + 1}\n0000000000 65535 f \n"));
        foreach (var pos in posicoes) Escrever(Ascii($"{pos:D10} 00000 n \n"));
        Escrever(Ascii($"trailer\n<< /Size {objetos.Count + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xref}\n%%EOF\n"));
        return saida.ToArray();
    }

    // ---------------------------------------------------------------- páginas, cabeçalho e rodapé

    private void NovaPagina()
    {
        _paginas.Add(new StringBuilder());
        DesenharCabecalho();
    }

    private void Garantir(double altura)
    {
        if (_y + altura > LimiteInferior) NovaPagina();
    }

    private void DesenharCabecalho()
    {
        var topo = Margem - 6;
        var x = Margem;
        if (_logo is { } l)
        {
            var alt = 36.0;
            var larg = Math.Min(alt * l.W / l.H, 110);
            Atual.Append("q ").Append(Num(larg)).Append(" 0 0 ").Append(Num(alt)).Append(' ').Append(Num(x)).Append(' ')
                .Append(Num(Altura - topo - alt)).Append(" cm /Im1 Do Q\n");
            x += larg + 10;
        }
        Texto(x, topo + 13, _empresa.Nome, true, 14);
        var y = topo + 25;
        foreach (var linha in new[] { _empresa.Linha1, _empresa.Linha2 })
            if (!string.IsNullOrWhiteSpace(linha)) { Texto(x, y, linha!, false, 8, cinza: true); y += 10; }

        var tituloX = Largura - Margem - Medir(_titulo, true, 13);
        Texto(tituloX, topo + 13, _titulo, true, 13);
        if (!string.IsNullOrWhiteSpace(_subtitulo))
            Texto(Largura - Margem - Medir(_subtitulo!, false, 8.5), topo + 25, _subtitulo!, false, 8.5, cinza: true);
        var emitido = "Emitido em " + _emitidoEm.ToString("dd/MM/yyyy 'às' HH:mm", CultureInfo.InvariantCulture);
        Texto(Largura - Margem - Medir(emitido, false, 8), topo + 36, emitido, false, 8, cinza: true);

        Atual.Append("0.15 G 1.2 w\n").Append(Num(Margem)).Append(' ').Append(Num(Altura - (topo + 48))).Append(" m ")
            .Append(Num(Largura - Margem)).Append(' ').Append(Num(Altura - (topo + 48))).Append(" l S\n");
        _y = AlturaCabecalho + Margem - 22;
    }

    private void DesenharRodape(StringBuilder s, int pagina, int total)
    {
        var yLinha = Altura - Margem - AlturaRodape + 12;
        s.Append("0.8 G 0.6 w\n").Append(Num(Margem)).Append(' ').Append(Num(Altura - yLinha)).Append(" m ")
            .Append(Num(Largura - Margem)).Append(' ').Append(Num(Altura - yLinha)).Append(" l S\n");
        var esquerda = "Documento gerado pelo Nexus LS";
        var direita = $"Página {pagina} de {total}";
        Escrever(s, Margem, yLinha + 12, esquerda, false, 7.5, true);
        Escrever(s, Largura - Margem - Medir(direita, false, 7.5), yLinha + 12, direita, false, 7.5, true);
    }

    // ---------------------------------------------------------------- texto e medidas

    private void Texto(double x, double yDoTopo, string texto, bool negrito, double tamanho, bool cinza = false) =>
        Escrever(Atual, x, yDoTopo, texto, negrito, tamanho, cinza);

    private static void Escrever(StringBuilder s, double x, double yDoTopo, string texto, bool negrito, double tamanho, bool cinza)
    {
        s.Append(cinza ? "0.4 g " : "0 g ").Append("BT /").Append(negrito ? "F2 " : "F1 ").Append(Num(tamanho)).Append(" Tf ")
            .Append(Num(x)).Append(' ').Append(Num(Altura - yDoTopo)).Append(" Td (").Append(Escapar(texto)).Append(") Tj ET\n");
    }

    private void TextoNaCelula(double x, double y, double largura, string texto, bool negrito, double tamanho, AlinhamentoPdf alinhamento, bool cinza = false)
    {
        var w = Medir(texto, negrito, tamanho);
        var px = alinhamento switch
        {
            AlinhamentoPdf.Direita => x + largura - 4 - w,
            AlinhamentoPdf.Centro => x + (largura - w) / 2,
            _ => x + 4
        };
        Texto(px, y, texto, negrito, tamanho, cinza);
    }

    private static List<string> Quebrar(string texto, double larguraMax, bool negrito, double tamanho)
    {
        var resultado = new List<string>();
        foreach (var paragrafo in (texto ?? "").ReplaceLineEndings("\n").Split('\n'))
        {
            var atual = "";
            foreach (var palavra in paragrafo.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var tentativa = atual.Length == 0 ? palavra : atual + " " + palavra;
                if (Medir(tentativa, negrito, tamanho) <= larguraMax || atual.Length == 0)
                {
                    atual = tentativa;
                    while (Medir(atual, negrito, tamanho) > larguraMax && atual.Length > 1) // palavra maior que a coluna: corta
                    {
                        var corte = atual.Length - 1;
                        while (corte > 1 && Medir(atual[..corte], negrito, tamanho) > larguraMax) corte--;
                        resultado.Add(atual[..corte]);
                        atual = atual[corte..];
                    }
                }
                else { resultado.Add(atual); atual = palavra; }
            }
            resultado.Add(atual);
        }
        return resultado;
    }

    public static double Medir(string texto, bool negrito, double tamanho)
    {
        var tabela = negrito ? LarguraNegrito : LarguraNormal;
        double soma = 0;
        foreach (var c in texto)
        {
            var b = Base(c);
            soma += b is >= (char)32 and <= (char)126 ? tabela[b - 32] : 556;
        }
        return soma * tamanho / 1000.0;
    }

    /// <summary>Letra acentuada mede como a letra sem acento.</summary>
    private static char Base(char c)
    {
        if (c < 128) return c;
        var d = c.ToString().Normalize(NormalizationForm.FormD);
        return d.Length > 0 && d[0] < 128 ? d[0] : '?';
    }

    private static readonly Dictionary<char, char> Windows1252 = new()
    {
        ['–'] = '\u0096', ['—'] = '\u0097', ['‘'] = '\u0091', ['’'] = '\u0092',
        ['“'] = '\u0093', ['”'] = '\u0094', ['•'] = '\u0095', ['€'] = '\u0080', ['…'] = '\u0085'
    };

    private static string Escapar(string linha)
    {
        var sb = new StringBuilder();
        foreach (var c in linha)
        {
            if (c is '\\' or '(' or ')') sb.Append('\\').Append(c);
            else if (Windows1252.TryGetValue(c, out var b)) sb.Append(b);
            else if (c < 32) sb.Append(' ');
            else if (c > 255) sb.Append('?');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- utilidades

    private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static string Ret(double x, double yTopo, double w, double h) => $"{Num(x)} {Num(Altura - yTopo - h)} {Num(w)} {Num(h)}";
    private static byte[] Ascii(string s) => Encoding.Latin1.GetBytes(s);
    private static byte[] Concat(params byte[][] partes) => partes.SelectMany(p => p).ToArray();

    private static byte[] Comprimir(byte[] dados)
    {
        using var ms = new MemoryStream();
        using (var z = new ZLibStream(ms, CompressionLevel.Optimal, leaveOpen: true)) z.Write(dados, 0, dados.Length);
        return ms.ToArray();
    }

    /// <summary>Lê a largura e a altura de um JPEG (marcador SOF). Só aceita JPEG de 3 componentes (colorido).</summary>
    private static bool TentarLerJpeg(byte[] d, out int w, out int h)
    {
        w = h = 0;
        if (d.Length < 4 || d[0] != 0xFF || d[1] != 0xD8) return false;
        var i = 2;
        while (i + 9 < d.Length)
        {
            if (d[i] != 0xFF) { i++; continue; }
            var m = d[i + 1];
            if (m is 0xC0 or 0xC1 or 0xC2)
            {
                h = d[i + 5] << 8 | d[i + 6];
                w = d[i + 7] << 8 | d[i + 8];
                return d[i + 9] == 3 && w > 0 && h > 0;
            }
            if (m == 0xD8 || (m >= 0xD0 && m <= 0xD7) || m == 0x01) { i += 2; continue; }
            i += 2 + (d[i + 2] << 8 | d[i + 3]);
        }
        return false;
    }

    // Larguras da Helvetica (AFM) para os caracteres 32..126, em milésimos de em.
    private static readonly int[] LarguraNormal =
    {
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584
    };

    private static readonly int[] LarguraNegrito =
    {
        278, 333, 474, 556, 556, 889, 722, 238, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 333, 333, 584, 584, 584, 611,
        975, 722, 722, 722, 722, 667, 611, 778, 722, 278, 556, 722, 611, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 333, 278, 333, 584, 556,
        333, 556, 611, 556, 611, 556, 333, 611, 611, 278, 278, 556, 278, 889, 611, 611,
        611, 611, 389, 556, 333, 611, 556, 778, 556, 556, 500, 389, 280, 389, 584
    };
}
