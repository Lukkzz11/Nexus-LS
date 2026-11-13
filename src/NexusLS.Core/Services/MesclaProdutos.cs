using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>Produtos ativos com o mesmo nome (ignorando maiúsculas, acentos e espaços repetidos).</summary>
public record GrupoDuplicado(string Nome, IReadOnlyList<Produto> Produtos);

/// <summary>
/// Junta produtos repetidos: o estoque de cada tamanho do repetido é somado ao produto que fica (com movimentação de entrada,
/// para a trilha), e o repetido é desativado. Nada é apagado: as vendas antigas continuam apontando para ele.
/// </summary>
public class MesclaProdutos
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<DateTime> _agora;

    public MesclaProdutos(IDbContextFactory<PdvDbContext> fabrica, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _agora = agora ?? (() => DateTime.Now);
    }

    public static string Chave(string nome)
    {
        var d = (nome ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public async Task<IReadOnlyList<GrupoDuplicado>> ListarAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var ativos = await db.Produtos.AsNoTracking().Where(p => p.Ativo).ToListAsync();
        return ativos.Where(p => Chave(p.Nome).Length > 0).GroupBy(p => Chave(p.Nome)).Where(g => g.Count() > 1)
            .Select(g =>
            {
                var ordem = g.OrderByDescending(p => p.EstoqueAtual).ThenBy(p => p.Id).ToList();
                return new GrupoDuplicado(ordem[0].Nome, ordem);
            })
            .OrderBy(g => g.Nome).ToList();
    }

    /// <summary>Passa o estoque do repetido para o que fica e desativa o repetido, tudo numa só gravação.</summary>
    public async Task MesclarAsync(int manterId, int removerId)
    {
        if (manterId == removerId) throw new RegraDeNegocioException("Escolha dois produtos diferentes.");
        await using var db = await _fabrica.CreateDbContextAsync();
        var manter = await db.Produtos.FindAsync(manterId) ?? throw new RegraDeNegocioException("Produto não encontrado.");
        var remover = await db.Produtos.FindAsync(removerId) ?? throw new RegraDeNegocioException("Produto não encontrado.");
        var tamManter = await db.TamanhosProduto.Where(t => t.ProdutoId == manterId).ToListAsync();
        var tamRemover = await db.TamanhosProduto.Where(t => t.ProdutoId == removerId).ToListAsync();
        var agora = _agora();
        var proximaOrdem = tamManter.Count == 0 ? 1 : tamManter.Max(t => t.Ordem) + 1;

        foreach (var origem in tamRemover.Where(t => t.Estoque > 0))
        {
            var destino = tamManter.FirstOrDefault(t => string.Equals(t.Tamanho, origem.Tamanho, StringComparison.OrdinalIgnoreCase));
            if (destino is null)
            {
                destino = new ProdutoTamanho { ProdutoId = manterId, Tamanho = origem.Tamanho, Estoque = 0, Ordem = proximaOrdem++ };
                db.TamanhosProduto.Add(destino);
                tamManter.Add(destino);
            }
            var qtd = origem.Estoque;
            destino.Estoque += qtd;
            manter.EstoqueAtual += qtd;
            origem.Estoque = 0;
            remover.EstoqueAtual -= qtd;
            db.Movimentacoes.Add(new MovimentacaoEstoque { ProdutoId = manterId, DataHora = agora, Tipo = TipoMovimentacao.Entrada, Quantidade = qtd, Tamanho = destino.Tamanho, Motivo = $"Mesclagem com \"{remover.Nome}\"" });
            db.Movimentacoes.Add(new MovimentacaoEstoque { ProdutoId = removerId, DataHora = agora, Tipo = TipoMovimentacao.Correcao, Quantidade = -qtd, Tamanho = origem.Tamanho, Motivo = $"Mesclado em \"{manter.Nome}\"" });
        }
        remover.Ativo = false;
        remover.AtualizadoEm = DateTime.UtcNow;
        manter.AtualizadoEm = DateTime.UtcNow;
        AtividadeService.Registrar(db, TipoAtividade.EstoqueAjustado, $"Produto \"{remover.Nome}\" mesclado em \"{manter.Nome}\" (duplicado desativado)", $"Produto {manter.Nome}");
        await db.SaveChangesAsync();
    }
}
