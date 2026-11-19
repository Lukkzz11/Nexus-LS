using NexusLS.Core.Services;

namespace NexusLS.Core.Consultor;

/// <summary>Quão importante é o cartão. A ordem dos números é a ordem de importância (urgente aparece primeiro).</summary>
public enum GravidadeSugestao { Informacao = 0, Atencao = 1, Urgente = 2 }

/// <summary>Assunto do cartão. Quem não pode ver o Financeiro não recebe cartões de <see cref="Financeiro"/> (nem os dados deles são lidos).</summary>
public enum AreaConsultor { Vendas = 0, Financeiro = 1, Estoque = 2 }

/// <summary>Para onde o botão do cartão leva. O programa traduz para uma tela; o Core não conhece telas.</summary>
public enum DestinoConsultor { Nenhum = 0, Estoque, EstoqueParado, Desempenho, Clientes, Financeiro, RelatorioFinanceiro }

/// <summary>
/// Um cartão do Consultor Nexus. O <see cref="Id"/> é fixo por regra (é ele que "Dispensar" guarda). O texto é em português
/// simples, traz os números usados e não promete nada.
/// </summary>
public sealed record Sugestao(string Id, string Titulo, string Texto, GravidadeSugestao Gravidade, AreaConsultor Area, string AcaoSugerida)
{
    public DestinoConsultor Destino { get; init; } = DestinoConsultor.Nenhum;
}

/// <summary>Vendas de uma forma de pagamento (já sem as canceladas).</summary>
public sealed record VendasPorForma(Models.FormaPagamento Forma, int Vendas, long TotalCentavos);

/// <summary>Um produto controlado por estoque que vendeu nos últimos 30 dias.</summary>
public sealed record ProdutoComGiro(int ProdutoId, string Nome, int Estoque, int EstoqueMinimo, int PecasVendidasUltimos30Dias);

/// <summary>Saldo do mês até hoje (mesma conta do Relatório financeiro: vendas + entradas lançadas, menos saídas lançadas).</summary>
public sealed record SaldoDoMes(DateTime Inicio, DateTime Fim, long EntradasCentavos, long SaidasCentavos, long SaldoCentavos);

/// <summary>
/// Os números já somados que as regras recebem. Regra nenhuma lê banco. Lista vazia ou campo nulo = "não há dado":
/// a regra então não gera cartão (o Consultor nunca inventa número).
/// </summary>
public sealed record DadosConsultor(DateTime Hoje)
{
    /// <summary>Data da primeira venda não cancelada do banco (nulo = nunca vendeu). Mostra se o histórico de uma comparação existe.</summary>
    public DateTime? PrimeiraVenda { get; init; }

    /// <summary>Vendas por dia dos últimos 56 dias FECHADOS (de hoje-56 a ontem, dias sem venda zerados), em ordem de data.</summary>
    public IReadOnlyList<VendasDoDia> VendasPorDia { get; init; } = [];

    /// <summary>Vendas dos últimos 30 dias fechados, por forma de pagamento.</summary>
    public IReadOnlyList<VendasPorForma> Formas { get; init; } = [];

    /// <summary>Produtos com estoque e sem venda há 60 dias ou mais (só os que já venderam antes).</summary>
    public IReadOnlyList<RelatorioProdutos.ProdutoParado> Parados { get; init; } = [];

    /// <summary>Produtos controlados que venderam nos últimos 30 dias.</summary>
    public IReadOnlyList<ProdutoComGiro> ProdutosComVenda { get; init; } = [];

    /// <summary>Contas a pagar em aberto vencidas ou que vencem até hoje+7.</summary>
    public IReadOnlyList<ContaAVencer> Contas { get; init; } = [];

    public SaldoDoMes? Saldo { get; init; }

    /// <summary>Clientes que fazem aniversário de hoje até hoje+7.</summary>
    public IReadOnlyList<AvisosAoAbrir.Aniversariante> Aniversariantes { get; init; } = [];
}
