using System.Globalization;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Um pedaço de uma linha do cupom, em negrito ou não.</summary>
public record SegmentoCupom(string Texto, bool Negrito);

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

        // Cabeçalho da loja, na mesma ordem do ERP PDV Life: nome, razão social, endereço, cidade, CNPJ, IE, telefone, e-mail, mensagem
        linhas.AddRange(Centralizar(loja.NomeLoja.ToUpper(new CultureInfo("pt-BR")), largura));
        if (loja.CupomMostrarRazaoSocial && !string.IsNullOrWhiteSpace(loja.RazaoSocial)) linhas.AddRange(Centralizar(loja.RazaoSocial, largura));
        var enderecoCompleto = EnderecoCompleto(loja);
        if (loja.CupomMostrarEndereco && enderecoCompleto.Length > 0) linhas.AddRange(Centralizar(enderecoCompleto, largura));
        if (loja.CupomMostrarCidade && !string.IsNullOrWhiteSpace(loja.CidadeUf)) linhas.AddRange(Centralizar(loja.CidadeUf, largura));
        if (loja.CupomMostrarDocumento && !string.IsNullOrWhiteSpace(loja.Documento)) linhas.AddRange(Centralizar($"CNPJ/CPF: {loja.Documento}", largura));
        if (loja.CupomMostrarInscricao && !string.IsNullOrWhiteSpace(loja.InscricaoEstadual)) linhas.AddRange(Centralizar($"IE: {loja.InscricaoEstadual}", largura));
        if (loja.CupomMostrarTelefone && !string.IsNullOrWhiteSpace(loja.Telefone)) linhas.AddRange(Centralizar($"WhatsApp / Tel: {loja.Telefone}", largura));
        if (loja.CupomMostrarEmail && !string.IsNullOrWhiteSpace(loja.Email)) linhas.AddRange(Centralizar(loja.Email, largura));
        if (!string.IsNullOrWhiteSpace(loja.CupomMensagemCabecalho))
            foreach (var parte in Paragrafos(loja.CupomMensagemCabecalho))
                linhas.AddRange(Centralizar(parte, largura));

        linhas.AddRange(Centralizar("CUPOM NÃO FISCAL", largura));
        if (venda.Cancelada) linhas.AddRange(Centralizar("*** VENDA CANCELADA ***", largura));
        linhas.Add(traco);

        // Dados da venda
        linhas.Add($"Venda / Pedido: #{venda.Id}");
        linhas.Add($"Data/Hora: {venda.DataHora.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)}");
        if (loja.CupomMostrarOperador && !string.IsNullOrWhiteSpace(venda.Operador)) linhas.AddRange(Quebrar($"Operador: {venda.Operador}", largura));
        if (loja.CupomMostrarCliente && venda.Cliente is not null) linhas.AddRange(Quebrar($"Cliente: {venda.Cliente.Nome}", largura));
        if (venda.TabelaPreco == TabelaPreco.Atacado) linhas.Add("Tabela: Atacado");
        linhas.Add(traco);

        // Itens: nome (e tamanho) numa linha, "qtd x unitário ........ total" na seguinte
        linhas.Add("ITENS DA VENDA");
        foreach (var item in venda.Itens)
        {
            var nome = item.Tamanho == Tamanhos.SemTamanho ? item.NomeProduto : $"{item.NomeProduto} ({item.Tamanho})";
            linhas.AddRange(Quebrar(nome, largura));
            linhas.Add(DuasPontas($"{item.Quantidade}x {Reais(item.PrecoUnitarioCentavos)}", Reais(item.TotalCentavos), largura));
        }
        linhas.Add(traco);

        // Totais
        linhas.Add(DuasPontas("Subtotal:", Reais(venda.SubtotalCentavos), largura));
        if (venda.DescontoCentavos > 0)
            linhas.Add(DuasPontas("Desconto:", "- " + Reais(venda.DescontoCentavos), largura));
        if (venda.AcrescimoCentavos > 0)
            linhas.Add(DuasPontas("Acréscimo crédito:", "+" + Reais(venda.AcrescimoCentavos), largura));
        linhas.Add(DuasPontas("TOTAL A PAGAR:", Reais(venda.TotalCentavos), largura));

        // Pagamento
        if (loja.CupomMostrarPagamento)
        {
            linhas.Add(traco);
            var forma = SelecaoPagamento.Nome(venda.FormaPagamento).ToUpper(new CultureInfo("pt-BR"));
            if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1)
                forma += $" {venda.Parcelas}x";
            linhas.AddRange(Quebrar($"Forma de Pagamento: {forma}", largura));
            if (venda.FormaPagamento == FormaPagamento.Dinheiro && venda.ValorRecebidoCentavos is long recebido && venda.TrocoCentavos > 0)
            {
                linhas.Add(DuasPontas("Recebido:", Reais(recebido), largura));
                linhas.Add(DuasPontas("TROCO:", Reais(venda.TrocoCentavos), largura));
            }
            if (venda.FormaPagamento == FormaPagamento.CartaoCredito && venda.Parcelas > 1)
                linhas.Add(DuasPontas($"{venda.Parcelas}x de", Reais(PrimeiraParcela(venda.TotalCentavos, venda.Parcelas)), largura));
        }

        // Rodapé
        // Como no ERP: sempre há um traço e uma mensagem de agradecimento (a da loja, ou a padrão)
        linhas.Add(traco);
        foreach (var parte in Paragrafos(string.IsNullOrWhiteSpace(loja.MensagemCupom) ? MensagemPadrao : loja.MensagemCupom))
            linhas.AddRange(Centralizar(parte, largura));
        if (loja.CupomMostrarPix && !string.IsNullOrWhiteSpace(loja.ChavePix))
            linhas.AddRange(Centralizar($"Pix: {loja.ChavePix}", largura));
        if (loja.CupomMostrarAssinatura)
            linhas.AddRange(Centralizar("Impresso pelo Nexus LS", largura));

        for (var i = 0; i < Math.Clamp(loja.CupomLinhasFinais, 0, 15); i++) linhas.Add("");
        return string.Join("\n", linhas);
    }

    private static readonly string[] RotulosEmNegrito =
    {
        "Venda / Pedido:", "Data/Hora:", "Operador:", "Cliente:", "Tabela:", "Forma de Pagamento:", "Recebido:", "Subtotal:"
    };

    /// <summary>
    /// O mesmo texto de <see cref="Montar"/>, dividido em pedaços com e sem negrito (nome da loja, CUPOM NÃO FISCAL,
    /// rótulos, ITENS DA VENDA, TOTAL e TROCO). Só o peso da letra muda, então as colunas continuam alinhadas na prévia
    /// e na impressora (fonte de largura fixa).
    /// </summary>
    public static List<List<SegmentoCupom>> Estilizar(string texto)
    {
        var resultado = new List<List<SegmentoCupom>>();
        var jaAchouNome = false;
        foreach (var linha in texto.Split('\n'))
        {
            var corte = linha.TrimStart();
            var segmentos = new List<SegmentoCupom>();
            if (!jaAchouNome && corte.Length > 0)
            {
                jaAchouNome = true;
                segmentos.Add(new SegmentoCupom(linha, true));
            }
            else if (corte is "CUPOM NÃO FISCAL" or "ITENS DA VENDA" || corte.StartsWith("***", StringComparison.Ordinal)
                     || corte.StartsWith("TOTAL A PAGAR:", StringComparison.Ordinal) || corte.StartsWith("TROCO:", StringComparison.Ordinal))
            {
                segmentos.Add(new SegmentoCupom(linha, true));
            }
            else if (RotulosEmNegrito.FirstOrDefault(r => linha.StartsWith(r, StringComparison.Ordinal)) is { } rotulo)
            {
                segmentos.Add(new SegmentoCupom(rotulo, true));
                if (linha.Length > rotulo.Length) segmentos.Add(new SegmentoCupom(linha[rotulo.Length..], false));
            }
            else
            {
                segmentos.Add(new SegmentoCupom(linha, false));
            }
            resultado.Add(segmentos);
        }
        return resultado;
    }

    /// <summary>Mensagem de rodapé quando a loja não escreve a sua (a mesma do ERP PDV Life).</summary>
    public const string MensagemPadrao = "OBRIGADO PELA PREFERÊNCIA! VOLTE SEMPRE!";

    /// <summary>Endereço numa linha só, como no ERP: "Logradouro, número - Bairro".</summary>
    public static string EnderecoCompleto(ConfiguracaoLoja loja)
    {
        var rua = (loja.Endereco ?? "").Trim();
        var numero = (loja.EnderecoNumero ?? "").Trim();
        var bairro = (loja.Bairro ?? "").Trim();
        var texto = numero.Length > 0 ? (rua.Length > 0 ? $"{rua}, {numero}" : numero) : rua;
        if (bairro.Length > 0) texto = texto.Length > 0 ? $"{texto} - {bairro}" : bairro;
        return texto;
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
        Operador = "Caixa",
        Cliente = new Cliente { Nome = "Consumidor Final" },
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

    /// <summary>Divide um texto digitado em várias linhas (Enter) nos seus parágrafos.</summary>
    private static string[] Paragrafos(string texto) => texto.ReplaceLineEndings(Environment.NewLine).Split(Environment.NewLine);

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
