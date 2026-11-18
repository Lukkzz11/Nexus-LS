using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace NexusLS.Movel;

/// <summary>O que fica guardado para o celular: o PIN de acesso e a porta do servidor.</summary>
public sealed class PreferenciasMovel
{
    public string Pin { get; set; } = "";
    public int Porta { get; set; } = ServidorMovel.PortaPadrao;
}

/// <summary>
/// Guarda o PIN do celular em <c>%LOCALAPPDATA%\NexusLS\movel.json</c>. O PIN e gerado sozinho
/// na primeira abertura e nunca muda sem pedido, para o celular da loja continuar entrando.
/// </summary>
public static class PinMovel
{
    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLS",
        "movel.json");

    public static PreferenciasMovel CarregarOuCriar(string? caminho = null)
    {
        caminho ??= CaminhoPadrao();

        var preferencias = new PreferenciasMovel();
        try
        {
            if (File.Exists(caminho))
            {
                var json = File.ReadAllText(caminho);
                var lido = System.Text.Json.JsonSerializer.Deserialize<PreferenciasMovel>(json);
                if (lido is not null)
                {
                    preferencias = lido;
                }
            }
        }
        catch (Exception ex)
        {
            Core.Diagnostico.RegistroDeLog.Aviso($"Nexus Movel: nao foi possivel ler {caminho}: {ex.Message}");
        }

        if (!PinValido(preferencias.Pin))
        {
            preferencias.Pin = Gerar();
            preferencias.Porta = preferencias.Porta is > 0 and < 65536 ? preferencias.Porta : ServidorMovel.PortaPadrao;
            Salvar(preferencias, caminho);
        }

        return preferencias;
    }

    public static void Salvar(PreferenciasMovel preferencias, string? caminho = null)
    {
        caminho ??= CaminhoPadrao();

        try
        {
            var pasta = Path.GetDirectoryName(caminho);
            if (!string.IsNullOrWhiteSpace(pasta)) Directory.CreateDirectory(pasta);

            var json = System.Text.Json.JsonSerializer.Serialize(preferencias, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            var temporario = caminho + ".tmp";
            File.WriteAllText(temporario, json);
            File.Move(temporario, caminho, overwrite: true);
        }
        catch (Exception ex)
        {
            Core.Diagnostico.RegistroDeLog.Erro($"Nexus Movel: nao foi possivel salvar {caminho}.", ex);
        }
    }

    public static string Gerar() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);

    public static bool PinValido(string? pin) =>
        !string.IsNullOrWhiteSpace(pin) && pin.Length == 6 && pin.All(char.IsAsciiDigit);
}

/// <summary>
/// Confere o PIN que o celular manda. Compara em tempo constante e segura a porta depois de
/// varias tentativas erradas: um PIN de 6 numeros na rede da loja ainda merece cuidado.
/// </summary>
public sealed class AutenticacaoMovel
{
    private const int MaximoFalhas = 8;
    private static readonly TimeSpan TempoDeBloqueio = TimeSpan.FromSeconds(60);

    private sealed class Tentativas
    {
        public int Falhas;
        public DateTime BloqueadoAte;
    }

    private readonly string _pin;
    private readonly Dictionary<string, Tentativas> _porOrigem = new(StringComparer.Ordinal);
    private readonly object _trava = new();

    public AutenticacaoMovel(string pin) => _pin = pin;

    public bool Conferir(string? informado, string? origem)
    {
        var chave = string.IsNullOrWhiteSpace(origem) ? "?" : origem;

        lock (_trava)
        {
            if (_porOrigem.TryGetValue(chave, out var tentativas) && tentativas.BloqueadoAte > DateTime.UtcNow)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(informado) && Iguais(_pin, informado))
            {
                _porOrigem.Remove(chave);
                return true;
            }

            if (!_porOrigem.TryGetValue(chave, out tentativas))
            {
                tentativas = new Tentativas();
                _porOrigem[chave] = tentativas;
            }

            tentativas.Falhas++;
            if (tentativas.Falhas >= MaximoFalhas)
            {
                tentativas.Falhas = 0;
                tentativas.BloqueadoAte = DateTime.UtcNow + TempoDeBloqueio;
            }

            return false;
        }
    }

    public bool Bloqueado(string? origem)
    {
        var chave = string.IsNullOrWhiteSpace(origem) ? "?" : origem;
        lock (_trava)
        {
            return _porOrigem.TryGetValue(chave, out var t) && t.BloqueadoAte > DateTime.UtcNow;
        }
    }

    private static bool Iguais(string esperado, string informado)
    {
        var a = Encoding.UTF8.GetBytes(esperado);
        var b = Encoding.UTF8.GetBytes(informado);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
