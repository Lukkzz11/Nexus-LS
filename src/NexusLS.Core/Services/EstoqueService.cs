using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

public class EstoqueService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public EstoqueService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Ajuste manual de estoque de UM tamanho (padrão: "Sem tamanho"). Regras para a quantidade:
    /// Entrada = positiva (soma); Perda = positiva (subtrai); Correção = com sinal (+/-), nunca zero.
    /// Perda e Correção exigem motivo. O estoque do tamanho nunca fica negativo.
    /// Tamanho, total do produto e movimentação são gravados juntos: ou grava tudo ou nada.
    /// </summary>
    public async Task<Produto> AjustarAsync(
        int produtoId, TipoMovimentacao tipo, int quantidade, string? motivo, string? tamanho = null)
    {
        int variacao = tipo switch
        {
            TipoMovimentacao.Entrada => quantidade > 0
                ? quantidade
                : throw new RegraDeNegocioException("A quantidade de entrada deve ser maior que zero."),
            TipoMovimentacao.Perda => quantidade > 0
                ? -quantidade
                : throw new RegraDeNegocioException("A quantidade perdida deve ser maior que zero."),
            TipoMovimentacao.Correcao => quantidade != 0
                ? quantidade
                : throw new RegraDeNegocioException("A correção não pode ser zero."),
            _ => throw new RegraDeNegocioException("Tipo de ajuste inválido. Vendas e cancelamentos têm tela própria.")
        };

        if (tipo != TipoMovimentacao.Entrada && string.IsNullOrWhiteSpace(motivo))
            throw new RegraDeNegocioException("Informe o motivo do ajuste.");

        var nomeTamanho = string.IsNullOrWhiteSpace(tamanho) ? Tamanhos.SemTamanho : tamanho.Trim();
        if (!string.Equals(nomeTamanho, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase))
            nomeTamanho = nomeTamanho.ToUpperInvariant();
        else
            nomeTamanho = Tamanhos.SemTamanho;

        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(produtoId)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        var linha = await db.TamanhosProduto
            .FirstOrDefaultAsync(t => t.ProdutoId == produtoId && t.Tamanho == nomeTamanho)
            ?? throw new RegraDeNegocioException($"O produto \"{produto.Nome}\" não tem o tamanho {nomeTamanho}.");

        if (linha.Estoque + variacao < 0)
        {
            var onde = linha.EhSemTamanho ? "" : $" no tamanho {linha.Tamanho}";
            throw new RegraDeNegocioException(
                $"Estoque insuficiente: \"{produto.Nome}\" tem {linha.Estoque} unidade(s){onde}.");
        }

        linha.Estoque += variacao;
        produto.EstoqueAtual += variacao;
        db.Movimentacoes.Add(new MovimentacaoEstoque
        {
            ProdutoId = produto.Id,
            DataHora = DateTime.Now,
            Tipo = tipo,
            Quantidade = variacao,
            Tamanho = linha.Tamanho,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()
        });

        await db.SaveChangesAsync(); // uma única transação para as três mudanças
        return produto;
    }

    /// <summary>Produtos ativos com estoque abaixo do mínimo (o mais crítico primeiro).</summary>
    public async Task<List<Produto>> ListarAbaixoDoMinimoAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Produtos.AsNoTracking()
            .Where(p => p.Ativo && p.EstoqueAtual < p.EstoqueMinimo)
            .OrderBy(p => p.EstoqueAtual - p.EstoqueMinimo)
            .ThenBy(p => p.Nome)
            .ToListAsync();
    }

    public async Task<List<MovimentacaoEstoque>> ListarMovimentacoesAsync(int produtoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Movimentacoes.AsNoTracking()
            .Where(m => m.ProdutoId == produtoId)
            .OrderByDescending(m => m.DataHora).ThenByDescending(m => m.Id)
            .ToListAsync();
    }
}
