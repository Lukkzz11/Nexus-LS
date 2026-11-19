using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NexusLS.Core.Gaveta;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma opção do filtro de categoria da gaveta (Todas, Cupons, Relatórios...).</summary>
public sealed record OpcaoCategoriaGaveta(string Texto, CategoriaGaveta? Categoria);

/// <summary>Uma linha da lista da gaveta.</summary>
public sealed record ItemGaveta(ArquivoGaveta Arquivo)
{
    public string Nome => Arquivo.Nome;
    public string Categoria => GavetaService.Rotulo(Arquivo.Categoria);
    public string Data => Arquivo.Data.ToString("dd/MM/yyyy HH:mm");
    public string Tamanho => Arquivo.Bytes >= 1_048_576 ? $"{Arquivo.Bytes / 1_048_576.0:0.0} MB" : $"{Math.Max(1, Arquivo.Bytes / 1024)} KB";
}

/// <summary>
/// A gaveta da Gestão: onde ficam guardados no computador os PDFs e documentos da loja (cupons, relatórios, notas fiscais,
/// comprovantes). Lista, busca, abre, importa e exclui. Entra no backup completo junto com o resto dos dados.
/// </summary>
public partial class GavetaViewModel : ViewModelBase
{
    private readonly GavetaService _gaveta;

    public GavetaViewModel(GavetaService gaveta)
    {
        _gaveta = gaveta;
        Categorias = new[] { new OpcaoCategoriaGaveta("Todas", null) }
            .Concat(Enum.GetValues<CategoriaGaveta>().Select(c => new OpcaoCategoriaGaveta(GavetaService.Rotulo(c), c))).ToList();
        _categoriaFiltro = Categorias[0];
        _categoriaImportar = Categorias[^1];
        Atualizar();
    }

    public IReadOnlyList<OpcaoCategoriaGaveta> Categorias { get; }
    public IEnumerable<OpcaoCategoriaGaveta> CategoriasParaImportar => Categorias.Skip(1);
    public ObservableCollection<ItemGaveta> Arquivos { get; } = new();

    [ObservableProperty] private OpcaoCategoriaGaveta _categoriaFiltro;
    [ObservableProperty] private OpcaoCategoriaGaveta _categoriaImportar;
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private ItemGaveta? _selecionado;
    [ObservableProperty] private string _resumo = "";

    public string PastaTexto => _gaveta.Pasta;

    /// <summary>Quem mostra a janela de escolher arquivo (nulo = cancelou). Trocável nos testes.</summary>
    public Func<string?> EscolherArquivo { get; set; } = () =>
    {
        var janela = new OpenFileDialog { Title = "Guardar na gaveta", CheckFileExists = true, Multiselect = false };
        return janela.ShowDialog() == true ? janela.FileName : null;
    };

    public Func<string, bool> Confirmar { get; set; } = texto =>
        MessageBox.Show(texto, "Nexus", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public Action<string> AbrirArquivo { get; set; } = caminho =>
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });

    public Action<string> MostrarNaPasta { get; set; } = caminho =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{caminho}\"") { UseShellExecute = true });

    /// <summary>Preenchido pela tela quando o envio por WhatsApp está disponível: recebe o arquivo escolhido.</summary>
    public Func<ArquivoGaveta, Task>? EnviarPorWhatsApp { get; set; }
    public bool TemWhatsApp => EnviarPorWhatsApp is not null;

    partial void OnCategoriaFiltroChanged(OpcaoCategoriaGaveta value) => Atualizar();
    partial void OnBuscaChanged(string value) => Atualizar();

    [RelayCommand]
    public void Atualizar()
    {
        Arquivos.Clear();
        foreach (var a in _gaveta.Listar(CategoriaFiltro.Categoria, Busca)) Arquivos.Add(new ItemGaveta(a));
        var total = Arquivos.Sum(i => i.Arquivo.Bytes);
        Resumo = Arquivos.Count == 0
            ? "Nenhum documento guardado ainda."
            : $"{Arquivos.Count} documento(s) · {(total >= 1_048_576 ? $"{total / 1_048_576.0:0.0} MB" : $"{Math.Max(1, total / 1024)} KB")}";
    }

    [RelayCommand]
    private Task ImportarAsync() => ExecutarAsync(() =>
    {
        var origem = EscolherArquivo();
        if (string.IsNullOrWhiteSpace(origem)) return Task.CompletedTask;
        _gaveta.Importar(CategoriaImportar.Categoria ?? CategoriaGaveta.Outros, origem);
        Atualizar();
        MostrarSucesso("Documento guardado na gaveta.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task AbrirAsync() => ExecutarAsync(() =>
    {
        if (Selecionado is { } s) AbrirArquivo(s.Arquivo.Caminho);
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task MostrarNaPastaAsync() => ExecutarAsync(() =>
    {
        if (Selecionado is { } s) MostrarNaPasta(s.Arquivo.Caminho);
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task ExcluirAsync() => ExecutarAsync(() =>
    {
        if (Selecionado is not { } s) return Task.CompletedTask;
        if (!Confirmar($"Excluir \"{s.Nome}\" da gaveta?\n\nO arquivo é apagado do computador e não vai para a lixeira.")) return Task.CompletedTask;
        _gaveta.Excluir(s.Arquivo.Caminho);
        Atualizar();
        MostrarSucesso("Documento excluído.");
        return Task.CompletedTask;
    });

    [RelayCommand]
    private Task EnviarWhatsAppAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is not { } s || EnviarPorWhatsApp is null) return;
        await EnviarPorWhatsApp(s.Arquivo);
    });
}
