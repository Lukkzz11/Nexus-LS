using Microsoft.EntityFrameworkCore;
using PdvSimples.Core.Data;
using PdvSimples.Core.Models;

namespace PdvSimples.Core.Services;

public class EstoqueService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;

    public EstoqueService(IDbContextFactory<PdvDbContext> fabrica) => _fabrica = fabrica;

    /// <summary>
    /// Ajuste manual de estoque. Regras para a quantidade:
    /// Entrada = positiva (soma); Perda = positiva (subtrai); Correção = com sinal (+/-), nunca zero.
    /// Perda e Correção exigem motivo. O estoque nunca fica negativo.
    /// Produto e movimentação são gravados juntos: ou grava os dois ou nenhum.
    /// </summary>
    public async Task<Produto> AjustarAsync(int produtoId, TipoMovimentacao tipo, int quantidade, string? motivo)
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

        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FindAsync(produtoId)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");

        if (produto.EstoqueAtual + variacao < 0)
            throw new RegraDeNegocioException(
                $"Estoque insuficiente: \"{produto.Nome}\" tem {produto.EstoqueAtual} unidade(s).");

        produto.EstoqueAtual += variacao;
        db.Movimentacoes.Add(new MovimentacaoEstoque
        {
            ProdutoId = produto.Id,
            DataHora = DateTime.Now,
            Tipo = tipo,
            Quantidade = variacao,
            Motivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim()
        });

        await db.SaveChangesAsync(); // uma única transação para as duas mudanças
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
