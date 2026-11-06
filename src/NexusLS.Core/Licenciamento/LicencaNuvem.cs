using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusLS.Core.Licenciamento;

/// <summary>Situação de uma licença vinda do Firebase (projeto nexus-ls, coleção "licencas").</summary>
public enum EstadoLicencaNuvem { Valida, Vencendo, Vencida, Bloqueada, OutraMaquina, NaoEncontrada, SemConexao, ChaveInvalida }

public record ResultadoLicencaNuvem(EstadoLicencaNuvem Estado, string Chave, string Cliente, DateTime? Validade, int? DiasRestantes, string Mensagem, bool Offline = false)
{
    public bool Ativa => Estado is EstadoLicencaNuvem.Valida or EstadoLicencaNuvem.Vencendo;
}

/// <summary>Documento "licencas/{chave}" como o site de licenças grava.</summary>
public record DocumentoLicenca(string Cliente, string? Validade, bool Ativa, string? Maquina, string? Plano);

public class FalhaDeConexaoLicencaException : Exception
{
    public FalhaDeConexaoLicencaException(string mensagem, Exception? inner = null) : base(mensagem, inner) { }
}

public interface IFonteLicenca
{
    /// <summary>Devolve o documento da chave, ou nulo se não existe. Lança <see cref="FalhaDeConexaoLicencaException"/> sem internet ou se o Firebase recusar.</summary>
    Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default);

    /// <summary>Grava o código do computador na licença (só a primeira ativação: as regras do Firestore impedem trocar depois).</summary>
    Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default);
}

/// <summary>
/// Lê a licença no Firestore pela API REST. Só usa a chave pública (Web API Key) do projeto: quem manda no que pode
/// ser lido e gravado são as regras do Firestore (veja docs/LICENCAS_FIREBASE.md). Nenhuma chave de administrador entra aqui.
/// </summary>
public class FonteLicencaFirestore : IFonteLicenca
{
    public const string ProjetoPadrao = "nexus-ls";
    public const string ChaveApiPadrao = "AIzaSyDkdYJIAcmAPyhxIs2DoJJH9e7q0WTfalI"; // Web API Key: pública por natureza.

    private readonly HttpClient _http;
    private readonly string _projeto;
    private readonly string _apiKey;

    public FonteLicencaFirestore(HttpClient http, string projeto = ProjetoPadrao, string apiKey = ChaveApiPadrao)
    {
        _http = http;
        _projeto = projeto;
        _apiKey = apiKey;
    }

    private string Url(string chave, string? mascara = null) =>
        $"https://firestore.googleapis.com/v1/projects/{_projeto}/databases/(default)/documents/licencas/{Uri.EscapeDataString(chave)}?key={_apiKey}{mascara}";

    public async Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default)
    {
        HttpResponseMessage r;
        try { r = await _http.GetAsync(Url(chave), ct).ConfigureAwait(false); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new FalhaDeConexaoLicencaException("Sem conexão com a internet para validar a licença.", ex);
        }
        using (r)
        {
            if (r.StatusCode == HttpStatusCode.NotFound) return null;
            if (r.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
                throw new FalhaDeConexaoLicencaException("O Firebase recusou a leitura da licença (confira as regras do Firestore).");
            if (!r.IsSuccessStatusCode)
                throw new FalhaDeConexaoLicencaException($"O Firebase respondeu {(int)r.StatusCode} ao ler a licença.");
            return Ler(await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
    }

    public async Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
    {
        var corpo = JsonSerializer.Serialize(new { fields = new { maquina = new { stringValue = maquina } } });
        using var pedido = new HttpRequestMessage(HttpMethod.Patch, Url(chave, "&updateMask.fieldPaths=maquina"))
        { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
        try
        {
            using var r = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
            if (!r.IsSuccessStatusCode)
                throw new FalhaDeConexaoLicencaException($"Não foi possível prender a licença a este computador (Firebase {(int)r.StatusCode}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new FalhaDeConexaoLicencaException("Sem conexão com a internet para ativar a licença.", ex);
        }
    }

    /// <summary>Converte o JSON do Firestore ("fields": {"x": {"stringValue": ...}}) no documento da licença.</summary>
    public static DocumentoLicenca? Ler(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("fields", out var campos)) return null;
            string? Texto(string nome)
            {
                if (!campos.TryGetProperty(nome, out var c)) return null;
                if (c.TryGetProperty("stringValue", out var s)) return s.GetString();
                if (c.TryGetProperty("timestampValue", out var t)) return t.GetString();
                return null;
            }
            var ativa = !campos.TryGetProperty("ativa", out var a) || !a.TryGetProperty("booleanValue", out var b) || b.GetBoolean();
            return new DocumentoLicenca(Texto("cliente") ?? "", Texto("validade"), ativa, Texto("maquina"), Texto("plano"));
        }
        catch (JsonException) { return null; }
    }
}

/// <summary>
/// Valida a licença com o Nexus LS License Manager através da Cloud Function centralizada do Firebase (projeto nexus-ls).
/// A Cloud Function valida a chave, calcula o vencimento no servidor, gerencia o limite de computadores e registra o dispositivo.
/// </summary>
public class FonteLicencaCloudFunction : IFonteLicenca
{
    public const string EndpointPadrao = "https://southamerica-east1-nexus-ls.cloudfunctions.net/validateLicense";

    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly Func<string> _obterCodigoMaquina;

    public FonteLicencaCloudFunction(HttpClient http, string endpoint = EndpointPadrao, Func<string>? obterCodigoMaquina = null)
    {
        _http = http;
        _endpoint = endpoint;
        _obterCodigoMaquina = obterCodigoMaquina ?? Services.LicencaService.CodigoDaMaquina;
    }

    public async Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default)
    {
        var maquina = _obterCodigoMaquina();
        var payload = JsonSerializer.Serialize(new
        {
            chave = chave.Trim(),
            dispositivoId = maquina,
            nomeDispositivo = Environment.MachineName,
            sistema = "NexusLS"
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        HttpResponseMessage r;
        try
        {
            r = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new FalhaDeConexaoLicencaException("Sem conexão com o servidor de licenças do Nexus LS.", ex);
        }

        using (r)
        {
            var corpo = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            // A função ainda não publicada responde 404 em HTML: isso não é "licença inexistente".
            if (!corpo.TrimStart().StartsWith("{"))
                throw new FalhaDeConexaoLicencaException("Servidor de licenças indisponível (função não publicada).");

            try
            {
                using var doc = JsonDocument.Parse(corpo);
                var root = doc.RootElement;
                var autorizado = root.TryGetProperty("autorizado", out var aut) && aut.GetBoolean();
                var situacao = root.TryGetProperty("situacao", out var sit) ? sit.GetString() ?? "" : "";
                var cliente = root.TryGetProperty("cliente", out var c) ? c.GetString() ?? "" : "";
                var vencimento = root.TryGetProperty("dataVencimento", out var v) ? v.GetString() : null;
                var dispAutorizado = root.TryGetProperty("dispositivoAutorizado", out var da) ? da.GetString() : maquina;

                if (situacao == "NAO_ENCONTRADA") return null;

                // Vencida: quem decide é a data (mostra "venceu em ..."), não o bloqueio.
                if (situacao == "VENCIDA" && vencimento is not null)
                    return new DocumentoLicenca(cliente, vencimento, Ativa: true, Maquina: dispAutorizado ?? maquina, Plano: null);

                if (situacao == "BLOQUEADA" || situacao == "CANCELADA")
                {
                    return new DocumentoLicenca(cliente, vencimento, Ativa: false, Maquina: maquina, Plano: null);
                }

                if (situacao == "LIMITE_ATINGIDO")
                {
                    return new DocumentoLicenca(cliente, vencimento, Ativa: true, Maquina: "OUTRO_COMPUTADOR", Plano: null);
                }

                return new DocumentoLicenca(cliente, vencimento, Ativa: autorizado, Maquina: dispAutorizado ?? maquina, Plano: null);
            }
            catch (JsonException)
            {
                throw new FalhaDeConexaoLicencaException("Resposta incompreensível do servidor de licenças.");
            }
        }
    }

    public Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
    {
        // Na Cloud Function, o registro do computador é realizado atomicamente durante a validação.
        return Task.CompletedTask;
    }
}

/// <summary>
/// Tenta a Cloud Function do License Manager e, se ela estiver fora do ar ou ainda não publicada,
/// lê o documento direto no Firestore (o site grava também cliente/validade/ativa/maquina).
/// </summary>
public class FonteLicencaComReserva : IFonteLicenca
{
    private readonly IFonteLicenca _principal;
    private readonly IFonteLicenca _reserva;
    private bool _usouReserva;

    public FonteLicencaComReserva(IFonteLicenca principal, IFonteLicenca reserva)
    {
        _principal = principal;
        _reserva = reserva;
    }

    public async Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default)
    {
        try
        {
            var d = await _principal.ObterAsync(chave, ct).ConfigureAwait(false);
            _usouReserva = false;
            return d;
        }
        catch (FalhaDeConexaoLicencaException)
        {
            _usouReserva = true;
            return await _reserva.ObterAsync(chave, ct).ConfigureAwait(false);
        }
    }

    public Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
        => (_usouReserva ? _reserva : _principal).RegistrarMaquinaAsync(chave, maquina, ct);
}

/// <summary>Guarda a última licença validada, para o programa e o atualizador mostrarem sem internet.</summary>
public class LicencaNuvemSalva
{
    public string Chave { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string Validade { get; set; } = "";   // yyyy-MM-dd
    public string Maquina { get; set; } = "";
    public DateTime VerificadaEm { get; set; }
}

/// <summary>
/// Regras da licença por nuvem: a pessoa digita a chave que recebeu do site; o programa lê o documento no Firebase,
/// confere se está ativa, se não venceu e se é deste computador, e guarda o resultado. Sem internet vale a última
/// verificação por até <see cref="DiasOffline"/> dias.
/// </summary>
public class ServicoLicencaNuvem
{
    public const int DiasDeAviso = 15;
    public const int DiasOffline = 7;
    private static readonly Regex PadraoChave = new("^[A-Za-z0-9\\-]{6,64}$", RegexOptions.Compiled);

    private readonly IFonteLicenca _fonte;
    private readonly string _caminho;
    private readonly Func<DateTime> _agora;
    private readonly Func<string> _maquina;

    public ServicoLicencaNuvem(IFonteLicenca fonte, string caminho, Func<DateTime>? agora = null, Func<string>? maquina = null)
    {
        _fonte = fonte;
        _caminho = caminho;
        _agora = agora ?? (() => DateTime.Now);
        _maquina = maquina ?? Services.LicencaService.CodigoDaMaquina;
    }

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "licenca-nuvem.json");

    public string CodigoDestaMaquina => _maquina();

    /// <summary>Primeira ativação (ou troca de chave): lê no Firebase, prende ao computador se ainda estiver livre e salva.</summary>
    public async Task<ResultadoLicencaNuvem> AtivarAsync(string chave, CancellationToken ct = default)
    {
        chave = (chave ?? "").Trim().ToUpperInvariant();
        if (!PadraoChave.IsMatch(chave))
            return new(EstadoLicencaNuvem.ChaveInvalida, chave, "", null, null, "Chave inválida: use de 6 a 64 letras, números ou hífens.");
        try
        {
            var doc = await _fonte.ObterAsync(chave, ct).ConfigureAwait(false);
            if (doc is null)
                return new(EstadoLicencaNuvem.NaoEncontrada, chave, "", null, null, "Licença não encontrada. Confira a chave.");

            var maquinaDoc = (doc.Maquina ?? "").Trim();
            var livre = maquinaDoc.Length == 0;
            var aqui = _maquina();
            var r = Avaliar(chave, doc, maquinaDoc.Length == 0 ? aqui : maquinaDoc, aqui);
            if (r.Ativa && livre)
            {
                await _fonte.RegistrarMaquinaAsync(chave, aqui, ct).ConfigureAwait(false);
                maquinaDoc = aqui;
            }
            if (r.Ativa) Salvar(chave, doc, maquinaDoc);
            return r;
        }
        catch (FalhaDeConexaoLicencaException ex)
        {
            return new(EstadoLicencaNuvem.SemConexao, chave, "", null, null, ex.Message);
        }
    }

    /// <summary>Reconfere a licença salva no Firebase (ao abrir o atualizador). Sem internet, usa a última verificação dentro da tolerância.</summary>
    public async Task<ResultadoLicencaNuvem> VerificarAsync(CancellationToken ct = default)
    {
        var salva = Carregar();
        if (salva is null)
            return new(EstadoLicencaNuvem.NaoEncontrada, "", "", null, null, "Nenhuma licença ativada neste computador.");
        try
        {
            var doc = await _fonte.ObterAsync(salva.Chave, ct).ConfigureAwait(false);
            if (doc is null)
                return new(EstadoLicencaNuvem.NaoEncontrada, salva.Chave, salva.Cliente, null, null, "Esta licença não existe mais no sistema.");
            var maquinaDoc = string.IsNullOrWhiteSpace(doc.Maquina) ? _maquina() : doc.Maquina!.Trim();
            var r = Avaliar(salva.Chave, doc, maquinaDoc, _maquina());
            if (r.Ativa) Salvar(salva.Chave, doc, maquinaDoc);
            return r;
        }
        catch (FalhaDeConexaoLicencaException)
        {
            return AvaliarLocal(porFaltaDeConexao: true);
        }
    }

    /// <summary>Estado a partir do que está salvo, sem internet (o programa usa isto para mostrar a licença).</summary>
    public ResultadoLicencaNuvem AvaliarLocal(bool porFaltaDeConexao = false)
    {
        var s = Carregar();
        if (s is null)
            return new(EstadoLicencaNuvem.NaoEncontrada, "", "", null, null, "Sem licença ativada.");
        if (s.Maquina != "*" && !string.Equals(s.Maquina, _maquina(), StringComparison.OrdinalIgnoreCase))
            return new(EstadoLicencaNuvem.OutraMaquina, s.Chave, s.Cliente, null, null, "Esta licença é de outro computador.");
        if (porFaltaDeConexao && (_agora() - s.VerificadaEm).TotalDays > DiasOffline)
            return new(EstadoLicencaNuvem.SemConexao, s.Chave, s.Cliente, null, null,
                $"Sem internet há mais de {DiasOffline} dias: conecte para validar a licença.", Offline: true);
        if (!DateTime.TryParseExact(s.Validade, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validade))
            return new(EstadoLicencaNuvem.ChaveInvalida, s.Chave, s.Cliente, null, null, "Licença salva com validade ilegível.");
        var r = PorValidade(s.Chave, s.Cliente, validade);
        return r with { Offline = true, Mensagem = r.Mensagem + (porFaltaDeConexao ? " (sem internet: última verificação)" : "") };
    }

    public void Remover()
    {
        if (File.Exists(_caminho)) File.Delete(_caminho);
    }

    private ResultadoLicencaNuvem Avaliar(string chave, DocumentoLicenca doc, string maquinaDoc, string aqui)
    {
        if (!doc.Ativa)
            return new(EstadoLicencaNuvem.Bloqueada, chave, doc.Cliente, null, null, "Esta licença foi bloqueada. Fale com o fornecedor.");
        if (maquinaDoc != "*" && !string.Equals(maquinaDoc, aqui, StringComparison.OrdinalIgnoreCase))
            return new(EstadoLicencaNuvem.OutraMaquina, chave, doc.Cliente, null, null, "Esta licença já está em uso em outro computador.");
        if (!TentarValidade(doc.Validade, out var validade))
            return new(EstadoLicencaNuvem.ChaveInvalida, chave, doc.Cliente, null, null, "A licença não tem uma validade válida no sistema.");
        return PorValidade(chave, doc.Cliente, validade);
    }

    private ResultadoLicencaNuvem PorValidade(string chave, string cliente, DateTime validade)
    {
        var dias = (int)(validade.Date - _agora().Date).TotalDays;
        if (dias < 0) return new(EstadoLicencaNuvem.Vencida, chave, cliente, validade, dias, $"Licença de {cliente} venceu em {validade:dd/MM/yyyy}.");
        if (dias <= DiasDeAviso) return new(EstadoLicencaNuvem.Vencendo, chave, cliente, validade, dias, $"Licença de {cliente} vence em {dias} dia(s) ({validade:dd/MM/yyyy}).");
        return new(EstadoLicencaNuvem.Valida, chave, cliente, validade, dias, $"Licença de {cliente}, válida até {validade:dd/MM/yyyy}.");
    }

    /// <summary>Aceita "2026-12-31", "2026-12-31T00:00:00Z" (timestamp do Firestore) e "31/12/2026".</summary>
    public static bool TentarValidade(string? texto, out DateTime validade)
    {
        validade = default;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        texto = texto.Trim();
        if (DateTime.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out validade)) return true;
        if (DateTime.TryParseExact(texto, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out validade)) return true;
        if (DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var o))
        {
            validade = o.UtcDateTime.Date;
            return true;
        }
        return false;
    }

    private void Salvar(string chave, DocumentoLicenca doc, string maquina)
    {
        TentarValidade(doc.Validade, out var v);
        var s = new LicencaNuvemSalva { Chave = chave, Cliente = doc.Cliente, Validade = v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Maquina = maquina, VerificadaEm = _agora() };
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }

    private LicencaNuvemSalva? Carregar()
    {
        try { return File.Exists(_caminho) ? JsonSerializer.Deserialize<LicencaNuvemSalva>(File.ReadAllText(_caminho)) : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }
}
