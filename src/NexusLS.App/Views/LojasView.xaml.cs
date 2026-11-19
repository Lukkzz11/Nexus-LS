using System.Windows;
using System.Windows.Controls;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Views;

public partial class LojasView : UserControl
{
    public LojasView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is LojasViewModel vm) vm.CopiarTexto = texto =>
            {
                try { Clipboard.SetText(texto); }
                catch (System.Runtime.InteropServices.COMException) { /* a área de transferência estava em uso: o usuário pode copiar do campo */ }
            };
        };
    }
}
