using System.Globalization;
using System.Text;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Gera arquivos CSV que o Excel brasileiro abre direto: separador ponto e vírgula, números com vírgula
/// decimal (sem símbolo de moeda nem ponto de milhar), datas dd/MM/aaaa e acentos preservados (UTF-8 com BOM).
/// </summary>
public static class ExportadorCsv
{
    private const char Separador = ';';

    /// <summary>Uma linha por venda (inclusive as canceladas, marcadas na coluna "Situação").</summary>
    public static string Vendas(IEnumerable<Venda> vendas)
    {
        var csv = new StringBuilder();
        Linha(csv, "Nº", "Data", "Cliente", "Tabela", "Pagamento", "Parcelas", "Subtotal", "Desconto", "Total",
            "Recebido", "Troco", "Situação", "Operador", "Motivo do cancelamento");

        foreach (var v in vendas.OrderBy(v => v.DataHora).ThenBy(v => v.Id))
        {
            Linha(csv,
                v.Id.ToString(CultureInfo.InvariantCulture),
                Data(v.DataHora),
                v.Cliente?.Nome ?? "",
                v.TabelaPreco == TabelaPreco.Atacado ? "Atacado" : "Varejo",
                SelecaoPagamento.Nome(v.FormaPagamento),
                v.Parcelas.ToString(CultureInfo.InvariantCulture),
                Valor(v.SubtotalCentavos),
                Valor(v.DescontoCentavos),
                Valor(v.TotalCentavos),
                v.ValorRecebidoCentavos is long r ? Valor(r) : "",
                v.FormaPagamento == FormaPagamento.Dinheiro ? Valor(v.TrocoCentavos) : "",
                v.Cancelada ? "Cancelada" : "Concluída",
                v.Operador ?? "",
                v.MotivoCancelamento ?? "");
        }
        return csv.ToString();
    }

    /// <summary>Uma linha por item vendido, com o número e a data da venda repetidos (bom para filtrar e somar no Excel).</summary>
    public static string Itens(IEnumerable<Venda> vendas)
    {
        var csv = new StringBuilder();
        Linha(csv, "Venda", "Data", "Produto", "Tamanho", "Quantidade", "Unitário", "Total", "Situação");

        foreach (var v in vendas.OrderBy(v => v.DataHora).ThenBy(v => v.Id))
            foreach (var i in v.Itens)
                Linha(csv,
                    v.Id.ToString(CultureInfo.InvariantCulture),
                    Data(v.DataHora),
                    i.NomeProduto,
                    i.Tamanho == Tamanhos.SemTamanho ? "" : i.Tamanho,
                    i.Quantidade.ToString(CultureInfo.InvariantCulture),
                    Valor(i.PrecoUnitarioCentavos),
                    Valor(i.TotalCentavos),
                    v.Cancelada ? "Cancelada" : "Concluída");
        return csv.ToString();
    }

    public static string MaisVendidos(IEnumerable<ProdutoMaisVendido> ranking, bool porTamanho)
    {
        var csv = new StringBuilder();
        if (porTamanho) Linha(csv, "Posição", "Produto", "Tamanho", "Quantidade", "Faturamento", "Vendas");
        else Linha(csv, "Posição", "Produto", "Quantidade", "Faturamento", "Vendas");

        foreach (var p in ranking)
        {
            if (porTamanho)
                Linha(csv, p.Posicao.ToString(CultureInfo.InvariantCulture), p.Nome,
                    p.Tamanho == Tamanhos.SemTamanho ? "" : p.Tamanho ?? "",
                    p.Quantidade.ToString(CultureInfo.InvariantCulture), Valor(p.FaturamentoCentavos),
                    p.NumeroDeVendas.ToString(CultureInfo.InvariantCulture));
            else
                Linha(csv, p.Posicao.ToString(CultureInfo.InvariantCulture), p.Nome,
                    p.Quantidade.ToString(CultureInfo.InvariantCulture), Valor(p.FaturamentoCentavos),
                    p.NumeroDeVendas.ToString(CultureInfo.InvariantCulture));
        }
        return csv.ToString();
    }

    /// <summary>Grava em UTF-8 COM BOM: sem ele o Excel mostraria "ConcluÃ­da" no lugar de "Concluída".</summary>
    public static void Salvar(string caminho, string conteudo) =>
        File.WriteAllText(caminho, conteudo, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    private static void Linha(StringBuilder csv, params string[] campos)
    {
        csv.Append(string.Join(Separador, campos.Select(Campo))).Append("\r\n");
    }

    /// <summary>
    /// Põe aspas quando o texto tem separador, aspas ou quebra de linha. Texto que começa com = + - @ ganha
    /// um apóstrofo na frente, para o Excel não o executar como fórmula.
    /// </summary>
    private static string Campo(string? texto)
    {
        texto ??= "";
        if (texto.Length > 0 && texto[0] is '=' or '+' or '-' or '@' && !EhNumero(texto)) texto = "'" + texto;

        return texto.IndexOfAny(new[] { Separador, '"', '\r', '\n' }) >= 0
            ? "\"" + texto.Replace("\"", "\"\"") + "\""
            : texto;
    }

    /// <summary>Números negativos (ex.: "-10,00") são valores, não fórmulas: ficam como estão.</summary>
    private static bool EhNumero(string texto) =>
        decimal.TryParse(texto, NumberStyles.Number, new CultureInfo("pt-BR"), out _);

    private static string Valor(long centavos) => Dinheiro.FormatarSimples(centavos);

    private static string Data(DateTime data) => data.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
}
