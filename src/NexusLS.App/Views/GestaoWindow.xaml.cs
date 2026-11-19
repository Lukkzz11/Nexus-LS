using System.IO;
using System.Windows;
using NexusLS.App.Seguranca;
using NexusLS.App.ViewModels;
using NexusLS.Core.Models;

namespace NexusLS.App.Views;

/// <summary>
/// A área de Gestão numa janela própria. Abre com o PIN de um gerente ou administrador e é fechada para o balcão
/// voltar a ter só PDV, caixa e estoque. Enquanto está aberta, a sessão é a de quem digitou o PIN
/// (as ações ficam registradas no nome dele); ao fechar, volta a ser a de quem estava no balcão.
/// </summary>
public partial class GestaoWindow : Window
{
    public GestaoWindow(object dataContext)
    {
        InitializeComponent();
        // O ícone vem do programa; num host sem esse recurso (testes) a janela abre sem ele.
        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/nexus.ico")); }
        catch (Exception e) when (e is IOException or UriFormatException or NotSupportedException) { }
        DataContext = dataContext;
        Closed += (_, _) => (dataContext as GestaoViewModel)?.Encerrar();
    }

    private void Fechar_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Abre a Gestão. Sem login ativo, abre direto. Com login, usa quem já está no balcão se ele tem acesso à gestão;
    /// senão pede o PIN. Devolve só quando a janela for fechada.
    /// </summary>
    public static void Abrir(ContainerDeServicos servicos, Window? dono, ItemMenu inicial, double zoom, Action<double> aoMudarZoom, Func<string> licencaTexto)
    {
        var doBalcao = Sessao.Atual;
        Funcionario? entrou = null;
        if (Sessao.LoginAtivo && !(doBalcao?.PodeAlguma(Permissoes.Gestao) ?? false))
        {
            entrou = LoginWindow.Pedir(servicos.Funcionarios, dono);
            if (entrou is null) return; // desistiu
            if (!entrou.PodeAlguma(Permissoes.Gestao))
            {
                MessageBox.Show(dono, $"{entrou.Nome} não tem acesso à gestão. Peça a um gerente ou administrador.", "Gestão", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Sessao.Entrar(entrou);
        }

        var vm = new GestaoViewModel(servicos, zoom, aoMudarZoom, licencaTexto);
        try
        {
            if (inicial == ItemMenu.Configuracoes)
            {
                // Configurações têm janela própria: abre só ela (presa a esta chamada para a sessão do PIN valer até fechar).
                if (GestaoViewModel.Permitido(ItemMenu.Configuracoes))
                    ConfiguracoesWindow.Abrir(servicos, dono, zoom, aoMudarZoom, licencaTexto,
                        () => !Sessao.LoginAtivo || Sessao.Atual?.Perfil == PerfilFuncionario.Administrador, modal: true);
                return;
            }
            vm.Ir(inicial);
            new GestaoWindow(vm) { Owner = dono }.ShowDialog();
        }
        finally
        {
            if (entrou is not null)
            {
                // Volta a sessão do balcão: a Gestão não deixa ninguém "logado como gerente" no PDV.
                if (doBalcao is not null) Sessao.Entrar(doBalcao); else Sessao.Sair();
            }
        }
    }
}
