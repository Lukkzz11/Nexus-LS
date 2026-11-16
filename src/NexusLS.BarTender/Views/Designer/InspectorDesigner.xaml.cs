using System.Windows.Controls;
using System.Windows.Input;

namespace NexusLS.BarTender.Views.Designer;

public partial class InspectorDesigner : UserControl
{
    public InspectorDesigner()
    {
        InitializeComponent();
    }

    private void FolhaTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb)
        {
            var be = tb.GetBindingExpression(TextBox.TextProperty);
            be?.UpdateSource();
            Keyboard.ClearFocus();
            this.Focus();
            e.Handled = true;
        }
    }
}
