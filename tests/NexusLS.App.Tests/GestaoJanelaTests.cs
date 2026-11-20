using System.Windows;
using System.Windows.Controls;
using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.App.Views;
using NexusLS.Core.Models;

namespace NexusLS.App.Tests;

/// <summary>A Gestão sai do menu do balcão e vira uma janela própria: quem entra, o que aparece e que ela abre e fecha.</summary>
[Collection("Sessao")]
public sealed class GestaoJanelaTests : IDisposable
{
    public GestaoJanelaTests() => Limpar();
    public void Dispose() => Limpar();
    private static void Limpar() { Sessao.Sair(); Sessao.DefinirLoginAtivo(false); }

    private static Funcionario Func(PerfilFuncionario perfil) => new() { Id = 1, Nome = "Teste", Perfil = perfil, Ativo = true };

    [Theory]
    [InlineData(PerfilFuncionario.Administrador, true)]
    [InlineData(PerfilFuncionario.Gerente, true)]
    [InlineData(PerfilFuncionario.OperadorDeCaixa, false)]
    [InlineData(PerfilFuncionario.Vendedor, false)]
    [InlineData(PerfilFuncionario.Personalizado, false)]
    public void So_gerente_e_administrador_entram_na_gestao(PerfilFuncionario perfil, bool entra) =>
        Assert.Equal(entra, Func(perfil).PodeAlguma(Permissoes.Gestao));

    [Fact]
    public void Personalizado_com_financeiro_entra_na_gestao()
    {
        var f = new Funcionario { Perfil = PerfilFuncionario.Personalizado, PermissoesPersonalizadas = Permissao.Vender | Permissao.Financeiro };
        Assert.True(f.PodeAlguma(Permissoes.Gestao));
    }

    [Fact]
    public void Sem_login_ativo_tudo_da_gestao_esta_liberado()
    {
        foreach (var item in new[] { ItemMenu.Agenda, ItemMenu.Clientes, ItemMenu.Financeiro, ItemMenu.ContasESaldos, ItemMenu.Relatorios, ItemMenu.Funcionarios, ItemMenu.Configuracoes, ItemMenu.Atividades, ItemMenu.Compras, ItemMenu.Fornecedores, ItemMenu.CalculadoraPrecos, ItemMenu.RelatorioFinanceiro, ItemMenu.RelatorioProdutos, ItemMenu.GestaoEmpresa, ItemMenu.EntradasESaidas })
            Assert.True(GestaoViewModel.Permitido(item), item.ToString());
    }

    [Fact]
    public void Gerente_ve_financeiro_e_relatorios_mas_nao_configuracoes_nem_funcionarios()
    {
        Sessao.Entrar(Func(PerfilFuncionario.Gerente));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.Financeiro));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.Relatorios));
        Assert.True(GestaoViewModel.Permitido(ItemMenu.Clientes));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Configuracoes));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Funcionarios));
    }

    [Fact]
    public void Vendedor_nao_ve_nada_da_gestao_alem_da_agenda_e_clientes()
    {
        Sessao.Entrar(Func(PerfilFuncionario.Vendedor));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Financeiro));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Relatorios));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Configuracoes));
        Assert.False(GestaoViewModel.Permitido(ItemMenu.Funcionarios));
    }

    [Fact]
    public void Os_itens_da_gestao_sao_reconhecidos_e_os_do_balcao_nao()
    {
        foreach (var item in new[] { ItemMenu.Agenda, ItemMenu.Clientes, ItemMenu.Financeiro, ItemMenu.ContasESaldos, ItemMenu.Relatorios, ItemMenu.Funcionarios, ItemMenu.Configuracoes, ItemMenu.CatalogoOnline, ItemMenu.Atividades, ItemMenu.Compras, ItemMenu.Fornecedores, ItemMenu.CalculadoraPrecos, ItemMenu.RelatorioFinanceiro, ItemMenu.RelatorioProdutos, ItemMenu.GestaoEmpresa, ItemMenu.EntradasESaidas })
            Assert.True(GestaoViewModel.EhDaGestao(item), item.ToString());
        foreach (var item in new[] { ItemMenu.Inicio, ItemMenu.Pdv, ItemMenu.Caixa, ItemMenu.EstoqueDaLoja, ItemMenu.Etiquetas })
            Assert.False(GestaoViewModel.EhDaGestao(item), item.ToString());
    }

    public sealed class GestaoFalsa
    {
        public ItemMenu ItemAtivo => ItemMenu.Agenda;
        public object? TelaAtual => null;
        public double Zoom => 1.0;
        public bool VerClientes => true;
        public bool VerFinanceiro => true;
        public bool VerRelatorios => true;
        public bool VerConfiguracoes => true;
        public bool VerFuncionarios => true;
        public bool VerCompras => true;
        public bool VerEstoque => true;
        public bool VerGrupoEstoque => true;
        public bool VerGrupoPessoas => true;
        public bool VerGrupoEmpresa => true;
        public string UsuarioTexto => "Maria (Gerente)";
    }

    [Fact]
    public void A_janela_abre_com_todos_os_itens_do_menu_agrupados_e_fecha_pelo_botao()
    {
        var tags = new List<string>(); bool fechou = false; string? botaoFechar = null;
        Capturador.Rodar(() =>
        {
            var janela = new GestaoWindow(new GestaoFalsa()) { WindowState = WindowState.Normal, ShowInTaskbar = false };
            janela.Closed += (_, _) => fechou = true;
            janela.Show();
            janela.UpdateLayout();
            foreach (var b in Botoes(janela))
            {
                if (b.Tag is string t) tags.Add(t);
                else if ((b.Content as string) == "Fechar a gestão") { botaoFechar = "ok"; b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
            }
        });
        Assert.Equal(new[]
        {
            "Agenda", "Clientes", "Relatorios", "Gaveta", "WhatsApp",
            "Financeiro", "ContasESaldos", "RelatorioFinanceiro",
            "RelatorioProdutos", "Compras", "Fornecedores", "CalculadoraPrecos",
            "Funcionarios", "Atividades",
            "GestaoEmpresa", "EntradasESaidas", "Configuracoes"
        }, tags);
        Assert.Equal("ok", botaoFechar);
        Assert.True(fechou);
    }

    [Fact]
    public void Desenha_a_janela_de_gestao_quando_pedido()
    {
        var saida = Environment.GetEnvironmentVariable("NEXUSLS_PRINTS");
        if (string.IsNullOrWhiteSpace(saida)) return;
        System.IO.Directory.CreateDirectory(saida);
        Capturador.Rodar(() =>
        {
            var janela = new GestaoWindow(new GestaoFalsa()) { WindowState = WindowState.Normal, Width = 1100, Height = 620, ShowInTaskbar = false, ShowActivated = false, Left = -20000, Top = 0 };
            janela.Show();
            janela.UpdateLayout();
            var alvo = (FrameworkElement)janela.Content;
            var img = new System.Windows.Media.Imaging.RenderTargetBitmap((int)alvo.ActualWidth, (int)alvo.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            img.Render(alvo);
            var png = new System.Windows.Media.Imaging.PngBitmapEncoder();
            png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
            using var f = System.IO.File.Create(System.IO.Path.Combine(saida, "gestao-janela.png"));
            png.Save(f);
            janela.Close();
        });
    }

    private static IEnumerable<Button> Botoes(DependencyObject raiz)
    {
        foreach (var filho in LogicalTreeHelper.GetChildren(raiz).OfType<DependencyObject>())
        {
            if (filho is Button b) yield return b;
            foreach (var neto in Botoes(filho)) yield return neto;
        }
    }
}
