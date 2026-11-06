using System.Windows.Controls;

namespace NexusLS.App.Views;

public partial class FechamentoView : UserControl
{
    public FechamentoView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is NexusLS.App.ViewModels.FechamentoViewModel vmFechamento)
                vmFechamento.Confirmar = mensagem => System.Windows.MessageBox.Show(System.Windows.Window.GetWindow(this), mensagem, "Nexus",
                    System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
            if (e.NewValue is NexusLS.App.ViewModels.FechamentoViewModel vm)
                vm.EscolherArquivoParaSalvar = nome =>
                {
                    var dialogo = new Microsoft.Win32.SaveFileDialog { FileName = nome, DefaultExt = ".pdf", Filter = "Documento PDF|*.pdf" };
                    return dialogo.ShowDialog(System.Windows.Window.GetWindow(this)) == true ? dialogo.FileName : null;
                };
        };
    }
}
