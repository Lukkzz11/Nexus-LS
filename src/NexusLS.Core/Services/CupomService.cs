using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>
/// Quem de fato manda o texto para uma impressora. O Core só conhece esta interface; a implementação
/// (impressora do Windows) fica no projeto da interface, e os testes usam uma impressora de mentira.
/// </summary>
public interface IImpressoraCupom
{
    Task ImprimirAsync(string texto, ConfiguracaoLoja loja);
}

/// <summary>Gera e imprime o cupom de uma venda com os dados da loja salvos em Configurações.</summary>
public class CupomService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly ConfiguracaoService _configuracao;
    private readonly IImpressoraCupom _impressora;

    public CupomService(IDbContextFactory<PdvDbContext> fabrica, ConfiguracaoService configuracao, IImpressoraCupom impressora)
    {
        _fabrica = fabrica;
        _configuracao = configuracao;
        _impressora = impressora;
    }

    public async Task<string> GerarTextoAsync(int vendaId)
    {
        var (texto, _) = await GerarAsync(vendaId);
        return texto;
    }

    /// <summary>Imprime (ou reimprime) o cupom, mesmo que a impressão automática esteja desligada.</summary>
    public async Task ImprimirAsync(int vendaId)
    {
        var (texto, loja) = await GerarAsync(vendaId);
        await _impressora.ImprimirAsync(texto, loja);
    }

    /// <summary>Chamado logo após finalizar a venda: só imprime se a impressão automática estiver ligada. Devolve se imprimiu.</summary>
    public async Task<bool> ImprimirAoFinalizarAsync(int vendaId)
    {
        var loja = await _configuracao.ObterAsync();
        if (!loja.ImprimirCupomAoFinalizar) return false;

        await ImprimirAsync(vendaId);
        return true;
    }

    /// <summary>Imprime a lista de separação de um pedido (sem preços), na mesma impressora do cupom.</summary>
    public async Task ImprimirListaSeparacaoAsync(int pedidoId)
    {
        var loja = await _configuracao.ObterAsync();
        await using var db = await _fabrica.CreateDbContextAsync();
        var pedido = await db.Pedidos.AsNoTracking().Include(p => p.Itens).FirstOrDefaultAsync(p => p.Id == pedidoId)
            ?? throw new RegraDeNegocioException("Pedido não encontrado.");
        await _impressora.ImprimirAsync(MontadorCupom.MontarListaSeparacao(pedido, loja), loja);
    }

    /// <summary>Cupom de teste com uma venda de exemplo, usando os dados do formulário (mesmo antes de salvar).</summary>
    public Task ImprimirTesteAsync(ConfiguracaoLoja loja) =>
        _impressora.ImprimirAsync(MontadorCupom.Montar(MontadorCupom.VendaDeExemplo(), loja), loja);

    private async Task<(string texto, ConfiguracaoLoja loja)> GerarAsync(int vendaId)
    {
        var loja = await _configuracao.ObterAsync();

        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.AsNoTracking()
            .Include(v => v.Itens).Include(v => v.Cliente)
            .FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");

        return (MontadorCupom.Montar(venda, loja), loja);
    }
}
