using System.Globalization;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Relatório do dia em PDF: o mesmo resumo que a tela Caixa mostra (vendas, ticket médio, descontos e o quanto
/// entrou em cada forma de pagamento) mais a lista das vendas. Usa o <see cref="NotaPdf"/>, que já gera o PDF em
/// texto de largura fixa sem biblioteca externa. Venda cancelada aparece marcada e não entra em nenhum total.
/// </summary>
public static class RelatorioDiaPdf
{
    /// <summary>Colunas do papel (Courier 10 no A4 cabe com folga até 78 letras por linha).</summary>
    private const int Largura = 78;

    public static string NomeDoArquivo(DateTime dia) => $"relatorio-do-dia-{dia:yyyy-MM-dd}.pdf";

    public static byte[] Gerar(IEnumerable<Venda> vendas, DateTime dia, string nomeLoja, DateTime? emitidoEm = null) =>
        NotaPdf.Gerar(Linhas(vendas, dia, nomeLoja, emitidoEm));

    public static List<string> Linhas(IEnumerable<Venda> vendas, DateTime dia, string nomeLoja, DateTime? emitidoEm = null)
    {
        var lista = vendas.ToList();
        var validas = lista.Where(v => !v.Cancelada).ToList();
        var canceladas = lista.Count - validas.Count;
        var total = validas.Sum(v => v.TotalCentavos);
        var descontos = validas.Sum(v => v.DescontoCentavos);
        var pecas = validas.Sum(v => v.Itens.Sum(i => i.Quantidade));
        var traco = new string('-', Largura);
        var cultura = CultureInfo.GetCultureInfo("pt-BR");

        var linhas = new List<string>
        {
            Centralizar((nomeLoja ?? "").Trim().ToUpper(cultura), Largura),
            Centralizar("RELATÓRIO DO DIA", Largura),
            Centralizar(dia.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Largura),
            "",
            DuasPontas("Vendas:", validas.Count.ToString(CultureInfo.InvariantCulture)),
            DuasPontas("Peças vendidas:", pecas.ToString(CultureInfo.InvariantCulture)),
            DuasPontas("Total vendido:", Dinheiro.Formatar(total)),
            DuasPontas("Ticket médio:", Dinheiro.Formatar(validas.Count == 0 ? 0 : total / validas.Count)),
            DuasPontas("Descontos:", Dinheiro.Formatar(descontos))
        };
        if (canceladas > 0)
            linhas.Add(DuasPontas("Canceladas (não somadas):", canceladas.ToString(CultureInfo.InvariantCulture)));

        linhas.Add(traco);
        linhas.Add("POR FORMA DE PAGAMENTO");
        foreach (var forma in RelatorioService.ResumoPorForma(lista))
            linhas.Add(DuasPontas($"{SelecaoPagamento.Nome(forma.Forma)}  ({forma.Vendas})", Dinheiro.Formatar(forma.TotalCentavos)));

        linhas.Add(traco);
        linhas.Add("VENDAS DO DIA");
        foreach (var venda in lista.OrderBy(v => v.DataHora).ThenBy(v => v.Id))
            linhas.Add(Colunas(venda));

        linhas.Add(traco);
        var emitido = emitidoEm ?? DateTime.Now;
        linhas.Add($"Emitido pelo Nexus LS em {emitido.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}");
        return linhas;
    }

    /// <summary>Uma venda numa linha: número, hora, operador, forma de pagamento e total.</summary>
    private static string Colunas(Venda venda)
    {
        var operador = (venda.Operador ?? "").Trim();
        if (operador.Length > 16) operador = operador[..16];
        var forma = SelecaoPagamento.Nome(venda.FormaPagamento);
        if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1) forma += $" {venda.Parcelas}x";

        var valor = Dinheiro.Formatar(venda.TotalCentavos);
        var espaco = Largura - valor.Length - 1;
        var corpo = $"#{venda.Id,-4} {venda.DataHora:HH:mm}  {operador}  {forma}";
        if (venda.Cancelada) corpo += "  (CANCELADA)";
        if (corpo.Length > espaco) corpo = corpo[..espaco];
        return corpo.PadRight(espaco) + " " + valor;
    }

    private static string DuasPontas(string esquerda, string direita)
    {
        var espaco = Largura - direita.Length;
        if (esquerda.Length > espaco - 1) esquerda = esquerda[..Math.Max(0, espaco - 1)];
        return esquerda.PadRight(espaco) + direita;
    }

    private static string Centralizar(string texto, int largura)
    {
        var limpo = texto.Length > largura ? texto[..largura] : texto;
        return new string(' ', (largura - limpo.Length) / 2) + limpo;
    }
}
