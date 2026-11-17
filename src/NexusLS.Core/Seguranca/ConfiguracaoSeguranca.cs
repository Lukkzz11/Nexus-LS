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

    public void Validar()
    {
        if (MinutosInatividade < 0 || MinutosInatividade > InatividadeMaximaMinutos)
            throw new RegraDeNegocioException($"O bloqueio por inatividade deve ficar entre 0 (desligado) e {InatividadeMaximaMinutos} minutos.");
    }
}

public static class RegrasSeguranca
{
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
            return JsonSerializer.Deserialize<ConfiguracaoSeguranca>(File.ReadAllText(_caminho)) ?? new();
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
