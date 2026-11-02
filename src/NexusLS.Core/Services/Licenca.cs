using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NexusLS.Core.Services;

public enum EstadoLicenca { SemLicenca, Valida, Vencendo, Vencida, Invalida, OutraMaquina }

/// <summary>Resultado de conferir a licença instalada: o estado, de quem é, até quando vale e um texto pronto para a tela.</summary>
public sealed record ResultadoLicenca(EstadoLicenca Estado, string? Cliente, DateTime? Validade, int? DiasRestantes, string Mensagem)
{
    /// <summary>Pode usar o programa? Sem licença o programa funciona em modo de avaliação (decisão do produto, ver <see cref="LicencaService.Obrigatoria"/>).</summary>
    public bool Ativa => Estado is EstadoLicenca.Valida or EstadoLicenca.Vencendo;
}

/// <summary>O arquivo de licença: dados do cliente e a assinatura feita com a chave PRIVADA do fornecedor.</summary>
public sealed class ArquivoLicenca
{
    public string Cliente { get; set; } = "";
    public string Validade { get; set; } = "";   // aaaa-MM-dd
    public string Maquina { get; set; } = "*";   // "*" = qualquer computador; senão o código mostrado em Configurações
    public string Assinatura { get; set; } = ""; // base64 (ECDSA P-256 / SHA-256)

    public string TextoAssinado() => $"NEXUSLS|{Cliente}|{Validade}|{Maquina}";
}

/// <summary>
/// Licença offline: o fornecedor assina (cliente, validade, computador) com uma chave privada que só ele tem; o programa só
/// carrega a chave PÚBLICA e confere a assinatura. Sem internet, sem servidor. Quem editar o arquivo invalida a assinatura.
/// </summary>
public class LicencaService
{
    /// <summary>
    /// Chave pública oficial (a privada fica só com o fornecedor, fora do repositório). Para trocar o par de chaves,
    /// gere outro par e substitua esta linha; as licenças antigas deixam de valer.
    /// </summary>
    public const string ChavePublicaOficial =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEcjrx9cki3JsIwVrzFHpMINg6ztdPaGwy3nNCxD2zRGjQx9XGjHoFZB9aKE94LIm71bkOefc/QTlDDYnPqWtLnw==";

    /// <summary>
    /// Se verdadeiro, o programa pode recusar o uso sem licença válida. Hoje fica FALSO: o programa funciona sem licença
    /// e apenas mostra o estado. Mude quando for começar a vender.
    /// </summary>
    public const bool Obrigatoria = false;

    public const int DiasDeAviso = 15;

    private readonly string _caminho;
    private readonly Func<DateTime> _hoje;
    private readonly string _chavePublica;
    private readonly Func<string> _maquina;

    public LicencaService(string caminho, Func<DateTime>? hoje = null, string? chavePublica = null, Func<string>? maquina = null)
    {
        _caminho = caminho;
        _hoje = hoje ?? (() => DateTime.Today);
        _chavePublica = chavePublica ?? ChavePublicaOficial;
        _maquina = maquina ?? CodigoDaMaquina;
    }

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "licenca.json");

    /// <summary>Código curto deste computador (o cliente passa ao fornecedor para pedir uma licença presa a ele).</summary>
    public static string CodigoDaMaquina()
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserDomainName}"));
        return Convert.ToHexString(bytes)[..12];
    }

    public string CodigoDestaMaquina => _maquina();

    public ResultadoLicenca Avaliar()
    {
        if (!File.Exists(_caminho))
            return new(EstadoLicenca.SemLicenca, null, null, null, "Sem licença (modo de avaliação).");
        try
        {
            return Avaliar(JsonSerializer.Deserialize<ArquivoLicenca>(File.ReadAllText(_caminho)));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new(EstadoLicenca.Invalida, null, null, null, "O arquivo de licença está ilegível.");
        }
    }

    private ResultadoLicenca Avaliar(ArquivoLicenca? arq)
    {
        if (arq is null || !AssinaturaConfere(arq))
            return new(EstadoLicenca.Invalida, arq?.Cliente, null, null, "Licença inválida: o arquivo foi alterado ou não é do Nexus LS.");
        if (!DateTime.TryParseExact(arq.Validade, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validade))
            return new(EstadoLicenca.Invalida, arq.Cliente, null, null, "Licença inválida: validade ilegível.");
        if (arq.Maquina != "*" && !string.Equals(arq.Maquina, _maquina(), StringComparison.OrdinalIgnoreCase))
            return new(EstadoLicenca.OutraMaquina, arq.Cliente, validade, null, "Esta licença é de outro computador.");

        var dias = (int)(validade.Date - _hoje().Date).TotalDays;
        if (dias < 0)
            return new(EstadoLicenca.Vencida, arq.Cliente, validade, dias, $"Licença vencida em {validade:dd/MM/yyyy}.");
        if (dias <= DiasDeAviso)
            return new(EstadoLicenca.Vencendo, arq.Cliente, validade, dias, $"Licença de {arq.Cliente} vence em {dias} dia(s) ({validade:dd/MM/yyyy}).");
        return new(EstadoLicenca.Valida, arq.Cliente, validade, dias, $"Licença de {arq.Cliente}, válida até {validade:dd/MM/yyyy}.");
    }

    private bool AssinaturaConfere(ArquivoLicenca arq)
    {
        try
        {
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(Convert.FromBase64String(_chavePublica), out _);
            return ec.VerifyData(Encoding.UTF8.GetBytes(arq.TextoAssinado()), Convert.FromBase64String(arq.Assinatura), HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>Confere o arquivo escolhido e, se estiver certo e dentro da validade, passa a ser a licença do programa.</summary>
    public ResultadoLicenca Instalar(string caminhoDoArquivo)
    {
        ArquivoLicenca? arq;
        try
        {
            arq = JsonSerializer.Deserialize<ArquivoLicenca>(File.ReadAllText(caminhoDoArquivo));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            throw new RegraDeNegocioException("Não consegui ler o arquivo de licença.");
        }

        var r = Avaliar(arq);
        if (!r.Ativa) throw new RegraDeNegocioException(r.Mensagem);

        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        File.WriteAllText(_caminho, JsonSerializer.Serialize(arq, new JsonSerializerOptions { WriteIndented = true }));
        return r;
    }
}

/// <summary>Do lado do fornecedor: assina uma licença com a chave privada (nunca vai dentro do programa).</summary>
public static class GeradorLicenca
{
    public static string Gerar(string chavePrivadaPkcs8Base64, string cliente, DateTime validade, string maquina = "*")
    {
        if (string.IsNullOrWhiteSpace(cliente)) throw new RegraDeNegocioException("Informe o nome do cliente.");
        var arq = new ArquivoLicenca { Cliente = cliente.Trim(), Validade = validade.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Maquina = string.IsNullOrWhiteSpace(maquina) ? "*" : maquina.Trim() };
        using var ec = ECDsa.Create();
        ec.ImportPkcs8PrivateKey(Convert.FromBase64String(chavePrivadaPkcs8Base64), out _);
        arq.Assinatura = Convert.ToBase64String(ec.SignData(Encoding.UTF8.GetBytes(arq.TextoAssinado()), HashAlgorithmName.SHA256));
        return JsonSerializer.Serialize(arq, new JsonSerializerOptions { WriteIndented = true });
    }
}
