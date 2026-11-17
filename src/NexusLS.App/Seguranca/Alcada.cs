using System.Windows;
using NexusLS.Core.Models;
using NexusLS.Core.Seguranca;

namespace NexusLS.App.Seguranca;

/// <summary>
/// Alçada do balcão: o que o operador faz sozinho e o que precisa de um gerente (PIN). Os limites vêm das
/// Configurações; nos testes de ViewModel, sem o programa de verdade, tudo é liberado.
/// </summary>
public static class Alcada
{
    public static Func<ConfiguracaoSeguranca> Config { get; set; } = () => new();

    /// <summary>Pede a autorização. Devolve true se pode seguir. O programa troca por uma versão que abre a tela de PIN.</summary>
    public static Func<string, bool> Solicitar { get; set; } = _ => true;

    public static bool EhGerente(Funcionario? f) => f?.Perfil is PerfilFuncionario.Administrador or PerfilFuncionario.Gerente;

    /// <summary>Sem login ativo, ou com um gerente/administrador no balcão, não há o que autorizar.</summary>
    public static bool Dispensada() => !Sessao.LoginAtivo || EhGerente(Sessao.Atual);

    /// <summary>A versão do programa: mostra o motivo, pede o PIN de um gerente e anota a autorização na trilha.</summary>
    public static bool PedirNaTela(string motivo)
    {
        if (Dispensada()) return true;
        var dono = Application.Current.Windows.OfType<Window>().LastOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;
        var resposta = MessageBox.Show(dono, motivo + Environment.NewLine + Environment.NewLine + "Peça a um gerente ou administrador para digitar o PIN.",
            "Autorização de gerente", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (resposta != MessageBoxResult.OK) return false;

        var f = Views.LoginWindow.Pedir(App.Servicos.Funcionarios, dono);
        if (f is null) return false;
        if (!EhGerente(f))
        {
            MessageBox.Show(dono, $"{f.Nome} não é gerente nem administrador.", "Autorização de gerente", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        try { Task.Run(() => App.Servicos.Funcionarios.RegistrarAutorizacaoAsync(f, motivo)).GetAwaiter().GetResult(); }
        catch (Exception ex) { NexusLS.Core.Diagnostico.RegistroDeLog.Erro("Não foi possível anotar a autorização do gerente.", ex); }
        return true;
    }
}
