using System.Windows.Controls;

namespace NexusLS.App.Views;

/// <summary>A View só desenha. Não tem regra nenhuma aqui: tudo vem do ProdutosViewModel via binding.</summary>
public partial class ProdutosView : UserControl
{
    public ProdutosView()
    {
        InitializeComponent();
    }
}
