using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.App.ViewModels;
using NexusLS.Core.Gaveta;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

public sealed class OrcamentosTelaTests : IDisposable
{
    private readonly BancoDeTeste _banco = new();
    private readonly string _pasta = Directory.CreateTempSubdirectory("orc").FullName;
    private readonly List<(string Telefone, string Mensagem, string Anexo)> _whatsApp = new();

    public void Dispose()
    {
        _banco.Dispose();
        Directory.Delete(_pasta, true);
    }

    private async Task<OrcamentosViewModel> TelaAsync()
    {
        var vm = new OrcamentosViewModel(new OrcamentoService(_banco.Fabrica), new ProdutoService(_banco.Fabrica),
            new ClienteService(_banco.Fabrica), new ConfiguracaoService(_banco.Fabrica), new GavetaService(_pasta))
        {
            AbrirWhatsApp = (t, m, a) => _whatsApp.Add((t, m, a))
        };
        await vm.Carregamento;
        return vm;
    }

    private static async Task MontarAsync(OrcamentosViewModel vm)
    {
        await vm.NovoCommand.ExecuteAsync(null);
        vm.ClienteNome = "Escola Sol";
        vm.ClienteTelefone = "11999990000";
        vm.AdicionarItemLivreCommand.Execute(null);
        vm.ItensForm[0].Nome = "Camiseta estampada";
        vm.ItensForm[0].QuantidadeTexto = "10";
        vm.ItensForm[0].PrecoTexto = "35,00";
        vm.Desconto = "20,00";
        vm.Observacao = "Entrega em 10 dias. 50% na entrada.";
    }

    [Fact]
    public async Task Montar_salvar_mandar_e_aprovar()
    {
        var vm = await TelaAsync();
        await MontarAsync(vm);
        Assert.Contains("330,00", vm.TotalForm);

        await vm.SalvarCommand.ExecuteAsync(null);

        Assert.False(vm.FormAberto);
        Assert.Single(vm.Lista);
        Assert.True(vm.EstaAberto);
        Assert.Contains("330,00", vm.DetalheTotal);

        await vm.EnviarWhatsAppCommand.ExecuteAsync(null);
        var envio = Assert.Single(_whatsApp);
        Assert.Equal("11999990000", envio.Telefone);
        Assert.Contains("Olá, Escola!", envio.Mensagem);
        Assert.True(File.Exists(envio.Anexo));

        await vm.VirarPedidoCommand.ExecuteAsync(null);
        Assert.False(vm.EstaAberto);
        Assert.Contains("Aprovado", vm.DetalheTitulo);
        Assert.Single(await new PedidoService(_banco.Fabrica).ListarAsync());
    }

    [Fact]
    public async Task Corrigir_abre_o_formulario_preenchido_e_nao_cria_outro()
    {
        var produto = await new ProdutoService(_banco.Fabrica).CadastrarAsync(new DadosProduto("Camisa", "111", 5000, 2000, 0), 5);
        var vm = await TelaAsync();
        await MontarAsync(vm);
        vm.BuscaProduto = "Camisa";
        await vm.AdicionarProdutoCommand.ExecuteAsync(null);
        await vm.SalvarCommand.ExecuteAsync(null);

        await vm.CorrigirCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.ItensForm.Count);
        Assert.Equal("20,00", vm.Desconto);
        Assert.Equal(produto.Id, vm.ItensForm[1].ProdutoId);
        vm.ItensForm[0].QuantidadeTexto = "20";
        await vm.SalvarCommand.ExecuteAsync(null);
        Assert.Single(vm.Lista);
        Assert.Contains("730,00", vm.DetalheTotal);
    }

    /// <summary>Só gera a imagem quando NEXUSLS_PRINTS aponta uma pasta (conferência visual).</summary>
    [Fact]
    public async Task Gera_a_captura_da_tela()
    {
        var pasta = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(pasta)) return;
        Directory.CreateDirectory(pasta);
        var vm = await TelaAsync();
        await MontarAsync(vm);
        await vm.SalvarCommand.ExecuteAsync(null);

        void Capturar(string nome) => Capturador.Rodar(() =>
        {
            var tela = new NexusLS.App.Views.OrcamentosView { DataContext = vm, Margin = new Thickness(20) };
            var janela = new Window { Content = tela, Width = 1150, Height = 760, Left = -3000, Top = 0, ShowInTaskbar = false };
            janela.SetResourceReference(Window.BackgroundProperty, "CorFundo");
            janela.Show();
            janela.UpdateLayout();
            tela.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap((int)janela.ActualWidth, (int)janela.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(janela);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (var arquivo = File.Create(Path.Combine(pasta, nome))) png.Save(arquivo);
            tela.DataContext = null; // solta os botões antes de usar os comandos de novo fora desta thread
            janela.Close();
        });

        await vm.EnviarWhatsAppCommand.ExecuteAsync(null);
        File.Copy(_whatsApp[0].Anexo, Path.Combine(pasta, "orcamento.pdf"), true);
        Capturar("orcamentos.png");

        // O formulário numa tela nova: as listas ligadas a uma janela só podem mudar na thread dela.
        vm = await TelaAsync();
        vm.Selecionado = vm.Lista[0];
        await vm.Carregamento;
        await vm.CorrigirCommand.ExecuteAsync(null);
        Capturar("orcamentos-form.png");
    }
}
