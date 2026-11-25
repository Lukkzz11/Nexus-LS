using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.BarTender.ViewModels;
using NexusLS.Core.Models;
using Xunit;

namespace NexusLS.App.Tests;

public sealed class CapturaEstudioEtiquetas
{
    /// <summary>Captura o Estúdio com um produto sem tamanhos e uma grade escolhida. Só roda com NEXUSLS_PRINTS.</summary>
    [Fact]
    public void Captura_EstudioComGradeEscolhida()
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(pasta)) return;
        Directory.CreateDirectory(pasta);

        Exception? erro = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new NexusLS.BarTender.App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += (_, e) => e.Handled = true;
                app.InitializeComponent();

                var vm = new DesignerViewModel { CaminhoPreferencias = Path.Combine(pasta, "prefs.json") };
                var tela = new NexusLS.BarTender.Views.Designer.DesignerView { DataContext = vm };
                var janela = new Window
                {
                    Width = 1600, Height = 880, WindowStyle = WindowStyle.None, ShowInTaskbar = false,
                    ShowActivated = false, Left = -20000, Top = 0, Content = tela
                };
                janela.Show();
                janela.UpdateLayout();
                janela.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

                vm.DefinirProduto(new Produto
                {
                    Id = 9002, Nome = "BERMUDA SOCIAL DE LINHO", Referencia = "1453", PrecoVendaCentavos = 8500,
                    CodigoBarras = "2000000000091",
                    Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = Tamanhos.SemTamanho } }
                }, "LOJA", carregarEtiquetaSalvaSeHouver: false);

                void Salvar(string nome)
                {
                    tela.ReconstruirCanvas();
                    janela.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    janela.UpdateLayout();
                    var bmp = new RenderTargetBitmap(1600, 880, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(tela);
                    var png = new PngBitmapEncoder();
                    png.Frames.Add(BitmapFrame.Create(bmp));
                    using var arquivo = File.Create(Path.Combine(pasta, nome));
                    png.Save(arquivo);
                }

                Salvar("estudio-sem-tamanho.png");
                vm.GradeSelecionada = GradesDeTamanho.CalcaBermuda.Nome;
                Salvar("estudio-grade.png");

                janela.Close();
                app.Shutdown();
            }
            catch (Exception ex) { erro = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (erro != null) throw erro;
    }
}
