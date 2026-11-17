using System.Text.Json;
using NexusLS.Core.Services;

namespace NexusLS.Core.Seguranca;

/// <summary>
/// Preferências de segurança deste computador. Ficam num JSON ao lado do banco (e não dentro dele), como o backup.
/// </summary>
public class ConfiguracaoSeguranca
{
    public const int InatividadeMaximaMinutos = 240;

    /// <summary>Depois de quantos minutos sem mexer o programa pede o PIN de novo. 0 = nunca bloqueia.</summary>
    public int MinutosInatividade { get; set; }

    /// <summary>Se ligado, quem não tem acesso à Gestão nem vê o botão (desligado: o botão aparece e pede o PIN de um gerente).</summary>
    public bool EsconderGestaoSemAcesso { get; set; }

    /// <summary>Retirada de dinheiro (sangria ou despesa) acima disto precisa de gerente. 0 = sem limite.</summary>
    public long SangriaMaximaSemGerenteCentavos { get; set; }

    /// <summary>Desconto acima deste percentual da venda precisa de gerente. 0 = sem limite.</summary>
    public int DescontoMaximoSemGerentePercent { get; set; }

    /// <summary>Cancelar uma venda exige o PIN de um gerente ou administrador (se quem cancela não for um).</summary>
    public bool PinGerenteParaCancelar { get; set; }

    /// <summary>Multa única sobre o que está vencido (%). 0 = não cobra.</summary>
    public decimal MultaAtrasoPercent { get; set; }

    /// <summary>Juros por mês de atraso (%), contados por dia sobre o saldo vencido. 0 = não cobra.</summary>
    public decimal JurosMesPercent { get; set; }

    /// <summary>Limite de crédito que já vem preenchido ao cadastrar um cliente novo. 0 = sem limite.</summary>
    public long LimiteCreditoPadraoCentavos { get; set; }

    /// <summary>Comissão padrão (% da venda líquida) de quem não tem percentual próprio. 0 = não paga.</summary>
    public decimal ComissaoPadraoPercent { get; set; }

    /// <summary>Percentual próprio de cada vendedor (pelo nome, como aparece nas vendas). Quem não está aqui usa o padrão.</summary>
    public Dictionary<string, decimal> ComissaoPorVendedor { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tirar da comissão o que o vendedor teve devolvido no período. Ligado por padrão.</summary>
    public bool ComissaoDescontaDevolucoes { get; set; } = true;

    public void Validar()
    {
        if (ComissaoPadraoPercent < 0 || ComissaoPadraoPercent > 100)
            throw new RegraDeNegocioException("A comissão padrão deve ficar entre 0 e 100%.");
        if (ComissaoPorVendedor.Values.Any(p => p < 0 || p > 100))
            throw new RegraDeNegocioException("A comissão de cada vendedor deve ficar entre 0 e 100%.");
        if (MultaAtrasoPercent < 0 || MultaAtrasoPercent > 100)
            throw new RegraDeNegocioException("A multa por atraso deve ficar entre 0 e 100%.");
        if (JurosMesPercent < 0 || JurosMesPercent > 100)
            throw new RegraDeNegocioException("Os juros ao mês devem ficar entre 0 e 100%.");
        if (LimiteCreditoPadraoCentavos < 0)
            throw new RegraDeNegocioException("O limite de crédito padrão não pode ser negativo.");
        if (SangriaMaximaSemGerenteCentavos < 0)
            throw new RegraDeNegocioException("O limite de sangria não pode ser negativo.");
        if (DescontoMaximoSemGerentePercent < 0 || DescontoMaximoSemGerentePercent > 100)
            throw new RegraDeNegocioException("O desconto máximo sem gerente deve ficar entre 0 (sem limite) e 100%.");
        if (MinutosInatividade < 0 || MinutosInatividade > InatividadeMaximaMinutos)
            throw new RegraDeNegocioException($"O bloqueio por inatividade deve ficar entre 0 (desligado) e {InatividadeMaximaMinutos} minutos.");
    }
}

public static class RegrasSeguranca
{
    /// <summary>A retirada passou do limite que o balcão pode fazer sozinho? Sem limite (0), nunca.</summary>
    public static bool RetiradaPrecisaDeGerente(long valorCentavos, long limiteCentavos) =>
        limiteCentavos > 0 && valorCentavos > limiteCentavos;

    /// <summary>O desconto passou do percentual que o balcão pode dar sozinho? Sem limite (0), nunca.</summary>
    public static bool DescontoPrecisaDeGerente(long descontoCentavos, long subtotalCentavos, int limitePercentual) =>
        limitePercentual > 0 && subtotalCentavos > 0 && descontoCentavos * 100 > subtotalCentavos * limitePercentual;

    /// <summary>Está parado tempo demais? Com o recurso desligado (0 minuto) nunca está.</summary>
    public static bool Ocioso(DateTime ultimaAtividade, DateTime agora, int minutos) =>
        minutos > 0 && agora - ultimaAtividade >= TimeSpan.FromMinutes(minutos);
}

/// <summary>Lê e grava a <see cref="ConfiguracaoSeguranca"/>. Arquivo ausente ou estragado vira o padrão.</summary>
public class ConfiguracaoSegurancaStore
{
    private readonly string _caminho;

    public ConfiguracaoSegurancaStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "seguranca", "config.json");

    public ConfiguracaoSeguranca Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            var c = JsonSerializer.Deserialize<ConfiguracaoSeguranca>(File.ReadAllText(_caminho)) ?? new();
            c.ComissaoPorVendedor = new Dictionary<string, decimal>(c.ComissaoPorVendedor ?? new(), StringComparer.OrdinalIgnoreCase);
            return c;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Salvar(ConfiguracaoSeguranca configuracao)
    {
        configuracao.Validar();
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}
