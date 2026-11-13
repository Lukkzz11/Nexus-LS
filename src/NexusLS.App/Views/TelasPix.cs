using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace NexusLS.App.Views;

/// <summary>Coloca uma janela no centro de OUTRO monitor (o do cliente). Com um monitor só, não mexe em nada. Trabalha em pixels, então vale para monitores com escalas diferentes.</summary>
internal static class TelasPix
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct InfoMonitor { public int Tamanho; public Rect Monitor, Trabalho; public int Flags; }
    private delegate bool EnumProc(IntPtr monitor, IntPtr dc, ref Rect rect, IntPtr dados);

    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc proc, IntPtr dados);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref InfoMonitor info);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr depois, int x, int y, int w, int h, uint flags);

    private const uint MonitorMaisProximo = 2, SemTamanho = 0x1, SemZ = 0x4, SemAtivar = 0x10;

    public static void MoverParaOutroMonitor(Window janela)
    {
        try
        {
            var hwnd = new WindowInteropHelper(janela).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Monitor do dono (onde o operador está) ou o da própria janela.
            var dono = janela.Owner is { } o ? new WindowInteropHelper(o).Handle : IntPtr.Zero;
            var atual = MonitorFromWindow(dono != IntPtr.Zero ? dono : hwnd, MonitorMaisProximo);

            var outros = new List<Rect>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr m, IntPtr _, ref Rect _, IntPtr _) =>
            {
                if (m == atual) return true;
                var info = new InfoMonitor { Tamanho = Marshal.SizeOf<InfoMonitor>() };
                if (GetMonitorInfo(m, ref info)) outros.Add(info.Trabalho);
                return true;
            }, IntPtr.Zero);
            if (outros.Count == 0) return;                                  // um monitor só: fica onde está

            var alvo = outros[0];
            GetWindowRect(hwnd, out var r);
            var x = alvo.Left + (alvo.Right - alvo.Left - (r.Right - r.Left)) / 2;
            var y = alvo.Top + (alvo.Bottom - alvo.Top - (r.Bottom - r.Top)) / 2;
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SemTamanho | SemZ | SemAtivar);
        }
        catch (Exception) { /* sem isso o Pix ainda abre no monitor atual */ }
    }
}
