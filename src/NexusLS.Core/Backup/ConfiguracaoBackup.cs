using System.Text.Json;
using NexusLS.Core.Services;

namespace NexusLS.Core.Backup;

/// <summary>
/// Preferências do backup. Ficam num arquivo JSON ao lado do banco (e não dentro dele), para o backup
/// funcionar mesmo se o banco estiver com problema.
/// </summary>
public class ConfiguracaoBackup
{
    public const int ManterMinimo = 3;
    public const int ManterMaximo = 200;
    public const int IntervaloMaximoHoras = 24 * 30;

    /// <summary>Pasta onde os backups são gravados.</summary>
    public string Pasta { get; set; } = PastaPadrao();

    /// <summary>De quantas em quantas horas o backup automático roda. 0 = desligado.</summary>
    public int IntervaloHoras { get; set; } = 24;

    /// <summary>Quantos backups comuns (manual e automático) ficam guardados; os mais antigos são apagados.</summary>
    public int Manter { get; set; } = 20;

    /// <summary>Horário local do último backup concluído (qualquer motivo comum).</summary>
    public DateTime? UltimoBackup { get; set; }

    public static string PastaPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nexus LS", "Backups");

    public void Validar()
    {
        if (string.IsNullOrWhiteSpace(Pasta))
            throw new RegraDeNegocioException("Escolha a pasta dos backups.");
        if (IntervaloHoras < 0 || IntervaloHoras > IntervaloMaximoHoras)
            throw new RegraDeNegocioException($"O intervalo do backup deve ficar entre 0 (desligado) e {IntervaloMaximoHoras} horas.");
        if (Manter < ManterMinimo || Manter > ManterMaximo)
            throw new RegraDeNegocioException($"Guarde entre {ManterMinimo} e {ManterMaximo} backups.");
    }
}

/// <summary>Lê e grava a <see cref="ConfiguracaoBackup"/>. Arquivo ausente ou estragado vira o padrão.</summary>
public class ConfiguracaoBackupStore
{
    private readonly string _caminho;

    public ConfiguracaoBackupStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "backup.json");

    public ConfiguracaoBackup Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<ConfiguracaoBackup>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Salvar(ConfiguracaoBackup configuracao)
    {
        configuracao.Validar();
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        // Grava num arquivo temporário e troca: uma queda no meio não deixa o JSON pela metade.
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(configuracao, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}
