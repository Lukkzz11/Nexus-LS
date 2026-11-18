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

    /// <summary>Ao abrir o Nexus, avisa se há produtos abaixo do estoque mínimo. Ligado por padrão.</summary>
    public bool AvisarEstoqueBaixoAoAbrir { get; set; } = true;

    /// <summary>Ao abrir, avisa das contas a pagar vencidas e das que vencem nos próximos <see cref="DiasAvisoContas"/> dias.</summary>
    public bool AvisarContasAoAbrir { get; set; } = true;
    public int DiasAvisoContas { get; set; } = 3;

    /// <summary>Ao abrir, avisa se o último backup é mais velho que <see cref="DiasBackupAtrasado"/> dias (ou se nunca foi feito).</summary>
    public bool AvisarBackupAtrasado { get; set; } = true;

    /// <summary>Nome do formato de papel que a tela de Etiquetas já abre escolhido. Vazio = o de bobina 50 × 30.</summary>
    public string? EtiquetaFormatoPadrao { get; set; }

    /// <summary>A tela de Etiquetas já abre com "preço de atacado" marcado.</summary>
    public bool EtiquetaPrecoAtacadoPadrao { get; set; }

    /// <summary>Texto da cobrança por WhatsApp (com campos como {cliente}). Vazio = o texto padrão.</summary>
    public string? MensagemCobranca { get; set; }
    public int DiasBackupAtrasado { get; set; } = 3;

    /// <summary>Regime tributário da empresa (chave de <see cref="Services.PreparoFiscal.Regimes"/>). Vazio = ainda não informado.</summary>
    public string? FiscalRegime { get; set; }

    /// <summary>"Homologacao" (teste, sem valor fiscal) ou "Producao". Começa em homologação.</summary>
    public string FiscalAmbiente { get; set; } = Services.PreparoFiscal.Homologacao;

    /// <summary>Série da NFC-e (1 a 999).</summary>
    public int FiscalSerie { get; set; } = 1;

    /// <summary>Identificador do CSC (só o número; o código secreto NÃO é guardado aqui).</summary>
    public string? FiscalCscId { get; set; }

    /// <summary>NCM (8 dígitos) que já vem preenchido em produto novo.</summary>
    public string? FiscalNcmPadrao { get; set; }

    /// <summary>CFOP (4 dígitos) padrão das vendas. Vazio = 5102 (venda de mercadoria).</summary>
    public string? FiscalCfopPadrao { get; set; }

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
        if (!string.IsNullOrWhiteSpace(FiscalRegime) && !Services.PreparoFiscal.Regimes.ContainsKey(FiscalRegime))
            throw new RegraDeNegocioException("Regime tributário desconhecido.");
        if (FiscalAmbiente != Services.PreparoFiscal.Homologacao && FiscalAmbiente != Services.PreparoFiscal.Producao)
            throw new RegraDeNegocioException("O ambiente fiscal deve ser homologação ou produção.");
        if (FiscalSerie < 1 || FiscalSerie > 999)
            throw new RegraDeNegocioException("A série da nota deve ficar entre 1 e 999.");
        if (!string.IsNullOrWhiteSpace(FiscalCscId) && (!FiscalCscId.All(char.IsAsciiDigit) || FiscalCscId.Length > 6))
            throw new RegraDeNegocioException("O ID do CSC é só o número (até 6 dígitos).");
        if (!string.IsNullOrWhiteSpace(FiscalNcmPadrao) && !(FiscalNcmPadrao.Length == 8 && FiscalNcmPadrao.All(char.IsAsciiDigit)))
            throw new RegraDeNegocioException("O NCM tem 8 números.");
        if (!string.IsNullOrWhiteSpace(FiscalCfopPadrao) && !(FiscalCfopPadrao.Length == 4 && FiscalCfopPadrao.All(char.IsAsciiDigit)))
            throw new RegraDeNegocioException("O CFOP tem 4 números.");
        if ((MensagemCobranca?.Length ?? 0) > Services.MensagensCliente.MaximoCaracteres)
            throw new RegraDeNegocioException($"A mensagem de cobrança pode ter no máximo {Services.MensagensCliente.MaximoCaracteres} letras.");
        if (DiasAvisoContas < 0 || DiasAvisoContas > 60)
            throw new RegraDeNegocioException("Os dias de aviso das contas devem ficar entre 0 e 60.");
        if (DiasBackupAtrasado < 1 || DiasBackupAtrasado > 60)
            throw new RegraDeNegocioException("Os dias do aviso de backup devem ficar entre 1 e 60.");
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
