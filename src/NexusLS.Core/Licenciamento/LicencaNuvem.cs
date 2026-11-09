using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NexusLS.Core.Licenciamento;

/// <summary>Situação de uma licença vinda do Firebase (projeto nexus-ls, coleção "licencas").</summary>
public enum EstadoLicencaNuvem { Valida, Vencendo, Vencida, Bloqueada, OutraMaquina, NaoEncontrada, SemConexao, ChaveInvalida }

public record ResultadoLicencaNuvem(
    EstadoLicencaNuvem Estado,
    string Chave,
    string Cliente,
    DateTime? Validade,
    int? DiasRestantes,
    string Mensagem,
    bool Offline = false,
    string? Empresa = null,
    string? Plano = null,
    int? DispositivosUsados = null,
    int? LimiteDispositivos = null,
    DateTime? DataInicio = null,
    DateTime? VerificadaEm = null)
{
    public bool Ativa => Estado is EstadoLicencaNuvem.Valida or EstadoLicencaNuvem.Vencendo;
}

/// <summary>Documento "licencas/{chave}" como o site/License Manager de licenças grava.</summary>
public record DocumentoLicenca(
    string Cliente,
    string? Validade,
    bool Ativa,
    string? Maquina,
    string? Plano,
    string? Empresa = null,
    string? DataInicio = null,
    int? DispositivosUsados = null,
    int? LimiteDispositivos = null,
    string? MotivoBloqueio = null);

public class FalhaDeConexaoLicencaException : Exception
{
    public FalhaDeConexaoLicencaException(string mensagem, Exception? inner = null) : base(mensagem, inner) { }
}

public interface IFonteLicenca
{
    /// <summary>Devolve o documento da chave, ou nulo se não existe. Lança <see cref="FalhaDeConexaoLicencaException"/> sem internet ou se o Firebase recusar.</summary>
    Task<DocumentoLicenca?> ObterAsync(string chave, CancellationToken ct = default);

    /// <summary>Grava o código do computador na licença (suporta 1 ou múltiplos códigos separados por vírgula conforme o limite).</summary>
    Task RegistrarMaquinaAsync(string chave, string maquina, CancellationToken ct = default);

    /// <summary>Desvincula o código deste computador na nuvem quando o usuário remove a licença localmente.</summary>
    Task DesvincularMaquinaAsync(string chave, string maquina, CancellationToken ct = default) => Task.CompletedTask;
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
    private readonly Func<string> _obterCodigoMaquina;

    public FonteLicencaFirestore(HttpClient http, string projeto = ProjetoPadrao, string apiKey = ChaveApiPadrao, Func<string>? obterCodigoMaquina = null)
    {
        _http = http;
        _projeto = projeto;
        _apiKey = apiKey;
        _obterCodigoMaquina = obterCodigoMaquina ?? Services.LicencaService.CodigoDaMaquina;
    }

    private string Url(string chave, string? mascara = null) =>
        $"https://firestore.googleapis.com/v1/projects/{_projeto}/databases/(default)/documents/licencas/{Uri.EscapeDataString(chave)}?key={_apiKey}{mascara}";

    private string UrlDispositivo(string docId, string? mascara = null) =>
        $"https://firestore.googleapis.com/v1/projects/{_projeto}/databases/(default)/documents/dispositivos/{Uri.EscapeDataString(docId)}?key={_apiKey}{mascara}";

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

        // Registra também os metadados do computador na coleção "dispositivos" (melhor esforço: se as regras antigas recusarem, não falha a ativação).
        await SincronizarDocumentoDispositivoAsync(chave, _obterCodigoMaquina(), ativo: true, ct).ConfigureAwait(false);
    }

    public async Task DesvincularMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
    {
        try
        {
            var doc = await ObterAsync(chave, ct).ConfigureAwait(false);
            if (doc is not null && !string.IsNullOrWhiteSpace(doc.Maquina) && doc.Maquina.Trim() != "*")
            {
                var restantes = ServicoLicencaNuvem.ExtrairMaquinas(doc.Maquina)
                    .Where(m => !string.Equals(m, maquina, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var novoValor = string.Join(",", restantes);

                var corpo = JsonSerializer.Serialize(new { fields = new { maquina = new { stringValue = novoValor } } });
                using var pedido = new HttpRequestMessage(HttpMethod.Patch, Url(chave, "&updateMask.fieldPaths=maquina"))
                { Content = new StringContent(corpo, Encoding.UTF8, "application/json") };
                using var _ = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
            }

            await SincronizarDocumentoDispositivoAsync(chave, maquina, ativo: false, ct).ConfigureAwait(false);
        }
        catch
        {
            // Se o Firestore restringir escrita direta ou estiver sem internet, não impede a remoção local.
        }
    }

    private async Task SincronizarDocumentoDispositivoAsync(string chave, string hwid, bool ativo, CancellationToken ct)
    {
        try
        {
            var docId = $"{chave}_{hwid}";
            var agoraIso = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            var corpo = JsonSerializer.Serialize(new
            {
                fields = new
                {
                    licencaId = new { stringValue = chave },
                    identificadorDispositivo = new { stringValue = hwid },
                    nomeDispositivo = new { stringValue = Environment.MachineName },
                    sistema = new { stringValue = RuntimeInformation.OSDescription },
                    ativo = new { booleanValue = ativo },
                    ultimaVerificacao = new { timestampValue = agoraIso }
                }
            });
            var mascara = "&updateMask.fieldPaths=licencaId&updateMask.fieldPaths=identificadorDispositivo&updateMask.fieldPaths=nomeDispositivo&updateMask.fieldPaths=sistema&updateMask.fieldPaths=ativo&updateMask.fieldPaths=ultimaVerificacao";
            using var pedido = new HttpRequestMessage(HttpMethod.Patch, UrlDispositivo(docId, mascara))
            {
                Content = new StringContent(corpo, Encoding.UTF8, "application/json")
            };
            using var _ = await _http.SendAsync(pedido, ct).ConfigureAwait(false);
        }
        catch
        {
            // Silencioso caso o projeto ainda esteja com regras antigas sem permissão em /dispositivos
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
            int? Inteiro(string nome)
            {
                if (!campos.TryGetProperty(nome, out var c)) return null;
                if (c.TryGetProperty("integerValue", out var i) && int.TryParse(i.GetString(), out var v)) return v;
                if (c.TryGetProperty("doubleValue", out var d)) return (int)d.GetDouble();
                return null;
            }
            var status = Texto("status")?.ToUpperInvariant();
            var ativa = true;
            if (campos.TryGetProperty("ativa", out var a) && a.TryGetProperty("booleanValue", out var b))
                ativa = b.GetBoolean();
            else if (status is "BLOQUEADA" or "CANCELADA")
                ativa = false;

            var cliente = Texto("clienteNome") ?? Texto("cliente") ?? "";
            var empresa = Texto("empresaNome") ?? Texto("empresa");
            var validade = Texto("dataVencimento") ?? Texto("validade");
            var inicio = Texto("dataInicio");
            var plano = Texto("plano");
            var maquina = Texto("maquina");
            var limite = Inteiro("limiteDispositivos");
            var maquinasVinculadas = ServicoLicencaNuvem.ExtrairMaquinas(maquina);
            var usados = Inteiro("dispositivosAtivosCount") ?? (maquinasVinculadas.Count > 0 ? maquinasVinculadas.Count : null);
            var motivo = Texto("motivoBloqueio") ?? Texto("motivoCancelamento");

            return new DocumentoLicenca(cliente, validade, ativa, maquina, plano, empresa, inicio, usados, limite, motivo);
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
            hostname = Environment.MachineName,
            osInfo = RuntimeInformation.OSDescription,
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
                var empresa = root.TryGetProperty("empresa", out var emp) ? emp.GetString() : null;
                var plano = root.TryGetProperty("plano", out var pl) ? pl.GetString() : null;
                var inicio = root.TryGetProperty("dataInicio", out var di) ? di.GetString() : null;
                var vencimento = root.TryGetProperty("dataVencimento", out var v) ? v.GetString() : null;
                var dispAutorizado = root.TryGetProperty("dispositivoAutorizado", out var da) ? da.GetString() : maquina;
                int? usados = root.TryGetProperty("dispositivosUsados", out var du) && du.ValueKind == JsonValueKind.Number
                    ? du.GetInt32()
                    : (root.TryGetProperty("dispositivosAtivos", out var daCount) && daCount.ValueKind == JsonValueKind.Number ? daCount.GetInt32() : null);
                int? limite = root.TryGetProperty("limiteDispositivos", out var ld) && ld.ValueKind == JsonValueKind.Number ? ld.GetInt32() : null;
                var mensagem = root.TryGetProperty("mensagem", out var msg) ? msg.GetString() : null;

                if (situacao == "NAO_ENCONTRADA") return null;

                // Vencida: quem decide é a data (mostra "venceu em ..."), não o bloqueio.
                if (situacao == "VENCIDA" && vencimento is not null)
                    return new DocumentoLicenca(cliente, vencimento, Ativa: true, Maquina: dispAutorizado ?? maquina, Plano: plano, Empresa: empresa, DataInicio: inicio, DispositivosUsados: usados, LimiteDispositivos: limite);

                if (situacao == "BLOQUEADA" || situacao == "CANCELADA")
                {
                    return new DocumentoLicenca(cliente, vencimento, Ativa: false, Maquina: maquina, Plano: plano, Empresa: empresa, DataInicio: inicio, DispositivosUsados: usados, LimiteDispositivos: limite, MotivoBloqueio: mensagem);
                }

                if (situacao == "LIMITE_ATINGIDO")
                {
                    return new DocumentoLicenca(cliente, vencimento, Ativa: true, Maquina: "OUTRO_COMPUTADOR", Plano: plano, Empresa: empresa, DataInicio: inicio, DispositivosUsados: usados, LimiteDispositivos: limite);
                }

                return new DocumentoLicenca(cliente, vencimento, Ativa: autorizado, Maquina: dispAutorizado ?? maquina, Plano: plano, Empresa: empresa, DataInicio: inicio, DispositivosUsados: usados, LimiteDispositivos: limite);
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

    public async Task DesvincularMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                acao = "desvincular",
                chave = chave.Trim(),
                dispositivoId = maquina
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };
            using var _ = await _http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch
        {
            // Se estiver sem internet, prossegue com a remoção local.
        }
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

    public async Task DesvincularMaquinaAsync(string chave, string maquina, CancellationToken ct = default)
    {
        await _principal.DesvincularMaquinaAsync(chave, maquina, ct).ConfigureAwait(false);
        await _reserva.DesvincularMaquinaAsync(chave, maquina, ct).ConfigureAwait(false);
    }
}

/// <summary>Guarda a última licença validada, com assinatura HMAC anti-adulteração e controle de relógio.</summary>
public class LicencaNuvemSalva
{
    public string Chave { get; set; } = "";
    public string Cliente { get; set; } = "";
    public string? Empresa { get; set; }
    public string? Plano { get; set; }
    public string? DataInicio { get; set; }
    public string Validade { get; set; } = "";   // yyyy-MM-dd
    public string Maquina { get; set; } = "";
    public int? DispositivosUsados { get; set; }
    public int? LimiteDispositivos { get; set; }
    public DateTime VerificadaEm { get; set; }
    public DateTime UltimoRelogio { get; set; }
    public string Assinatura { get; set; } = "";
}

/// <summary>
/// Regras da licença por nuvem: a pessoa digita a chave que recebeu do site; o programa lê o documento no Firebase,
/// confere se está ativa, se não venceu e se é deste computador, e guarda o resultado assinado. Sem internet vale a última
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

    /// <summary>Retorna a chave atualmente salva neste computador (ou vazio se não houver).</summary>
    public string? ChaveSalva => Carregar()?.Chave;

    /// <summary>Extrai a lista de HWIDs vinculados a partir do campo maquina (suporta múltiplos separados por vírgula).</summary>
    public static IReadOnlyList<string> ExtrairMaquinas(string? campoMaquina)
    {
        if (string.IsNullOrWhiteSpace(campoMaquina)) return Array.Empty<string>();
        var limpo = campoMaquina.Trim();
        if (limpo == "*") return new[] { "*" };
        return limpo
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>Primeira ativação (ou troca de chave): lê no Firebase, prende ao computador se houver vaga e salva.</summary>
    public async Task<ResultadoLicencaNuvem> AtivarAsync(string chave, CancellationToken ct = default)
    {
        chave = (chave ?? "").Trim().ToUpperInvariant();
        if (!PadraoChave.IsMatch(chave))
            return new(EstadoLicencaNuvem.ChaveInvalida, chave, "", null, null, "Chave inválida: use de 6 a 64 letras, números ou hífens.");
        try
        {
            var doc = await _fonte.ObterAsync(chave, ct).ConfigureAwait(false);
            if (doc is null)
                return new(EstadoLicencaNuvem.NaoEncontrada, chave, "", null, null, "Licença não encontrada. Confira a chave digitada.");

            var aqui = _maquina();
            var maquinas = ExtrairMaquinas(doc.Maquina);
            var curinga = maquinas.Contains("*", StringComparer.OrdinalIgnoreCase);
            var jaVinculada = curinga || maquinas.Contains(aqui, StringComparer.OrdinalIgnoreCase);
            var limite = Math.Max(1, doc.LimiteDispositivos ?? 1);
            var temVaga = !jaVinculada && maquinas.Count < limite;

            string maquinaEfetiva;
            int usadosEfetivos;
            if (curinga)
            {
                maquinaEfetiva = "*";
                usadosEfetivos = doc.DispositivosUsados ?? 1;
            }
            else if (jaVinculada)
            {
                maquinaEfetiva = aqui;
                usadosEfetivos = doc.DispositivosUsados ?? maquinas.Count;
            }
            else if (temVaga)
            {
                maquinaEfetiva = aqui;
                usadosEfetivos = maquinas.Count + 1;
            }
            else
            {
                maquinaEfetiva = "OUTRO_COMPUTADOR";
                usadosEfetivos = doc.DispositivosUsados ?? maquinas.Count;
            }

            var docAvaliado = doc with { DispositivosUsados = usadosEfetivos, LimiteDispositivos = limite };
            var r = Avaliar(chave, docAvaliado, maquinaEfetiva, aqui);

            if (r.Ativa && temVaga)
            {
                var novaLista = maquinas.Concat(new[] { aqui }).ToArray();
                var novoCampoMaquina = string.Join(",", novaLista);
                await _fonte.RegistrarMaquinaAsync(chave, novoCampoMaquina, ct).ConfigureAwait(false);
            }
            if (r.Ativa)
            {
                Salvar(chave, docAvaliado, curinga ? "*" : aqui);
            }
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
            {
                Remover();
                return new(EstadoLicencaNuvem.NaoEncontrada, salva.Chave, salva.Cliente, null, null, "Esta licença não existe mais no sistema.");
            }

            var aqui = _maquina();
            var maquinas = ExtrairMaquinas(doc.Maquina);
            var curinga = maquinas.Contains("*", StringComparer.OrdinalIgnoreCase);
            var autorizadaAqui = curinga || maquinas.Contains(aqui, StringComparer.OrdinalIgnoreCase);

            // Se o documento tem máquinas registradas e esta máquina não está lá (ou se o admin limpou as máquinas vinculadas),
            // significa que este computador foi desvinculado no painel administrativo.
            string maquinaEfetiva;
            if (curinga)
            {
                maquinaEfetiva = "*";
            }
            else if (autorizadaAqui)
            {
                maquinaEfetiva = aqui;
            }
            else if (maquinas.Count == 0)
            {
                Remover();
                return new(EstadoLicencaNuvem.NaoEncontrada, salva.Chave, doc.Cliente, null, null,
                    "Este computador foi desvinculado desta licença pelo administrador. Clique em 'Ativar / Validar Licença' para vincular novamente.");
            }
            else
            {
                maquinaEfetiva = "OUTRO_COMPUTADOR";
            }

            var r = Avaliar(salva.Chave, doc, maquinaEfetiva, aqui);
            if (r.Ativa)
            {
                Salvar(salva.Chave, doc, maquinaEfetiva);
            }
            else
            {
                // Licença bloqueada, vencida ou vinculada a outro computador no servidor: invalida o cache local.
                Remover();
            }
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

        var agora = _agora();
        // Proteção contra atraso manual do relógio do Windows (Clock Rollback)
        var referenciaRelogio = s.UltimoRelogio > s.VerificadaEm ? s.UltimoRelogio : s.VerificadaEm;
        if (agora < referenciaRelogio.AddHours(-2))
        {
            return new(EstadoLicencaNuvem.SemConexao, s.Chave, s.Cliente, null, null,
                "Foi detectada uma alteração no relógio do computador. Conecte à internet para revalidar a licença.", Offline: true);
        }

        if (porFaltaDeConexao && (agora - s.VerificadaEm).TotalDays > DiasOffline)
            return new(EstadoLicencaNuvem.SemConexao, s.Chave, s.Cliente, null, null,
                $"Sem internet há mais de {DiasOffline} dias: conecte para validar a licença.", Offline: true);
        if (!DateTime.TryParseExact(s.Validade, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var validade))
            return new(EstadoLicencaNuvem.ChaveInvalida, s.Chave, s.Cliente, null, null, "Licença salva com validade ilegível.");

        DateTime? inicio = TentarValidade(s.DataInicio, out var di) ? di : null;
        var r = PorValidade(s.Chave, s.Cliente, validade, s.Empresa, s.Plano, s.DispositivosUsados, s.LimiteDispositivos, inicio, s.VerificadaEm);
        return r with { Offline = true, Mensagem = r.Mensagem + (porFaltaDeConexao ? " (sem internet: última verificação)" : "") };
    }

    /// <summary>Remove a licença local e tenta liberar a vaga deste computador na nuvem.</summary>
    public async Task RemoverAsync(CancellationToken ct = default)
    {
        var salva = Carregar();
        if (salva is not null && !string.IsNullOrWhiteSpace(salva.Chave))
        {
            try
            {
                await _fonte.DesvincularMaquinaAsync(salva.Chave, _maquina(), ct).ConfigureAwait(false);
            }
            catch
            {
                // Segue para remoção local mesmo sem conexão
            }
        }
        Remover();
    }

    public void Remover()
    {
        if (File.Exists(_caminho)) File.Delete(_caminho);
    }

    private ResultadoLicencaNuvem Avaliar(string chave, DocumentoLicenca doc, string maquinaDoc, string aqui)
    {
        DateTime? inicio = TentarValidade(doc.DataInicio, out var di) ? di : null;
        if (!doc.Ativa)
        {
            var detalhe = !string.IsNullOrWhiteSpace(doc.MotivoBloqueio) ? doc.MotivoBloqueio : "Esta licença foi bloqueada ou cancelada. Fale com o fornecedor.";
            return new(EstadoLicencaNuvem.Bloqueada, chave, doc.Cliente, null, null, detalhe,
                Empresa: doc.Empresa, Plano: doc.Plano, DispositivosUsados: doc.DispositivosUsados, LimiteDispositivos: doc.LimiteDispositivos, DataInicio: inicio, VerificadaEm: _agora());
        }
        if (maquinaDoc != "*" && !string.Equals(maquinaDoc, aqui, StringComparison.OrdinalIgnoreCase))
        {
            var infoLimite = doc.LimiteDispositivos is > 1
                ? $"Limite de {doc.LimiteDispositivos} computadores atingido para esta licença."
                : "Esta licença já está em uso em outro computador.";
            return new(EstadoLicencaNuvem.OutraMaquina, chave, doc.Cliente, null, null, infoLimite,
                Empresa: doc.Empresa, Plano: doc.Plano, DispositivosUsados: doc.DispositivosUsados, LimiteDispositivos: doc.LimiteDispositivos, DataInicio: inicio, VerificadaEm: _agora());
        }
        if (!TentarValidade(doc.Validade, out var validade))
            return new(EstadoLicencaNuvem.ChaveInvalida, chave, doc.Cliente, null, null, "A licença não tem uma validade válida no sistema.");
        return PorValidade(chave, doc.Cliente, validade, doc.Empresa, doc.Plano, doc.DispositivosUsados, doc.LimiteDispositivos, inicio, _agora());
    }

    private ResultadoLicencaNuvem PorValidade(
        string chave,
        string cliente,
        DateTime validade,
        string? empresa = null,
        string? plano = null,
        int? usados = null,
        int? limite = null,
        DateTime? inicio = null,
        DateTime? verificadaEm = null)
    {
        var dias = (int)(validade.Date - _agora().Date).TotalDays;
        if (dias < 0)
            return new(EstadoLicencaNuvem.Vencida, chave, cliente, validade, dias, $"Licença de {cliente} venceu em {validade:dd/MM/yyyy}.",
                Empresa: empresa, Plano: plano, DispositivosUsados: usados, LimiteDispositivos: limite, DataInicio: inicio, VerificadaEm: verificadaEm);
        if (dias <= DiasDeAviso)
            return new(EstadoLicencaNuvem.Vencendo, chave, cliente, validade, dias, $"Licença de {cliente} vence em {dias} dia(s) ({validade:dd/MM/yyyy}).",
                Empresa: empresa, Plano: plano, DispositivosUsados: usados, LimiteDispositivos: limite, DataInicio: inicio, VerificadaEm: verificadaEm);
        return new(EstadoLicencaNuvem.Valida, chave, cliente, validade, dias, $"Licença de {cliente}, válida até {validade:dd/MM/yyyy}.",
            Empresa: empresa, Plano: plano, DispositivosUsados: usados, LimiteDispositivos: limite, DataInicio: inicio, VerificadaEm: verificadaEm);
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

    private string CalcularAssinatura(LicencaNuvemSalva s)
    {
        var segredo = Encoding.UTF8.GetBytes($"NEXUSLS_CACHE_V2|{_maquina()}|9F8B7C6D5E4A");
        var dados = $"{s.Chave}|{s.Cliente}|{s.Empresa}|{s.Plano}|{s.Validade}|{s.Maquina}|{s.VerificadaEm.Ticks}";
        var hash = HMACSHA256.HashData(segredo, Encoding.UTF8.GetBytes(dados));
        return Convert.ToBase64String(hash);
    }

    private void Salvar(string chave, DocumentoLicenca doc, string maquina)
    {
        TentarValidade(doc.Validade, out var v);
        var agora = _agora();
        var s = new LicencaNuvemSalva
        {
            Chave = chave,
            Cliente = doc.Cliente,
            Empresa = doc.Empresa,
            Plano = doc.Plano,
            DataInicio = doc.DataInicio,
            Validade = v.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Maquina = maquina,
            DispositivosUsados = doc.DispositivosUsados,
            LimiteDispositivos = doc.LimiteDispositivos,
            VerificadaEm = agora,
            UltimoRelogio = agora
        };
        s.Assinatura = CalcularAssinatura(s);
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }

    private LicencaNuvemSalva? Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return null;
            var s = JsonSerializer.Deserialize<LicencaNuvemSalva>(File.ReadAllText(_caminho));
            if (s is null) return null;

            // Se o arquivo foi editado manualmente no Bloco de Notas, a assinatura HMAC não bate e o cache é descartado.
            if (!string.IsNullOrEmpty(s.Assinatura))
            {
                var esperada = CalcularAssinatura(s);
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(s.Assinatura), Encoding.UTF8.GetBytes(esperada)))
                {
                    return null;
                }
            }
            else
            {
                // Migração transparente: se era um arquivo antigo sem assinatura, assina na primeira leitura válida.
                s.Assinatura = CalcularAssinatura(s);
            }

            return s;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
