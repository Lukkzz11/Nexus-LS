using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using NexusLS.Tests.Infra;

namespace NexusLS.App.Tests;

/// <summary>
/// Desenha a janela principal (menu lateral + uma tela) num PNG para conferir o visual do design system.
/// Só roda com a variável NEXUSLS_PRINTS. Não usa o MainViewModel de verdade (ele abriria o banco do usuário):
/// lê o MainWindow.xaml do código-fonte e liga a um "menu de mentira" com os mesmos nomes de propriedade.
/// </summary>
public sealed class CapturaJanelaPrincipal : IDisposable
{
    private readonly BancoDeTeste _banco = new();

    public void Dispose() => _banco.Dispose();

    public sealed class AtualizacaoFalsa
    {
        public bool TemNovaVersao => true;
        public string AvisoTexto => "Nova versão 1.2.0 disponível.";
        public bool Baixando => false;
        public string ProgressoTexto => "";
        public double ProgressoPercentual { get; set; }
        public string Mensagem => "";
        public bool MensagemEhErro => false;
    }

    public sealed class MenuFalso
    {
        public ItemMenu ItemAtivo { get; init; }
        public object? TelaAtual { get; init; }
        public bool VerSecaoVendas => true;
        public bool VerPdv => true;
        public bool VerCaixa => true;
        public bool VerRelatorios => true;
        public bool VerSecaoEstoque => true;
        public bool VerEstoque => true;
        public bool VerCompras => true;
        public bool VerSecaoGestao => true;
        public bool VerClientes => true;
        public bool VerFinanceiro => true;
        public bool VerFuncionarios => true;
        public bool VerConfiguracoes => true;
        public bool TemUsuario => true;
        public string UsuarioTexto => "Maria Souza (Gerente)";
        public AtualizacaoFalsa Atualizacao { get; } = new();
    }

    private static string CaminhoDoXaml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusLS.slnx"))) dir = dir.Parent;
        return Path.Combine(dir!.FullName, "src", "NexusLS.App", "MainWindow.xaml");
    }

    [Fact]
    public async Task Gera_a_janela_principal_com_o_menu()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        Directory.CreateDirectory(saida);

        var produtos = new ProdutoService(_banco.Fabrica);
        var estoque = new EstoqueService(_banco.Fabrica);
        var camiseta = await produtos.CadastrarAsync(new DadosProduto("Camiseta Básica Algodão", "789", 5990, 2000, 12, "REF-100", 3990), 8);
        await produtos.CadastrarAsync(new DadosProduto("Boné Aba Reta", "555", 2990, 800, 5), 10);
        await produtos.CadastrarAsync(new DadosProduto("Cinto Couro", null, 8990, 3000, 5), 1);

        EstoqueLojaViewModel? vm = null;
        Capturador.Rodar(() => vm = new EstoqueLojaViewModel(produtos, estoque));
        for (var i = 0; i < 300 && vm!.Produtos.Count < 3; i++) await Task.Delay(10);
        Capturador.Rodar(() => vm!.Selecionado = vm.Produtos.First(p => p.Id == camiseta.Id));
        await Task.Delay(150);

        var texto = Regex.Replace(File.ReadAllText(CaminhoDoXaml()), @"\sx:Class=""[^""]*""", "");
        Capturador.Rodar(() =>
        {
            var janela = (Window)XamlReader.Parse(texto);
            var dados = new MenuFalso { ItemAtivo = ItemMenu.EstoqueDaLoja, TelaAtual = vm };
            
            var raiz = (FrameworkElement)janela.Content;
            ((FrameworkElement)janela.Content).DataContext = dados;
            janela.Content = null; // solta o conteúdo da janela para poder desenhá-lo dentro de outro elemento
            const int largura = 1440, altura = 860;
            raiz.Measure(new Size(largura, altura));
            raiz.Arrange(new Rect(0, 0, largura, altura));
            raiz.UpdateLayout();

            var fundo = new System.Windows.Controls.Border { Width = largura, Height = altura, Background = janela.Background, Child = raiz };
            fundo.Measure(new Size(largura, altura));
            fundo.Arrange(new Rect(0, 0, largura, altura));
            fundo.UpdateLayout();
            var imagem = new RenderTargetBitmap(largura, altura, 96, 96, PixelFormats.Pbgra32);
            imagem.Render(fundo);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(imagem));
            using var arquivo = File.Create(Path.Combine(saida, "20-janela-principal.png"));
            png.Save(arquivo);
        });
    }
}
