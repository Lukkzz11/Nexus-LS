using System.IO;
using System.Windows;
using Wpf.Ui.Controls;

namespace NexusLS.BarTender.Views;

public partial class MainWindow : FluentWindow
{
    public MainWindow()
    {
        App.Log("MainWindow ctor BEGIN");
        try
        {
            InitializeComponent();
            App.Log("MainWindow InitializeComponent SUCCESS");
        }
        catch (Exception ex)
        {
            App.Log($"MainWindow InitializeComponent EXCEPTION: {ex}");
            throw;
        }

        Loaded += (s, e) => App.Log("MainWindow LOADED event fired!");
        Closing += (s, e) => App.Log($"MainWindow CLOSING event fired! Cancel={e.Cancel}");
        Closed += (s, e) => App.Log("MainWindow CLOSED event fired!");
    }

    private System.Windows.Threading.DispatcherTimer? _timerToast;

    public void ExibirToast(string mensagem, string titulo = "Sucesso")
    {
        if (ToastSucesso == null) return;

        ToastTitulo.Text = titulo;
        ToastMensagem.Text = mensagem;
        ToastSucesso.Visibility = Visibility.Visible;
        ToastSucesso.Opacity = 1.0;

        _timerToast?.Stop();
        _timerToast = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _timerToast.Tick += (s, e) =>
        {
            _timerToast?.Stop();
            ToastSucesso.Visibility = Visibility.Collapsed;
        };
        _timerToast.Start();
    }

    private void DataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm && vm.ProdutoSelecionado != null)
        {
            vm.AbrirProdutoDoCatalogoCommand.Execute(vm.ProdutoSelecionado);
        }
    }
}
