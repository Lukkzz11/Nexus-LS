using System.Windows.Controls;
using System.Windows.Input;

namespace NexusLS.BarTender.Views.Designer;

public partial class RibbonDesigner : UserControl
{
    public event Action<string>? PresetSelecionadoAlterado;

    public RibbonDesigner()
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

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is string preset)
        {
            PresetSelecionadoAlterado?.Invoke(preset);
        }
    }
}
