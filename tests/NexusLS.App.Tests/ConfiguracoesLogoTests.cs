using System.IO;
using NexusLS.App.ViewModels;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>"Enviar logo" na tela de Configurações: a imagem é reduzida, aparece na prévia e só é guardada ao salvar.</summary>
[Collection("OperadorAtual")]
public sealed class ConfiguracoesLogoTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "nexusls-logo-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { _banco.Dispose(); try { Directory.Delete(_pasta, true); } catch (IOException) { } }

    private static byte[] BmpGrande(int largura, int altura)
    {
        byte[]? bytes = null;
        Capturador.Rodar(() =>
        {
            var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(largura, altura, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            bytes = ms.ToArray();
        });
        return bytes!;
    }

    [Fact]
    public async Task Enviar_logo_reduz_mostra_na_previa_e_so_grava_ao_salvar()
    {
        Directory.CreateDirectory(_pasta);
        var arquivo = Path.Combine(_pasta, "logo.png");
        await File.WriteAllBytesAsync(arquivo, BmpGrande(1200, 600));
        var config = new ConfiguracaoService(_banco.Fabrica);
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira())) { EscolherArquivoLogo = () => arquivo });
        for (var i = 0; i < 300 && vm!.NomeLoja == ""; i++) await Task.Delay(10);

        Assert.False(vm!.TemLogo);
        Capturador.Rodar(() => vm.EnviarLogoCommand.Execute(null));
        for (var i = 0; i < 300 && !vm.TemLogo; i++) await Task.Delay(10);
        Assert.True(vm.TemLogo);
        Assert.Null((await config.ObterAsync()).LogoCupom); // ainda não salvou

        await Capturador.RodarAsync(() => vm.SalvarCommand.ExecuteAsync(null));
        var guardado = (await config.ObterAsync()).LogoCupom;
        Assert.NotNull(guardado);
        Assert.True(guardado!.Length < ConfiguracaoService.LogoMaximoBytes);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, guardado[..4]);

        Capturador.Rodar(() => vm.RemoverLogoCommand.Execute(null));
        Assert.False(vm.TemLogo);
        await Capturador.RodarAsync(() => vm.SalvarCommand.ExecuteAsync(null));
        Assert.Null((await config.ObterAsync()).LogoCupom);
    }

    [Fact]
    public async Task Arquivo_que_nao_e_imagem_avisa_sem_mexer_no_logo()
    {
        Directory.CreateDirectory(_pasta);
        var arquivo = Path.Combine(_pasta, "texto.png");
        await File.WriteAllTextAsync(arquivo, "isto não é uma imagem");
        var config = new ConfiguracaoService(_banco.Fabrica);
        ConfiguracoesViewModel? vm = null;
        Capturador.Rodar(() => vm = new ConfiguracoesViewModel(config, new CupomService(_banco.Fabrica, config, new ImpressoraDeMentira())) { EscolherArquivoLogo = () => arquivo });
        Capturador.Rodar(() => vm!.EnviarLogoCommand.Execute(null));
        for (var i = 0; i < 300 && !vm!.MensagemEhErro; i++) await Task.Delay(10);
        Assert.True(vm!.MensagemEhErro);
        Assert.Contains("imagem", vm.Mensagem);
        Assert.False(vm.TemLogo);
    }
}
