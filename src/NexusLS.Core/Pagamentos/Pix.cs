namespace NexusLS.Core.Pagamentos;

public enum StatusPix { Pendente, Aprovado, Expirado, Cancelado, Erro, Estornado }

/// <summary>Cobrança Pix no formato padrão do Nexus, igual para qualquer gateway.</summary>
public record CobrancaPix(
    string Gateway, string IdExterno, string ChaveVenda, long ValorCentavos,
    string CopiaECola, string? QrCodeBase64, StatusPix Status, DateTime? ExpiraEm);

/// <summary>Contrato de um gateway Pix. Para ligar outro provedor basta implementar isto: o PDV não muda.</summary>
public interface IGatewayPix
{
    string Nome { get; }

    /// <summary>Cria a cobrança. A chave de idempotência garante que repetir a chamada não cobra duas vezes.</summary>
    Task<CobrancaPix> CriarAsync(long valorCentavos, string descricao, string chaveIdempotencia, CancellationToken ct = default);

    /// <summary>Situação real da cobrança no gateway (única fonte para considerar a venda paga).</summary>
    Task<StatusPix> ConsultarAsync(string idExterno, CancellationToken ct = default);
}

/// <summary>Gateway que sabe devolver o dinheiro de um Pix já aprovado (estorno total).</summary>
public interface IEstornoPix
{
    Task EstornarAsync(string idExterno, string chaveIdempotencia, CancellationToken ct = default);
}

/// <summary>Onde as cobranças ficam registradas (SQLite no programa; em memória nos testes).</summary>
public interface IRepositorioPix
{
    Task<CobrancaPix?> PorChaveVendaAsync(string chaveVenda, CancellationToken ct = default);
    Task<CobrancaPix?> PorIdExternoAsync(string gateway, string idExterno, CancellationToken ct = default);
    Task SalvarAsync(CobrancaPix cobranca, CancellationToken ct = default);
}

/// <summary>
/// Tarifa de um gateway, editável sem mexer no código. Custo = fixo + percentual (este só quando o valor passa do limiar).
/// Mercado Pago: 0,99% desde o primeiro centavo. SyncPay: R$ 0,75 fixo; acima de R$ 150 soma 1,99%.
/// </summary>
public record TarifaGateway(
    string Gateway, bool Ativo, decimal FixoCentavos, decimal Percentual, long LimiarPercentualCentavos = 0, int Prioridade = 0)
{
    /// <summary>Custo em centavos SEM arredondar, para o empate ser decidido pela conta exata.</summary>
    public decimal Custo(long valorCentavos) =>
        FixoCentavos + (valorCentavos > LimiarPercentualCentavos ? valorCentavos * Percentual / 100m : 0m);

    public static IReadOnlyList<TarifaGateway> Padrao { get; } = new[]
    {
        new TarifaGateway("MercadoPago", true, 0m, 0.99m, 0, Prioridade: 1),
        new TarifaGateway("SyncPay", true, 75m, 1.99m, 15000, Prioridade: 2),
    };
}

/// <summary>Escolhe o gateway ativo mais barato para o valor; no empate vence a menor Prioridade.</summary>
public static class RoteadorPix
{
    public static TarifaGateway Escolher(long valorCentavos, IEnumerable<TarifaGateway> tarifas)
    {
        if (valorCentavos <= 0) throw new ArgumentException("O valor precisa ser maior que zero.", nameof(valorCentavos));
        var escolhida = tarifas.Where(t => t.Ativo)
            .OrderBy(t => t.Custo(valorCentavos)).ThenBy(t => t.Prioridade)
            .FirstOrDefault();
        return escolhida ?? throw new InvalidOperationException("Nenhum gateway de Pix está ativo. Ative um em Configurações de Pagamento.");
    }
}

/// <summary>
/// Fluxo do Pix: escolhe o gateway, cria a cobrança uma única vez por venda e só dá como pago o que o gateway confirma.
/// </summary>
public class ServicoPix
{
    private readonly IReadOnlyDictionary<string, IGatewayPix> _gateways;
    private readonly IRepositorioPix _repositorio;
    private readonly Func<IEnumerable<TarifaGateway>> _tarifas;

    public ServicoPix(IEnumerable<IGatewayPix> gateways, IRepositorioPix repositorio, Func<IEnumerable<TarifaGateway>>? tarifas = null)
    {
        _gateways = gateways.ToDictionary(g => g.Nome, StringComparer.OrdinalIgnoreCase);
        _repositorio = repositorio;
        _tarifas = tarifas ?? (() => TarifaGateway.Padrao);
    }

    /// <summary>
    /// Inicia (ou retoma) a cobrança da venda. Se já existe uma pendente para a mesma venda, ela é reaproveitada, nunca duplicada.
    /// Se a anterior expirou ou foi cancelada, uma nova é criada com outra chave.
    /// </summary>
    public async Task<CobrancaPix> IniciarAsync(string chaveVenda, long valorCentavos, string descricao, CancellationToken ct = default)
    {
        var existente = await _repositorio.PorChaveVendaAsync(chaveVenda, ct).ConfigureAwait(false);
        if (existente is not null)
        {
            var atualizada = await AtualizarAsync(chaveVenda, ct).ConfigureAwait(false) ?? existente;
            if (atualizada.Status is StatusPix.Pendente or StatusPix.Aprovado) return atualizada;
            chaveVenda = chaveVenda + "#" + Guid.NewGuid().ToString("N")[..6];
        }

        var tarifa = RoteadorPix.Escolher(valorCentavos, _tarifas());
        if (!_gateways.TryGetValue(tarifa.Gateway, out var gateway))
            throw new InvalidOperationException($"O gateway {tarifa.Gateway} não está configurado.");

        var cobranca = await gateway.CriarAsync(valorCentavos, descricao, chaveVenda, ct).ConfigureAwait(false);
        cobranca = cobranca with { ChaveVenda = chaveVenda };
        await _repositorio.SalvarAsync(cobranca, ct).ConfigureAwait(false);
        return cobranca;
    }

    /// <summary>Avisa que uma cobrança mudou de situação (chamado pelo webhook). A tela do Pix usa isso para reagir na hora.</summary>
    public event Action<CobrancaPix>? CobrancaAtualizada;

    /// <summary>
    /// Chamado quando o gateway avisa (webhook) que algo mudou. O aviso NÃO é confiável por si só: sempre se pergunta ao gateway
    /// a situação real antes de gravar. Devolve nulo se a cobrança não é deste computador.
    /// </summary>
    public async Task<CobrancaPix?> ConfirmarPorAvisoAsync(string gateway, string idExterno, CancellationToken ct = default)
    {
        var atual = await _repositorio.PorIdExternoAsync(gateway, idExterno, ct).ConfigureAwait(false);
        if (atual is null) return null;
        var nova = await AtualizarAsync(atual.ChaveVenda, ct).ConfigureAwait(false) ?? atual;
        if (nova.Status != atual.Status) CobrancaAtualizada?.Invoke(nova);
        return nova;
    }

    /// <summary>Devolve o valor ao cliente (estorno total). Só vale para Pix aprovado; a situação só muda depois que o gateway aceita.</summary>
    public async Task<CobrancaPix> EstornarAsync(string chaveVenda, CancellationToken ct = default)
    {
        var atual = await _repositorio.PorChaveVendaAsync(chaveVenda, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Cobrança Pix não encontrada.");
        if (atual.Status != StatusPix.Aprovado) throw new InvalidOperationException("Só dá para estornar um Pix já pago.");
        if (!_gateways.TryGetValue(atual.Gateway, out var g) || g is not IEstornoPix estorno)
            throw new InvalidOperationException($"O gateway {atual.Gateway} não faz estorno pelo Nexus. Faça o estorno no painel dele.");
        await estorno.EstornarAsync(atual.IdExterno, "estorno-" + atual.IdExterno, ct).ConfigureAwait(false);
        var novo = atual with { Status = StatusPix.Estornado };
        await _repositorio.SalvarAsync(novo, ct).ConfigureAwait(false);
        return novo;
    }

    /// <summary>Consulta o gateway e grava a situação. Sem internet, devolve a última conhecida (a venda continua pendente).</summary>
    public async Task<CobrancaPix?> AtualizarAsync(string chaveVenda, CancellationToken ct = default)
    {
        var atual = await _repositorio.PorChaveVendaAsync(chaveVenda, ct).ConfigureAwait(false);
        if (atual is null) return null;
        if (atual.Status != StatusPix.Pendente) return atual;
        if (!_gateways.TryGetValue(atual.Gateway, out var gateway)) return atual;
        try
        {
            var status = await gateway.ConsultarAsync(atual.IdExterno, ct).ConfigureAwait(false);
            if (status == atual.Status) return atual;
            var novo = atual with { Status = status };
            await _repositorio.SalvarAsync(novo, ct).ConfigureAwait(false);
            return novo;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return atual;
        }
    }
}
