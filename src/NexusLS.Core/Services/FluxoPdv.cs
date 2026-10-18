using System.Globalization;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

// Regras de cada etapa do fluxo do PDV (tamanho, quantidade, desconto, pagamento, sucesso).
// Ficam no Core, sem janela, para serem testadas sem abrir tela: a tela só desenha o estado destas classes.

public enum DirecaoGrade { Esquerda, Direita, Cima, Baixo }

/// <summary>Escolha do tamanho: uma grade de 3 colunas navegada pelas setas.</summary>
public class GradeTamanhos
{
    public const int Colunas = 3;
    private readonly List<ProdutoTamanho> _opcoes;

    /// <summary>Começa no primeiro tamanho que tem estoque (ou no primeiro, se todos estão esgotados).</summary>
    public GradeTamanhos(IEnumerable<ProdutoTamanho> opcoes)
    {
        _opcoes = opcoes.ToList();
        Indice = Math.Max(0, _opcoes.FindIndex(o => o.Estoque > 0));
    }

    public IReadOnlyList<ProdutoTamanho> Opcoes => _opcoes;
    public int Indice { get; private set; }
    public ProdutoTamanho? Selecionado => _opcoes.Count == 0 ? null : _opcoes[Indice];

    /// <summary>Vai direto para a opção de índice <paramref name="indice"/> (usado ao digitar a letra ou o número do tamanho).</summary>
    public bool IrPara(int indice)
    {
        if (indice < 0 || indice >= _opcoes.Count || indice == Indice) return false;
        Indice = indice;
        return true;
    }

    /// <summary>← → andam de um em um; ↑ ↓ pulam uma linha (3). Não passa das pontas. Devolve se mexeu.</summary>
    public bool Mover(DirecaoGrade direcao)
    {
        var novo = direcao switch
        {
            DirecaoGrade.Esquerda => Indice - 1,
            DirecaoGrade.Direita => Indice + 1,
            DirecaoGrade.Cima => Indice - Colunas,
            _ => Indice + Colunas
        };
        if (novo < 0 || novo >= _opcoes.Count) return false;
        Indice = novo;
        return true;
    }

    /// <summary>
    /// Quais tamanhos aparecem na venda: os da grade; e "Sem tamanho" só no atacado
    /// (ou quando o produto não tem grade nenhuma — senão não haveria como vendê-lo).
    /// </summary>
    public static List<ProdutoTamanho> OpcoesParaVenda(IEnumerable<ProdutoTamanho> grade, TabelaPreco tabela)
    {
        var todos = grade.ToList();
        var reais = todos.Where(t => !t.EhSemTamanho).ToList();
        var semTamanho = todos.Where(t => t.EhSemTamanho);
        if (tabela == TabelaPreco.Atacado || reais.Count == 0) reais.AddRange(semTamanho);
        return reais;
    }
}

/// <summary>Escolha da quantidade: ↑ soma, ↓ subtrai, mínimo 1 e máximo o que ainda há em estoque.</summary>
public class SeletorQuantidade
{
    public SeletorQuantidade(int maximo)
    {
        if (maximo < 1) throw new RegraDeNegocioException("Não há estoque disponível para vender.");
        Maximo = maximo;
    }

    public int Maximo { get; }
    public int Valor { get; private set; } = 1;

    public bool Aumentar()
    {
        if (Valor >= Maximo) return false;
        Valor++;
        return true;
    }

    public bool Diminuir()
    {
        if (Valor <= 1) return false;
        Valor--;
        return true;
    }
}

public enum TipoDesconto { Reais, Percentual }

/// <summary>Desconto já convertido: em R$ ou em %, mais quanto isso vale em centavos (para mostrar na tela).</summary>
public record DescontoResolvido(long Centavos, decimal? Percentual, long ValorEmCentavos);

/// <summary>Etapa de desconto: ← escolhe R$, → escolhe %. Pode ficar vazio (sem desconto).</summary>
public class SelecaoDesconto
{
    public TipoDesconto Tipo { get; private set; } = TipoDesconto.Reais;
    public string Texto { get; set; } = "";

    public bool Esquerda() => Mudar(TipoDesconto.Reais);

    public bool Direita() => Mudar(TipoDesconto.Percentual);

    private bool Mudar(TipoDesconto tipo)
    {
        if (Tipo == tipo) return false;
        Tipo = tipo;
        return true;
    }

    /// <summary>Confere o que foi digitado contra o subtotal. Lança RegraDeNegocioException se for inválido.</summary>
    public DescontoResolvido Resolver(long subtotalCentavos)
    {
        if (string.IsNullOrWhiteSpace(Texto)) return new DescontoResolvido(0, null, 0);

        if (Tipo == TipoDesconto.Reais)
        {
            if (!Dinheiro.TentarConverter(Texto, out var centavos))
                throw new RegraDeNegocioException("Desconto inválido. Exemplo: 5,00");
            if (centavos > subtotalCentavos)
                throw new RegraDeNegocioException("O desconto não pode ser maior que o subtotal.");
            return new DescontoResolvido(centavos, null, centavos);
        }

        var limpo = Texto.Replace("%", "").Replace(" ", "").Replace(',', '.');
        if (!decimal.TryParse(limpo, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percentual))
            throw new RegraDeNegocioException("Percentual inválido. Exemplo: 10 ou 7,5");
        if (percentual < 0 || percentual > 100)
            throw new RegraDeNegocioException("O desconto em % deve ficar entre 0 e 100.");

        var valor = (long)Math.Round(subtotalCentavos * percentual / 100m, MidpointRounding.AwayFromZero);
        return new DescontoResolvido(0, percentual, valor);
    }
}

/// <summary>
/// Etapa de pagamento: ← → trocam a forma, ↑ ↓ mudam as parcelas (crédito). O valor recebido já vem
/// preenchido com o total; no dinheiro calcula o troco e não deixa finalizar com valor menor que o total.
/// </summary>
public class SelecaoPagamento
{
    public static readonly FormaPagamento[] Formas =
    {
        FormaPagamento.Dinheiro, FormaPagamento.Pix, FormaPagamento.CartaoDebito,
        FormaPagamento.CartaoCredito, FormaPagamento.Cheque, FormaPagamento.APrazo
    };

    public const int MaxParcelas = 4;

    public SelecaoPagamento(long totalCentavos)
    {
        TotalCentavos = totalCentavos;
        ValorRecebidoTexto = Dinheiro.FormatarParaEdicao(totalCentavos);
    }

    public long TotalCentavos { get; }
    public FormaPagamento Forma { get; private set; } = FormaPagamento.Dinheiro;
    public int Parcelas { get; private set; } = 1;
    public string ValorRecebidoTexto { get; set; }

    /// <summary>Cliente da venda a prazo.</summary>
    public int? ClienteId { get; set; }

    public static string Nome(FormaPagamento forma) => forma switch
    {
        FormaPagamento.Dinheiro => "Dinheiro",
        FormaPagamento.Pix => "Pix",
        FormaPagamento.CartaoDebito => "Cartão débito",
        FormaPagamento.CartaoCredito => "Cartão crédito",
        FormaPagamento.Cheque => "Cheque",
        _ => "A prazo"
    };

    /// <summary>→ passa para a próxima forma; no fim volta à primeira.</summary>
    public void Proxima() => Trocar(+1);

    /// <summary>← passa para a forma anterior; no começo vai para a última.</summary>
    public void Anterior() => Trocar(-1);

    private void Trocar(int passo)
    {
        var indice = Array.IndexOf(Formas, Forma);
        Forma = Formas[(indice + passo + Formas.Length) % Formas.Length];
        Parcelas = 1; // parcelas só existem no crédito: ao sair e voltar, recomeça em 1x
    }

    /// <summary>↑ no crédito: mais uma parcela (até 4). Nas outras formas não faz nada.</summary>
    public bool MaisParcelas()
    {
        if (Forma != FormaPagamento.CartaoCredito || Parcelas >= MaxParcelas) return false;
        Parcelas++;
        return true;
    }

    /// <summary>↓ no crédito: uma parcela a menos (mínimo 1).</summary>
    public bool MenosParcelas()
    {
        if (Forma != FormaPagamento.CartaoCredito || Parcelas <= 1) return false;
        Parcelas--;
        return true;
    }

    public long? ValorRecebidoCentavos =>
        Dinheiro.TentarConverter(ValorRecebidoTexto, out var centavos) ? centavos : null;

    /// <summary>Troco do dinheiro; zero nas outras formas ou se ainda falta dinheiro.</summary>
    public long TrocoCentavos =>
        Forma == FormaPagamento.Dinheiro && ValorRecebidoCentavos is long recebido
            ? Math.Max(0, recebido - TotalCentavos)
            : 0;

    /// <summary>Devolve o motivo de não poder finalizar, ou nulo se está tudo certo.</summary>
    public string? Validar()
    {
        if (Forma == FormaPagamento.Dinheiro)
        {
            if (ValorRecebidoCentavos is not long recebido)
                return "Informe o valor recebido. Exemplo: 50,00";
            if (recebido < TotalCentavos)
                return $"Valor recebido menor que o total: faltam {Dinheiro.Formatar(TotalCentavos - recebido)}.";
        }

        if (Forma == FormaPagamento.APrazo && ClienteId is null)
            return "A venda a prazo exige um cliente. Digite o nome e escolha na lista.";

        return null;
    }

    /// <summary>Monta o pedido que o VendaService espera.</summary>
    public PedidoVenda ParaPedido(Carrinho carrinho, DescontoResolvido desconto) => new(
        carrinho.ParaPedido(),
        desconto.Centavos,
        Forma,
        Forma == FormaPagamento.Dinheiro ? ValorRecebidoCentavos : null,
        carrinho.Tabela,
        desconto.Percentual,
        Forma == FormaPagamento.CartaoCredito ? Parcelas : 1,
        Forma == FormaPagamento.APrazo ? ClienteId : null);
}

public enum OpcaoSucesso { Reimprimir, NovaVenda }

/// <summary>Tela de sucesso: [Reimprimir cupom] [Nova venda]. Já nasce em "Nova venda": Enter, Enter segue a próxima venda.</summary>
public class SelecaoSucesso
{
    public OpcaoSucesso Opcao { get; private set; } = OpcaoSucesso.NovaVenda;

    public bool Esquerda()
    {
        if (Opcao == OpcaoSucesso.Reimprimir) return false;
        Opcao = OpcaoSucesso.Reimprimir;
        return true;
    }

    public bool Direita()
    {
        if (Opcao == OpcaoSucesso.NovaVenda) return false;
        Opcao = OpcaoSucesso.NovaVenda;
        return true;
    }
}
