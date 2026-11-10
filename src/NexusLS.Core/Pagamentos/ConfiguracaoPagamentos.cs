using System.Text.Json;
using NexusLS.Core.Sincronizacao.Firebase;

namespace NexusLS.Core.Pagamentos;

/// <summary>
/// Configurações de pagamento desta instalação. O token do Mercado Pago NUNCA fica em texto claro nem no código:
/// vai para o arquivo protegido pelo Windows (DPAPI) e só este usuário neste computador consegue ler.
/// </summary>
public class ConfiguracaoPagamentos
{
    public bool MercadoPagoAtivo { get; set; }
    /// <summary>Token (Access Token) do Mercado Pago, já protegido. Nunca é exibido de volta na tela.</summary>
    public string? MercadoPagoTokenProtegido { get; set; }
    /// <summary>E-mail usado como pagador na cobrança Pix (o Mercado Pago exige um).</summary>
    public string EmailPagador { get; set; } = "";
    public decimal MercadoPagoPercentual { get; set; } = 0.99m;
    public decimal MercadoPagoFixoCentavos { get; set; } = 0m;
    /// <summary>Minutos até o QR Code expirar.</summary>
    public int ValidadeMinutos { get; set; } = 30;

    public bool TemToken => !string.IsNullOrWhiteSpace(MercadoPagoTokenProtegido);

    public TarifaGateway TarifaMercadoPago() =>
        new("MercadoPago", MercadoPagoAtivo && TemToken, MercadoPagoFixoCentavos, MercadoPagoPercentual, 0, Prioridade: 1);
}

public class ConfiguracaoPagamentosStore
{
    private static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true };
    private readonly string _caminho;
    private readonly IProtetorSegredo _protetor;

    public ConfiguracaoPagamentosStore(string caminho, IProtetorSegredo protetor)
    {
        _caminho = caminho;
        _protetor = protetor;
    }

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "pagamentos.json");

    public ConfiguracaoPagamentos Ler()
    {
        try
        {
            if (!File.Exists(_caminho)) return new ConfiguracaoPagamentos();
            return JsonSerializer.Deserialize<ConfiguracaoPagamentos>(File.ReadAllText(_caminho)) ?? new ConfiguracaoPagamentos();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            return new ConfiguracaoPagamentos();
        }
    }

    public void Salvar(ConfiguracaoPagamentos config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        File.WriteAllText(_caminho, JsonSerializer.Serialize(config, Opcoes));
    }

    /// <summary>Guarda um novo token (protegido). Texto vazio mantém o que já estava.</summary>
    public void DefinirToken(ConfiguracaoPagamentos config, string? tokenEmTextoClaro)
    {
        if (string.IsNullOrWhiteSpace(tokenEmTextoClaro)) return;
        config.MercadoPagoTokenProtegido = _protetor.Proteger(tokenEmTextoClaro.Trim());
    }

    /// <summary>O token em texto claro, só para montar a chamada à API. Nulo se não há ou não dá para ler.</summary>
    public string? Token()
    {
        var c = Ler();
        return c.TemToken ? _protetor.Desproteger(c.MercadoPagoTokenProtegido!) : null;
    }
}
