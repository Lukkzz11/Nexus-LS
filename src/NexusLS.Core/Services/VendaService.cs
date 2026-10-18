using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public class VendaService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    /// <param name="agora">Relógio. Nos testes passamos uma data fixa; no app, o padrão é DateTime.Now.</param>
    public VendaService(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    /// <summary>
    /// Grava a venda e dá baixa no estoque NA MESMA TRANSAÇÃO. Uma transação é um "tudo ou nada":
    /// se qualquer passo falhar, o banco volta ao estado anterior e nada fica pela metade.
    /// </summary>
    public async Task<Venda> FinalizarAsync(PedidoVenda pedido)
    {
        if (pedido.Itens.Count == 0)
            throw new RegraDeNegocioException("O carrinho está vazio.");
        if (pedido.Itens.Any(i => i.Quantidade <= 0))
            throw new RegraDeNegocioException("A quantidade de cada item deve ser maior que zero.");

        // Se o mesmo produto aparecer em duas linhas, soma as quantidades.
        var quantidades = pedido.Itens
            .GroupBy(i => i.ProdutoId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantidade));

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var ids = quantidades.Keys.ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        // Nesta etapa a venda ainda sai da linha "Sem tamanho"; a escolha do tamanho entra na próxima etapa.
        var semTamanho = await db.TamanhosProduto
            .Where(t => ids.Contains(t.ProdutoId) && t.Tamanho == Tamanhos.SemTamanho)
            .ToDictionaryAsync(t => t.ProdutoId);

        var venda = new Venda { DataHora = _agora(), FormaPagamento = pedido.FormaPagamento };
        var faltas = new List<string>();

        foreach (var (produtoId, quantidade) in quantidades)
        {
            if (!produtos.TryGetValue(produtoId, out var produto))
                throw new RegraDeNegocioException("Produto do carrinho não encontrado.");
            if (!produto.Ativo)
                throw new RegraDeNegocioException($"O produto \"{produto.Nome}\" está desativado.");
            var disponivel = semTamanho.TryGetValue(produtoId, out var linha) ? linha.Estoque : 0;
            if (disponivel < quantidade)
            {
                faltas.Add($"\"{produto.Nome}\" (pedido: {quantidade}, em estoque: {disponivel})");
                continue;
            }

            venda.Itens.Add(new ItemVenda
            {
                ProdutoId = produto.Id,
                NomeProduto = produto.Nome,                       // cópia do nome e do preço no momento da venda
                Tamanho = Tamanhos.SemTamanho,
                PrecoUnitarioCentavos = produto.PrecoVendaCentavos,
                Quantidade = quantidade,
                TotalCentavos = produto.PrecoVendaCentavos * quantidade
            });
        }

        if (faltas.Count > 0)
            throw new RegraDeNegocioException("Estoque insuficiente para: " + string.Join("; ", faltas) + ".");

        venda.SubtotalCentavos = venda.Itens.Sum(i => i.TotalCentavos);
        CalcularPagamento(venda, pedido);

        // 1º passo: grava a venda para o banco gerar o Id dela.
        db.Vendas.Add(venda);
        await db.SaveChangesAsync();

        // 2º passo: baixa o estoque e registra as movimentações ligadas à venda.
        foreach (var item in venda.Itens)
        {
            produtos[item.ProdutoId].EstoqueAtual -= item.Quantidade;
            semTamanho[item.ProdutoId].Estoque -= item.Quantidade;
            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = item.ProdutoId,
                DataHora = venda.DataHora,
                Tipo = TipoMovimentacao.Venda,
                Quantidade = -item.Quantidade,
                Tamanho = item.Tamanho,
                Motivo = $"Venda #{venda.Id}",
                VendaId = venda.Id
            });
        }
        await db.SaveChangesAsync();

        await transacao.CommitAsync(); // só aqui as mudanças viram definitivas
        return venda;
    }

    /// <summary>Cancela a venda e devolve o estoque. O registro NUNCA é apagado, só marcado.</summary>
    public async Task<Venda> CancelarAsync(int vendaId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        await using var transacao = await db.Database.BeginTransactionAsync();

        var venda = await db.Vendas.Include(v => v.Itens).FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");
        if (venda.Cancelada)
            throw new RegraDeNegocioException("Esta venda já está cancelada.");

        var agora = _agora();
        var ids = venda.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = await db.Produtos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var grades = await db.TamanhosProduto.Where(t => ids.Contains(t.ProdutoId)).ToListAsync();

        foreach (var item in venda.Itens)
        {
            produtos[item.ProdutoId].EstoqueAtual += item.Quantidade;

            // Devolve ao tamanho de onde saiu. Se o tamanho foi removido da grade depois da venda, ele é recriado.
            var linha = grades.FirstOrDefault(t => t.ProdutoId == item.ProdutoId && t.Tamanho == item.Tamanho);
            if (linha is null)
            {
                linha = new ProdutoTamanho { ProdutoId = item.ProdutoId, Tamanho = item.Tamanho, Estoque = 0 };
                db.TamanhosProduto.Add(linha);
                grades.Add(linha);
            }
            linha.Estoque += item.Quantidade;

            db.Movimentacoes.Add(new MovimentacaoEstoque
            {
                ProdutoId = item.ProdutoId,
                DataHora = agora,
                Tipo = TipoMovimentacao.CancelamentoVenda,
                Quantidade = item.Quantidade,
                Tamanho = item.Tamanho,
                Motivo = $"Cancelamento da venda #{venda.Id}",
                VendaId = venda.Id
            });
        }

        venda.Cancelada = true;
        venda.DataCancelamento = agora;

        await db.SaveChangesAsync();
        await transacao.CommitAsync();
        return venda;
    }

    /// <summary>Vendas de um dia (padrão: hoje), da mais recente para a mais antiga, incluindo as canceladas.</summary>
    public async Task<List<Venda>> ListarDoDiaAsync(DateTime? dia = null)
    {
        var inicio = (dia ?? _agora()).Date;
        var fim = inicio.AddDays(1);

        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Vendas.AsNoTracking().Include(v => v.Itens)
            .Where(v => v.DataHora >= inicio && v.DataHora < fim)
            .OrderByDescending(v => v.DataHora).ThenByDescending(v => v.Id)
            .ToListAsync();
    }

    /// <summary>Soma só as vendas válidas (cancelada não conta no total do dia).</summary>
    public static long TotalValido(IEnumerable<Venda> vendas) =>
        vendas.Where(v => !v.Cancelada).Sum(v => v.TotalCentavos);

    private static void CalcularPagamento(Venda venda, PedidoVenda pedido)
    {
        if (pedido.DescontoCentavos < 0)
            throw new RegraDeNegocioException("O desconto não pode ser negativo.");
        if (pedido.DescontoCentavos > venda.SubtotalCentavos)
            throw new RegraDeNegocioException("O desconto não pode ser maior que o subtotal.");

        venda.DescontoCentavos = pedido.DescontoCentavos;
        venda.TotalCentavos = venda.SubtotalCentavos - pedido.DescontoCentavos;

        if (pedido.FormaPagamento == FormaPagamento.Dinheiro)
        {
            var recebido = pedido.ValorRecebidoCentavos
                ?? throw new RegraDeNegocioException("Informe o valor recebido em dinheiro.");
            if (recebido < venda.TotalCentavos)
                throw new RegraDeNegocioException("O valor recebido é menor que o total da venda.");

            venda.ValorRecebidoCentavos = recebido;
            venda.TrocoCentavos = recebido - venda.TotalCentavos;
        }
        else
        {
            venda.ValorRecebidoCentavos = null; // Pix e cartão: paga exatamente o total
            venda.TrocoCentavos = 0;
        }
    }
}
