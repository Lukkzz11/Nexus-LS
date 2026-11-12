using System.Globalization;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Código de barras de um tamanho = código da referência + número do tamanho (ref 5555, tamanho 1 → 55551).
/// O produto sem tamanho (ordem 0) usa só o código da referência.
/// </summary>
public static class CodigoBarrasTamanho
{
    public static string? Montar(string? codigoDoProduto, int ordem)
    {
        var baseCodigo = codigoDoProduto?.Trim();
        if (string.IsNullOrEmpty(baseCodigo)) return null;
        return ordem < 1 || !UsaNumeroDoTamanho(baseCodigo) ? baseCodigo : baseCodigo + ordem.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Um EAN-13 (13 dígitos, de fabricante ou interno do Nexus) identifica o produto inteiro e não leva número de tamanho:
    /// colar um dígito nele geraria um código inválido. A regra vale para os códigos curtos de referência (5555 → 55551).
    /// </summary>
    public static bool UsaNumeroDoTamanho(string? codigoDoProduto)
    {
        var c = codigoDoProduto?.Trim();
        return !string.IsNullOrEmpty(c) && !(c.Length == 13 && c.All(char.IsAsciiDigit));
    }

    /// <summary>
    /// Número oficial do tamanho dentro do produto. Usa a <see cref="ProdutoTamanho.Ordem"/> gravada no cadastro
    /// (a mesma que o PDV usa); se o produto não tiver a grade carregada, cai no número do próprio nome e, por fim,
    /// na tabela de letras. "Sem tamanho" é sempre 0.
    /// </summary>
    public static int ObterOrdem(Produto? produto, string? tamanho)
    {
        var nome = tamanho?.Trim();
        if (string.IsNullOrEmpty(nome) || string.Equals(nome, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            return 0;

        var cadastrado = produto?.Tamanhos?.FirstOrDefault(t =>
            string.Equals(t.Tamanho?.Trim(), nome, StringComparison.OrdinalIgnoreCase));
        // Se a linha existe na grade, a Ordem dela manda (inclusive 0, que é o mesmo código do produto no PDV).
        if (cadastrado is not null) return Math.Max(0, cadastrado.Ordem);

        return ObterOrdemGenerica(nome);
    }

    /// <summary>
    /// Fallback para quando a grade não está disponível (prévia do designer, produto de teste):
    /// tamanho numérico vira o próprio número; letras seguem a sequência natural PP→1, P→2, M→3, G→4...
    /// </summary>
    public static int ObterOrdemGenerica(string? tamanho)
    {
        var nome = tamanho?.Trim();
        if (string.IsNullOrEmpty(nome)) return 0;
        if (string.Equals(nome, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase)) return 0;
        if (int.TryParse(nome, out var n) && n > 0 && n <= 99) return n;

        return nome.ToUpperInvariant() switch
        {
            "PP" => 1,
            "P" => 2,
            "M" => 3,
            "G" => 4,
            "GG" => 5,
            "XG" => 6,
            "XXG" => 7,
            "G1" => 5,
            "G2" => 6,
            "G3" => 7,
            _ => 1
        };
    }

    /// <summary>
    /// Código-base do produto, na mesma prioridade em que a etiqueta imprime: EAN-13, referência,
    /// código de barras curto e, por fim, código interno (todos já sem o número do tamanho).
    /// </summary>
    public static string? BaseDoCodigo(Produto? produto)
    {
        if (produto is null) return null;

        return Ean13(produto.CodigoBarras)
            ?? (!string.IsNullOrWhiteSpace(produto.Referencia) ? produto.Referencia.Trim()
                : !string.IsNullOrWhiteSpace(produto.CodigoBarras) ? produto.CodigoBarras.Trim()
                : string.IsNullOrWhiteSpace(produto.CodigoInterno) ? null : produto.CodigoInterno.Trim());
    }

    /// <summary>
    /// Código que deve sair na etiqueta para este produto/tamanho. Preferência:
    /// EAN-13 do produto (identifica o produto inteiro, sem sufixo), depois a referência, depois o código de barras
    /// curto e, por fim, o código interno. A ordem do tamanho é sempre a cadastrada no produto.
    /// </summary>
    public static string? Resolver(Produto? produto, string? tamanho)
    {
        var baseCodigo = BaseDoCodigo(produto);
        if (string.IsNullOrWhiteSpace(baseCodigo)) return null;

        var ordem = ObterOrdem(produto, tamanho);
        var montado = Montar(baseCodigo, ordem);
        return string.IsNullOrWhiteSpace(montado) ? baseCodigo : montado;
    }

    /// <summary>Referência da etiqueta quando o modelo usa a variável "Referência" no código de barras.</summary>
    public static string? ResolverReferencia(Produto? produto, string? tamanho)
    {
        if (produto is null || string.IsNullOrWhiteSpace(produto.Referencia)) return null;
        var ordem = ObterOrdem(produto, tamanho);
        return Montar(produto.Referencia, ordem) ?? produto.Referencia.Trim();
    }

    /// <summary>
    /// Confere se o código pode ser impresso nesta largura de etiqueta. Devolve uma lista vazia quando está tudo certo;
    /// caso contrário, explica o motivo (texto sem acento, caractere não suportado ou barras finas demais).
    /// </summary>
    public static IReadOnlyList<string> Validar(string? codigo, double larguraEtiquetaMm)
    {
        var erros = new List<string>();
        if (string.IsNullOrWhiteSpace(codigo))
        {
            erros.Add("sem código de barras");
            return erros;
        }

        if (!Code128.PodeCodificar(codigo))
        {
            erros.Add("contém acento ou caractere que o Code 128 não aceita");
            return erros;
        }

        if (larguraEtiquetaMm > 0 && !LegibilidadeCodigo.Legivel(codigo, larguraEtiquetaMm))
        {
            erros.Add($"as barras ficariam com menos de {TextoBr.Numero(LegibilidadeCodigo.ModuloMinimoMm, "0.00")} mm nesta etiqueta");
        }

        return erros;
    }

    private static string? Ean13(string? codigo)
    {
        var c = codigo?.Trim();
        return c is { Length: 13 } && c.All(char.IsAsciiDigit) ? c : null;
    }
}

/// <summary>Uma referência nova montada a partir de uma já existente: os dados copiados e o próximo número livre do mesmo modelo.</summary>
public sealed record ModeloDeReferencia(NexusLS.Core.Models.Produto Base, IReadOnlyList<string> Tamanhos, string NovaReferencia);
