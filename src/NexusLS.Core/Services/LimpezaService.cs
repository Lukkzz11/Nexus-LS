using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;
using NexusLS.Core.Models;

namespace NexusLS.Core.Services;

/// <summary>O que o "Limpar tudo" apaga. O nível maior inclui o menor.</summary>
public enum NivelLimpeza
{
    /// <summary>
    /// Vendas, caixa, pedidos, compras, financeiro, pagamentos de clientes e histórico de estoque.
    /// Fica: produtos (com o estoque de agora), clientes (sem dívidas), fornecedores, contas financeiras, funcionários e configurações.
    /// </summary>
    Movimento = 1,

    /// <summary>Tudo do nível anterior mais produtos, clientes e fornecedores. Ficam só contas, funcionários, configurações e atividades.</summary>
    Tudo = 2
}

public record ResultadoLimpeza(NivelLimpeza Nivel, string ArquivoBackup, IReadOnlyDictionary<string, int> Apagados)
{
    public int Total => Apagados.Values.Sum();
}

/// <summary>
/// "Limpar tudo": apaga dados em lote. É a única forma de apagar venda ou lançamento, por isso tem várias travas:
/// precisa do texto <c>APAGAR</c> digitado, faz backup antes (se o backup falhar, nada é apagado), apaga numa só
/// transação e deixa o registro na trilha de atividades. Funcionários, configurações e a própria trilha nunca são apagados.
/// A permissão (só administrador) é conferida pela tela; este serviço não conhece quem está logado.
/// </summary>
public class LimpezaService
{
    public const string TextoDeConfirmacao = "APAGAR";

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly Func<Task<string>> _backupAntes;
    private readonly Func<DateTime> _agora;

    /// <param name="backupAntes">Faz o backup de segurança e devolve o caminho do arquivo. Se lançar erro, a limpeza é cancelada.</param>
    public LimpezaService(IDbContextFactory<PdvDbContext> fabrica, Func<Task<string>> backupAntes, Func<DateTime>? agora = null)
    {
        _fabrica = fabrica;
        _backupAntes = backupAntes;
        _agora = agora ?? (() => DateTime.Now);
    }

    public static string Descricao(NivelLimpeza nivel) => nivel switch
    {
        NivelLimpeza.Movimento =>
            "Apaga vendas, caixa, pedidos, compras, financeiro, cheques, transferências, pagamentos de clientes e o histórico de estoque. " +
            "Mantém produtos (com o estoque de agora), clientes, fornecedores, contas, funcionários e configurações.",
        _ =>
            "Apaga TUDO que é da loja: além do movimento, os produtos, clientes e fornecedores. " +
            "Mantém só contas, funcionários, configurações e a trilha de atividades."
    };

    public async Task<ResultadoLimpeza> LimparAsync(NivelLimpeza nivel, string confirmacao)
    {
        if (!string.Equals((confirmacao ?? "").Trim(), TextoDeConfirmacao, StringComparison.Ordinal))
            throw new RegraDeNegocioException($"Para apagar, digite {TextoDeConfirmacao} (em maiúsculas).");

        string arquivo;
        try
        {
            arquivo = await _backupAntes();
        }
        catch (Exception e)
        {
            throw new RegraDeNegocioException($"Nada foi apagado, porque não foi possível fazer o backup de segurança antes: {e.Message}");
        }

        await using var db = await _fabrica.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var apagados = new Dictionary<string, int>();

        // A ordem importa: primeiro quem aponta para outros (itens, eventos), por último os cadastros.
        apagados["Eventos de pedido"] = await db.EventosPedido.ExecuteDeleteAsync();
        apagados["Itens de pedido"] = await db.ItensPedido.ExecuteDeleteAsync();
        apagados["Pedidos"] = await db.Pedidos.ExecuteDeleteAsync();
        apagados["Movimentações de estoque"] = await db.Movimentacoes.ExecuteDeleteAsync();
        apagados["Liquidações de cartão"] = await db.LiquidacoesCartao.ExecuteDeleteAsync(); // aponta para a venda
        apagados["Cheques recebidos"] = await db.ChequesRecebidos.ExecuteDeleteAsync();
        apagados["Transferências entre contas"] = await db.Transferencias.ExecuteDeleteAsync();
        apagados["Itens de venda"] = await db.ItensVenda.ExecuteDeleteAsync();
        apagados["Vendas"] = await db.Vendas.ExecuteDeleteAsync();
        apagados["Pagamentos de clientes"] = await db.PagamentosCliente.ExecuteDeleteAsync();
        apagados["Lançamentos financeiros"] = await db.Lancamentos.ExecuteDeleteAsync();
        apagados["Itens de compra"] = await db.ItensCompra.ExecuteDeleteAsync();
        apagados["Compras"] = await db.Compras.ExecuteDeleteAsync();
        apagados["Movimentos de caixa"] = await db.MovimentosCaixa.ExecuteDeleteAsync();
        apagados["Fechamentos de caixa"] = await db.FechamentosCaixa.ExecuteDeleteAsync();
        apagados["Turnos de caixa"] = await db.TurnosCaixa.ExecuteDeleteAsync();

        if (nivel == NivelLimpeza.Tudo)
        {
            apagados["Controle do catálogo online"] = await db.SincronizacaoProdutos.ExecuteDeleteAsync();
            apagados["Tamanhos"] = await db.TamanhosProduto.ExecuteDeleteAsync();
            apagados["Produtos"] = await db.Produtos.ExecuteDeleteAsync();
            apagados["Clientes"] = await db.Clientes.ExecuteDeleteAsync();
            apagados["Fornecedores"] = await db.Fornecedores.ExecuteDeleteAsync();
        }

        var total = apagados.Values.Sum();
        AtividadeService.Registrar(db, TipoAtividade.DadosApagados,
            $"Limpar tudo ({(nivel == NivelLimpeza.Tudo ? "tudo da loja" : "só o movimento")}): {total} registro(s) apagado(s). Backup: {Path.GetFileName(arquivo)}",
            "Limpar tudo", null, _agora());
        await db.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoLimpeza(nivel, arquivo, apagados);
    }
}
