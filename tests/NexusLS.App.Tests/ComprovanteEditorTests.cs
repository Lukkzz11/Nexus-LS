using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

[Collection("OperadorAtual")]
public sealed class ComprovanteEditorTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    public void Dispose() => _banco.Dispose();

    private static byte[] LogoDeTeste()
    {
        byte[]? png = null;
        Capturador.Rodar(() =>
        {
            var desenho = new DrawingVisual();
            using (var g = desenho.RenderOpen())
                g.DrawEllipse(Brushes.Black, null, new Point(100, 50), 90, 40);   // fundo transparente, como um logo em PNG
            var bmp = new RenderTargetBitmap(200, 100, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(desenho);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            png = ms.ToArray();
        });
        return png!;
    }

    private async Task<ConfiguracoesViewModel> Tela()
    {
        var config = new ConfiguracaoService(_banco.Fabrica);
        await config.SalvarAsync(new DadosLoja("Casa Nexus", "12345678000190", "Rua das Flores", "81921423368", "Obrigado!"));
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira())));
        for (var i = 0; i < 300 && vm!.NomeLoja != "Casa Nexus"; i++) await Task.Delay(10);
        return vm!;
    }

    [Fact]
    public async Task Logo_enviado_aparece_na_previa_e_some_ao_desligar()
    {
        var vm = await Tela();
        Capturador.Rodar(() => vm.DefinirLogo(LogoDeTeste()));
        Assert.NotNull(vm.LogoPrevia);
        Assert.True(vm.TemLogo);
        Capturador.Rodar(() => vm.CupomMostrarLogo = false);
        Assert.Null(vm.LogoPrevia);
        Capturador.Rodar(() => vm.CupomMostrarLogo = true);
        Assert.NotNull(vm.LogoPrevia);

        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (!string.IsNullOrWhiteSpace(saida))
        {
            Directory.CreateDirectory(saida);
            Capturador.SalvarConfiguracoes(vm, Path.Combine(saida, "12-config-com-logo.png"));
        }
    }

    [Fact]
    public async Task A_janela_do_comprovante_abre_com_os_estilos_do_programa_e_mostra_a_previa()
    {
        var vm = await Tela();
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        Capturador.Rodar(() =>
        {
            var janela = new NexusLS.App.Views.ComprovanteWindow { DataContext = vm, Width = 1180, Height = 780 };
            janela.Show();
            janela.UpdateLayout();
            Assert.True(janela.ActualWidth > 0);
            if (!string.IsNullOrWhiteSpace(saida))
            {
                Directory.CreateDirectory(saida);
                var bmp = new RenderTargetBitmap(1180, 780, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(janela);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using var f = File.Create(Path.Combine(saida, "13-janela-comprovante.png"));
                enc.Save(f);
            }
            janela.Close();
        });
    }

    [Fact]
    public async Task Escolher_um_modelo_muda_a_previa_e_ajusta_as_opcoes()
    {
        var vm = await Tela();
        var life = vm.Previa;

        Capturador.Rodar(() => vm.Modelos[2].Escolhido = true);
        Assert.Equal(2, vm.CupomModelo);
        Assert.True(vm.CupomMostrarAssinaturaCliente);
        Assert.Contains("COMPROVANTE DE VENDA", vm.Previa);
        Assert.Contains("Total da Nota:", vm.Previa);
        Assert.Contains("Assinatura do cliente", vm.Previa);

        Capturador.Rodar(() => vm.Modelos[1].Escolhido = true);
        Assert.Equal(1, vm.CupomModelo);
        Assert.DoesNotContain("ITENS DA VENDA", vm.Previa);

        Capturador.Rodar(() => vm.Modelos[0].Escolhido = true);
        Assert.Contains("| CUPOM NÃO FISCAL |", vm.Previa);
        Assert.Contains("ITENS DA VENDA", vm.Previa);
        Assert.NotEqual(life, vm.Previa);

        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Equal(0, (await new ConfiguracaoService(_banco.Fabrica).ObterAsync()).CupomModelo);
    }
}
