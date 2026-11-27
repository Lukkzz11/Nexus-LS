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

    /// <summary>O texto com um QR Code embaixo. Quem não sabe desenhar QR imprime só o texto.</summary>
    Task ImprimirComQrAsync(string texto, ConfiguracaoLoja loja, string qrCode) => ImprimirAsync(texto, loja);
}

/// <summary>Gera e imprime o cupom de uma venda com os dados da loja salvos em Configurações.</summary>
public class CupomService
{
    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly ConfiguracaoService _configuracao;
    private readonly IImpressoraCupom _impressora;
    private readonly Gaveta.GavetaService? _gaveta;

    public CupomService(IDbContextFactory<PdvDbContext> fabrica, ConfiguracaoService configuracao, IImpressoraCupom impressora, Gaveta.GavetaService? gaveta = null)
    {
        _fabrica = fabrica;
        _configuracao = configuracao;
        _impressora = impressora;
        _gaveta = gaveta;
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
    /// <summary>Imprime o comprovante para presente: sem preços e com o QR Code de troca.</summary>
    public async Task ImprimirPresenteAsync(int vendaId)
    {
        var (venda, loja) = await CarregarVendaAsync(vendaId);
        if (venda.Cancelada) throw new RegraDeNegocioException("Venda cancelada não tem comprovante para presente.");
        await _impressora.ImprimirComQrAsync(MontadorCupom.MontarPresente(venda, loja), loja, MontadorCupom.CodigoDeTroca(venda));
    }

    /// <summary>O comprovante da venda em PDF (padrão visual do Nexus, com campo de assinatura do cliente).</summary>
    public async Task<byte[]> GerarPdfAsync(int vendaId, bool pedirAssinatura = true)
    {
        var (venda, loja) = await CarregarVendaAsync(vendaId);
        return Pdf.DocumentosPdf.ComprovanteVenda(venda, loja, null, pedirAssinatura);
    }

    /// <summary>Gera o PDF do comprovante e guarda na gaveta (Cupons). Devolve o caminho do arquivo.</summary>
    public async Task<string> GuardarPdfNaGavetaAsync(int vendaId)
    {
        if (_gaveta is null) throw new RegraDeNegocioException("A gaveta de documentos não está disponível.");
        var (venda, _) = await CarregarVendaAsync(vendaId);
        var bytes = await GerarPdfAsync(vendaId);
        return _gaveta.Salvar(Gaveta.CategoriaGaveta.Cupons, Pdf.DocumentosPdf.NomeComprovante(venda), bytes);
    }

    /// <summary>
    /// Chamado logo após finalizar a venda: guarda o PDF na gaveta (se a opção estiver ligada) e imprime (se a impressão
    /// automática estiver ligada). Falha ao guardar o PDF nunca impede a venda nem a impressão. Devolve se imprimiu.
    /// </summary>
    public async Task<bool> ImprimirAoFinalizarAsync(int vendaId)
    {
        var loja = await _configuracao.ObterAsync();
        if (loja.SalvarCupomPdfAoFinalizar && _gaveta is not null)
        {
            try { await GuardarPdfNaGavetaAsync(vendaId); }
            catch (Exception ex) { Diagnostico.RegistroDeLog.Erro("Não foi possível guardar o PDF do cupom na gaveta.", ex); }
        }
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

    /// <summary>Imprime (ou reimprime) o vale-troca pelo código, com o saldo de agora.</summary>
    public async Task ImprimirValeTrocaAsync(string codigo)
    {
        var loja = await _configuracao.ObterAsync();
        await _impressora.ImprimirAsync(await GerarValeTrocaAsync(codigo, loja), loja);
    }

    /// <summary>O texto do vale-troca (o mesmo que vai para a impressora).</summary>
    public async Task<string> GerarValeTrocaAsync(string codigo, ConfiguracaoLoja? loja = null)
    {
        loja ??= await _configuracao.ObterAsync();
        await using var db = await _fabrica.CreateDbContextAsync();
        var vale = await TrocaService.AcharValeAsync(db, codigo) ?? throw new RegraDeNegocioException("Vale-troca não encontrado.");
        var saldo = await TrocaService.SaldoAsync(db, vale);
        var itens = vale.DevolucaoId is int d
            ? await db.ItensDevolucao.AsNoTracking().Where(i => i.DevolucaoId == d).OrderBy(i => i.Id).ToListAsync()
            : new List<ItemDevolucao>();
        return MontadorCupom.MontarValeTroca(saldo, itens, loja);
    }

    /// <summary>Cupom de teste com uma venda de exemplo, usando os dados do formulário (mesmo antes de salvar).</summary>
    public Task ImprimirTesteAsync(ConfiguracaoLoja loja)
    {
        var venda = MontadorCupom.VendaDeExemplo();
        venda.Operador = loja.NomeOperador;
        return _impressora.ImprimirAsync(MontadorCupom.Montar(venda, loja), loja);
    }

    private async Task<(string texto, ConfiguracaoLoja loja)> GerarAsync(int vendaId)
    {
        var (venda, loja) = await CarregarVendaAsync(vendaId);
        return (MontadorCupom.Montar(venda, loja), loja);
    }

    /// <summary>A venda com itens e cliente (e a referência de cada item, se o cupom mostra), e os dados da loja.</summary>
    private async Task<(Venda venda, ConfiguracaoLoja loja)> CarregarVendaAsync(int vendaId)
    {
        var loja = await _configuracao.ObterAsync();

        await using var db = await _fabrica.CreateDbContextAsync();
        var venda = await db.Vendas.AsNoTracking()
            .Include(v => v.Itens).Include(v => v.Cliente)
            .FirstOrDefaultAsync(v => v.Id == vendaId)
            ?? throw new RegraDeNegocioException("Venda não encontrada.");

        if (loja.CupomMostrarReferencia)
        {
            var ids = venda.Itens.Select(i => i.ProdutoId).Distinct().ToList();
            var refs = await db.Produtos.AsNoTracking().Where(p => ids.Contains(p.Id))
                .Select(p => new { p.Id, p.Referencia }).ToDictionaryAsync(p => p.Id, p => p.Referencia);
            foreach (var item in venda.Itens) item.Referencia = refs.GetValueOrDefault(item.ProdutoId);
        }

        return (venda, loja);
    }
}
