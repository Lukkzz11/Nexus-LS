using System.Globalization;
using System.Text.Json;

namespace NexusLS.Core.Seguranca;

/// <summary>
/// Onde ficam as tentativas erradas de PIN. Fica separado do serviço para o bloqueio
/// sobreviver a um reinício do programa (a versão antiga guardava só na memória).
/// </summary>
public interface ITentativasLoginStore
{
    /// <summary>Há bloqueio ativo para o funcionário nesta hora? Devolve também até quando vai.</summary>
    bool EstaBloqueado(int funcionarioId, DateTime agora, out DateTime bloqueadoAte);

    /// <summary>
    /// Conta mais uma falha. Ao chegar em <see cref="TentativasLoginStore.MaxFalhas"/>, grava o bloqueio
    /// e zera o contador. Devolve quantas falhas seguidas ficaram registradas (0 quando o bloqueio começou).
    /// </summary>
    int RegistrarFalha(int funcionarioId, DateTime agora);

    /// <summary>Zera as falhas e o bloqueio (PIN certo, PIN redefinido, funcionário recriado).</summary>
    void Limpar(int funcionarioId);
}

/// <summary>
/// Bloqueio de PIN persistido em JSON (<c>%LOCALAPPDATA%\NexusLS\seguranca\tentativas.json</c>).
/// Escrita atômica (arquivo .tmp + Move), tolerante a arquivo corrompido e segura entre threads.
/// Nunca guarda o PIN: só o id do funcionário, a contagem e a hora do bloqueio.
/// </summary>
public sealed class TentativasLoginStore : ITentativasLoginStore
{
    /// <summary>5 erros seguidos bloqueiam, como manda a regra 11 do PRESERVAR.md.</summary>
    public const int MaxFalhas = 5;

    /// <summary>Bloqueio de 30 segundos, como manda a regra 11 do PRESERVAR.md.</summary>
    public static readonly TimeSpan TempoDeBloqueio = TimeSpan.FromSeconds(30);

    private static readonly JsonSerializerOptions Formato = new() { WriteIndented = true };

    private readonly string _caminho;
    private readonly object _trava = new();

    /// <summary>
    /// Cópia em memória do estado (carregada uma vez). Serve de rede de segurança: se o disco estiver
    /// cheio e a gravação falhar, o bloqueio ainda vale nesta execução do programa.
    /// </summary>
    private Dictionary<string, EstadoTentativas>? _cache;

    /// <param name="caminho">
    /// Arquivo JSON. Nulo/vazio usa <see cref="CaminhoPadrao"/>. Os testes injetam um caminho temporário
    /// para não mexer no perfil real do usuário.
    /// </param>
    public TentativasLoginStore(string? caminho = null) =>
        _caminho = string.IsNullOrWhiteSpace(caminho) ? CaminhoPadrao() : caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexusLS", "seguranca", "tentativas.json");

    public bool EstaBloqueado(int funcionarioId, DateTime agora, out DateTime bloqueadoAte)
    {
        bloqueadoAte = default;
        lock (_trava)
        {
            var estado = Estado();
            var chave = Chave(funcionarioId);
            if (!estado.TryGetValue(chave, out var item) || item.BloqueadoAte is not { } ate)
                return false;

            if (ate > agora)
            {
                bloqueadoAte = ate;
                return true;
            }

            // Passou o bloqueio: zera a contagem para a próxima sequência começar do zero.
            estado.Remove(chave);
            Gravar(estado);
            return false;
        }
    }

    public int RegistrarFalha(int funcionarioId, DateTime agora)
    {
        lock (_trava)
        {
            var estado = Estado();
            var chave = Chave(funcionarioId);
            estado.TryGetValue(chave, out var item);
            item ??= new EstadoTentativas();

            // Se ainda está bloqueado, uma nova tentativa (feita por fora do serviço) não conta.
            if (item.BloqueadoAte is { } ativo && ativo > agora) return 0;

            if (item.BloqueadoAte is not null)
            {
                item.Falhas = 0;
                item.BloqueadoAte = null;
            }

            item.Falhas++;
            if (item.Falhas >= MaxFalhas)
            {
                item.Falhas = 0;
                item.BloqueadoAte = agora + TempoDeBloqueio;
            }

            estado[chave] = item;
            Gravar(estado);
            return item.Falhas;
        }
    }

    public void Limpar(int funcionarioId)
    {
        lock (_trava)
        {
            var estado = Estado();
            if (estado.Remove(Chave(funcionarioId))) Gravar(estado);
        }
    }

    /// <summary>Id como texto: o JSON fica legível e não depende do conversor de chaves do System.Text.Json.</summary>
    private static string Chave(int funcionarioId) => funcionarioId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Carrega do disco só na primeira vez; depois trabalha na cópia em memória.</summary>
    private Dictionary<string, EstadoTentativas> Estado() => _cache ??= Carregar();

    private Dictionary<string, EstadoTentativas> Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            var dados = JsonSerializer.Deserialize<Dictionary<string, EstadoTentativas>>(File.ReadAllText(_caminho), Formato);
            return dados ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Arquivo corrompido (ou ilegível): recomeça vazio. Não dá para herdar um bloqueio de lixo.
            return new();
        }
    }

    private void Gravar(Dictionary<string, EstadoTentativas> estado)
    {
        try
        {
            var pasta = Path.GetDirectoryName(_caminho);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

            // Escrita atômica: se o programa cair no meio, o arquivo antigo continua válido.
            var temporario = _caminho + ".tmp";
            File.WriteAllText(temporario, JsonSerializer.Serialize(estado, Formato));
            File.Move(temporario, _caminho, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            // Disco cheio ou pasta sem permissão: não derruba o login. O bloqueio em memória da
            // chamada atual já foi aplicado; na pior das hipóteses ele não sobrevive ao reinício.
        }
    }

    private sealed class EstadoTentativas
    {
        public int Falhas { get; set; }
        public DateTime? BloqueadoAte { get; set; }
    }
}
