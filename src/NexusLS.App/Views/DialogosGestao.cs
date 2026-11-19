using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace NexusLS.App.Views;

/// <summary>As janelinhas do Windows que as telas da Gestão usam (salvar CSV, escolher arquivo, confirmar, abrir). Ficam fora dos ViewModels.</summary>
internal static class DialogosGestao
{
    public static Func<string, string?> SalvarCsv(DependencyObject tela) => nomeSugerido =>
    {
        var d = new SaveFileDialog { FileName = nomeSugerido, DefaultExt = ".csv", Filter = "Planilha CSV (Excel)|*.csv|Relatório em PDF (padrão Nexus)|*.pdf", Title = "Salvar planilha" };
        return d.ShowDialog(Window.GetWindow(tela)) == true ? d.FileName : null;
    };

    public static Func<string?> EscolherAnexo(DependencyObject tela) => () =>
    {
        var d = new OpenFileDialog
        {
            Title = "Escolha a foto ou o documento",
            Filter = "Fotos e documentos|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.pdf;*.txt;*.csv;*.doc;*.docx;*.xls;*.xlsx|Todos os arquivos|*.*"
        };
        return d.ShowDialog(Window.GetWindow(tela)) == true ? d.FileName : null;
    };

    public static Func<string, bool> Confirmar(DependencyObject tela) => texto =>
        MessageBox.Show(Window.GetWindow(tela), texto, "Confirmar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public static void AbrirArquivo(string caminho)
    {
        if (!File.Exists(caminho)) throw new FileNotFoundException("Arquivo não encontrado.", caminho);
        Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true });
    }
}
