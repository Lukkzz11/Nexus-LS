using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.App.Imagens;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>Foto do produto: a redução real com o WPF e o fluxo da tela (escolher, ver, remover).</summary>
public sealed class FotoProdutoTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexus-fotos-app-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        _banco.Dispose();
        if (Directory.Exists(_pasta)) Directory.Delete(_pasta, true);
    }

    private static byte[] PngDe(int largura, int altura)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(Brushes.Tomato, null, new System.Windows.Rect(0, 0, largura, altura));
        var bmp = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Imagem_grande_vira_jpeg_com_no_maximo_o_lado_pedido_e_texto_nao_e_imagem()
    {
        Capturador.Rodar(() =>
        {
            var proc = new ProcessadorImagemWpf();
            var jpeg = proc.ReduzirParaJpeg(PngDe(3000, 1500), 1200);
            Assert.Equal(0xFF, jpeg[0]);
            Assert.Equal(0xD8, jpeg[1]);                                    // assinatura do JPEG
            var quadro = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(1200, quadro.PixelWidth);
            Assert.Equal(600, quadro.PixelHeight);

            var pequena = BitmapDecoder.Create(new MemoryStream(proc.ReduzirParaJpeg(PngDe(300, 200), 1200)), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
            Assert.Equal(300, pequena.PixelWidth);                          // não amplia

            Assert.Throws<RegraDeNegocioException>(() => proc.ReduzirParaJpeg(System.Text.Encoding.UTF8.GetBytes("isto não é imagem"), 1200));
        });
    }

    [Fact]
    public async Task Tela_exige_produto_salvo_mostra_a_foto_e_remove()
    {
        var produtos = new ProdutoService(_banco.Fabrica);
        var fotos = new FotoProdutoService(_banco.Fabrica, new ProcessadorImagemWpf(), _pasta);
        var vm = new EstoqueLojaViewModel(produtos, new EstoqueService(_banco.Fabrica), fotos);
        var arquivo = Path.Combine(Path.GetTempPath(), "foto-teste-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(arquivo, PngDe(800, 800));
        try
        {
            vm.EscolherFoto = () => arquivo;
            vm.NovoCommand.Execute(null);
            await vm.TrocarFotoCommand.ExecuteAsync(null);
            Assert.True(vm.MensagemEhErro);
            Assert.Contains("Salve o produto antes", vm.Mensagem);

            vm.Nome = "Camiseta"; vm.PrecoVenda = "50,00";
            await vm.SalvarCommand.ExecuteAsync(null);
            Assert.False(vm.MensagemEhErro, vm.Mensagem);
            Assert.False(vm.TemFoto);
            Assert.Equal("Sem foto", vm.SituacaoFoto);

            await vm.TrocarFotoCommand.ExecuteAsync(null);
            Assert.False(vm.MensagemEhErro, vm.Mensagem);
            Assert.True(vm.TemFoto);
            Assert.Contains("será enviada", vm.SituacaoFoto);

            await vm.RemoverFotoCommand.ExecuteAsync(null);
            Assert.False(vm.TemFoto);
            Assert.Equal("Sem foto", vm.SituacaoFoto);
        }
        finally { File.Delete(arquivo); }
    }
}
