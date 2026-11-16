using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusLS.BarTender.Models.Designer;

public partial class ModeloEtiquetaDesign : ObservableObject
{
    [ObservableProperty]
    private string _nome = "Modelo 50x30 mm";

    [ObservableProperty]
    private double _larguraMm = 50.0;

    [ObservableProperty]
    private double _alturaMm = 30.0;

    public ObservableCollection<ElementoDesign> Elementos { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public void Salvar(string caminho)
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        var dir = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Grava em arquivo temporário e só então troca: uma queda de energia no meio
        // não deixa o modelo truncado, e o anterior fica guardado em ".bak".
        var temporario = caminho + ".tmp";
        try
        {
            File.WriteAllText(temporario, json);

            if (File.Exists(caminho))
            {
                File.Replace(temporario, caminho, caminho + ".bak", ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporario, caminho);
            }
        }
        finally
        {
            if (File.Exists(temporario))
            {
                try { File.Delete(temporario); } catch (Exception exLog) { NexusLS.BarTender.App.Log($"ModeloEtiquetaDesign #1: {exLog.Message}"); }
            }
        }
    }

    public static ModeloEtiquetaDesign Carregar(string caminho)
    {
        if (!File.Exists(caminho))
        {
            throw new FileNotFoundException("Arquivo de modelo não encontrado.", caminho);
        }

        var json = File.ReadAllText(caminho);
        var modelo = JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, JsonOpts);
        return modelo ?? CriarPadrao50x30();
    }
}
