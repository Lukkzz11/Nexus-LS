using System.Windows;
using PdvSimples.App.ViewModels;

namespace PdvSimples.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // DataContext = "de onde a tela tira os dados do binding". Aqui, o MainViewModel.
        DataContext = new MainViewModel();
    }
}
