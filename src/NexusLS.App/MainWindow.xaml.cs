using System.Windows;
using NexusLS.App.ViewModels;

namespace NexusLS.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // DataContext = "de onde a tela tira os dados do binding". Aqui, o MainViewModel.
        DataContext = new MainViewModel();
    }
}
