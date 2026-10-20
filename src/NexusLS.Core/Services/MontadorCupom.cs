using System.Globalization;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Monta o texto do cupom NÃO FISCAL de uma venda, em colunas fixas (fonte de largura fixa), pronto para a
/// impressora. Não imprime nada e não conhece o banco nem a janela: recebe a venda e os dados da loja.
/// </summary>
public static class MontadorCupom
{
    public static string Montar(Venda venda, ConfiguracaoLoja loja)
    {
        var largura = Math.Clamp(loja.LarguraCupom, ConfiguracaoService.LarguraMinima, ConfiguracaoService.LarguraMaxima);
        var linhas = new List<string>();
        var traco = new string('-', largura);

        // Cabeçalho da loja
        linhas.AddRange(Centralizar(loja.NomeLoja, largura));
        if (!string.IsNullOrWhiteSpace(loja.Documento)) linhas.AddRange(Centralizar($"CNPJ/CPF: {loja.Documento}", largura));
        if (!string.IsNullOrWhiteSpace(loja.Endereco)) linhas.AddRange(Centralizar(loja.Endereco, largura));
        if (!string.IsNullOrWhiteSpace(loja.Telefone)) linhas.AddRange(Centralizar($"Tel: {loja.Telefone}", largura));

        linhas.Add(traco);
        linhas.AddRange(Centralizar("CUPOM NÃO FISCAL", largura));
        if (venda.Cancelada) linhas.AddRange(Centralizar("*** VENDA CANCELADA ***", largura));

        linhas.Add(DuasPontas($"Venda nº {venda.Id}", venda.DataHora.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), largura));
        if (venda.Cliente is not null) linhas.AddRange(Quebrar($"Cliente: {venda.Cliente.Nome}", largura));
        if (venda.TabelaPreco == TabelaPreco.Atacado) linhas.Add("Tabela: Atacado");
        linhas.Add(traco);

        // Itens: nome (e tamanho) numa linha, "qtd x unitário ........ total" na seguinte
        foreach (var item in venda.Itens)
        {
            var nome = item.Tamanho == Tamanhos.SemTamanho ? item.NomeProduto : $"{item.NomeProduto} ({item.Tamanho})";
            linhas.AddRange(Quebrar(nome, largura));
            linhas.Add(DuasPontas($"  {item.Quantidade} x {Reais(item.PrecoUnitarioCentavos)}", Reais(item.TotalCentavos), largura));
        }
        linhas.Add(traco);

        // Totais
        linhas.Add(DuasPontas("Subtotal", Reais(venda.SubtotalCentavos), largura));
        if (venda.DescontoCentavos > 0)
            linhas.Add(DuasPontas("Desconto", "-" + Reais(venda.DescontoCentavos), largura));
        linhas.Add(DuasPontas("TOTAL", Reais(venda.TotalCentavos), largura));
        linhas.Add(traco);

        // Pagamento
        var forma = SelecaoPagamento.Nome(venda.FormaPagamento);
        if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1)
            forma += $" {venda.Parcelas}x";
        linhas.Add(DuasPontas("Pagamento", forma, largura));
        if (venda.FormaPagamento == FormaPagamento.Dinheiro && venda.ValorRecebidoCentavos is long recebido)
        {
            linhas.Add(DuasPontas("Recebido", Reais(recebido), largura));
            linhas.Add(DuasPontas("Troco", Reais(venda.TrocoCentavos), largura));
        }
        if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1)
            linhas.Add(DuasPontas($"{venda.Parcelas}x de", Reais(PrimeiraParcela(venda.TotalCentavos, venda.Parcelas)), largura));

        // Rodapé
        if (!string.IsNullOrWhiteSpace(loja.MensagemCupom))
        {
            linhas.Add(traco);
            linhas.AddRange(Centralizar(loja.MensagemCupom, largura));
        }

        linhas.Add("");
        return string.Join("\n", linhas);
    }

    /// <summary>
    /// Lista de separação do pedido para a expedição: sem preços, com uma caixinha [ ] para marcar cada item conferido.
    /// </summary>
    public static string MontarListaSeparacao(Pedido pedido, ConfiguracaoLoja loja)
    {
        var largura = Math.Clamp(loja.LarguraCupom, ConfiguracaoService.LarguraMinima, ConfiguracaoService.LarguraMaxima);
        var linhas = new List<string>();
        var traco = new string('-', largura);

        linhas.AddRange(Centralizar(loja.NomeLoja, largura));
        linhas.Add(traco);
        linhas.AddRange(Centralizar("LISTA DE SEPARAÇÃO", largura));
        linhas.Add(DuasPontas($"Pedido {pedido.Numero}", pedido.CriadoEm.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), largura));
        linhas.Add($"Etapa: {RegrasPedido.Nome(pedido.Status)}  ({RegrasPedido.Nome(pedido.Origem)})");
        linhas.AddRange(Quebrar($"Cliente: {pedido.ClienteNome}", largura));
        if (!string.IsNullOrWhiteSpace(pedido.ClienteTelefone)) linhas.Add($"Tel: {pedido.ClienteTelefone}");
        linhas.Add(traco);

        foreach (var item in pedido.Itens)
        {
            var nome = item.Tamanho == Tamanhos.SemTamanho ? item.Nome : $"{item.Nome} ({item.Tamanho})";
            linhas.AddRange(Quebrar($"[ ] {item.Quantidade} x {nome}", largura));
        }
        linhas.Add(traco);
        linhas.Add($"Total de peças: {pedido.Itens.Sum(i => i.Quantidade)}");
        if (!string.IsNullOrWhiteSpace(pedido.Observacao))
        {
            linhas.Add(traco);
            linhas.AddRange(Quebrar($"Obs: {pedido.Observacao}", largura));
        }
        linhas.Add("");
        linhas.Add("Separado por: ____________________");
        linhas.Add("");
        return string.Join("\n", linhas);
    }

    /// <summary>Venda inventada para a prévia da tela de Configurações e para o cupom de teste.</summary>
    public static Venda VendaDeExemplo() => new()
    {
        Id = 123,
        DataHora = DateTime.Now,
        FormaPagamento = FormaPagamento.Dinheiro,
        SubtotalCentavos = 15980,
        DescontoCentavos = 1000,
        TotalCentavos = 14980,
        ValorRecebidoCentavos = 20000,
        TrocoCentavos = 5020,
        Itens =
        {
            new ItemVenda { NomeProduto = "Camiseta Básica", Tamanho = "M", PrecoUnitarioCentavos = 5990, Quantidade = 2, TotalCentavos = 11980 },
            new ItemVenda { NomeProduto = "Boné Aba Reta", Tamanho = Tamanhos.SemTamanho, PrecoUnitarioCentavos = 4000, Quantidade = 1, TotalCentavos = 4000 }
        }
    };

    /// <summary>Valor de cada parcela. A primeira leva a sobra dos centavos, para a soma fechar exatamente no total.</summary>
    private static long PrimeiraParcela(long total, int parcelas) => total / parcelas + total % parcelas;

    private static string Reais(long centavos) => Dinheiro.Formatar(centavos).Replace(' ', ' ');

    /// <summary>Texto à esquerda e texto à direita na mesma linha, com espaços no meio. Se não couber, corta o da esquerda.</summary>
    private static string DuasPontas(string esquerda, string direita, int largura)
    {
        var espaco = largura - direita.Length - 1;
        if (espaco < 1) return direita[..Math.Min(direita.Length, largura)];
        if (esquerda.Length > espaco) esquerda = esquerda[..espaco];
        return esquerda + new string(' ', largura - esquerda.Length - direita.Length) + direita;
    }

    private static IEnumerable<string> Centralizar(string texto, int largura) =>
        Quebrar(texto, largura).Select(l => new string(' ', (largura - l.Length) / 2) + l);

    /// <summary>Quebra o texto em linhas de no máximo "largura" caracteres, sem partir palavras (exceto as gigantes).</summary>
    private static List<string> Quebrar(string texto, int largura)
    {
        var resultado = new List<string>();
        var atual = "";

        foreach (var palavra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = palavra;
            while (p.Length > largura)                 // palavra maior que a linha: corta
            {
                if (atual.Length > 0) { resultado.Add(atual); atual = ""; }
                resultado.Add(p[..largura]);
                p = p[largura..];
            }

            if (atual.Length == 0) atual = p;
            else if (atual.Length + 1 + p.Length <= largura) atual += " " + p;
            else { resultado.Add(atual); atual = p; }
        }

        if (atual.Length > 0) resultado.Add(atual);
        if (resultado.Count == 0) resultado.Add("");
        return resultado;
    }
}
