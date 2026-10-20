using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Seguranca;

/// <summary>
/// Quem está usando o programa agora. Se ninguém foi cadastrado como funcionário, o login fica desligado e tudo é
/// permitido (como o programa sempre funcionou). Com funcionários cadastrados, só entra quem acerta o PIN, e cada
/// tela segue as permissões do perfil.
/// </summary>
public static class Sessao
{
    public static Funcionario? Atual { get; private set; }

    /// <summary>Verdadeiro quando existe pelo menos um funcionário cadastrado.</summary>
    public static bool LoginAtivo { get; private set; }

    public static event Action? Mudou;

    public static void DefinirLoginAtivo(bool ativo)
    {
        LoginAtivo = ativo;
        Mudou?.Invoke();
    }

    public static void Entrar(Funcionario funcionario)
    {
        Atual = funcionario;
        LoginAtivo = true;
        OperadorAtual.Nome = funcionario.Nome;
        Mudou?.Invoke();
    }

    public static void Sair()
    {
        Atual = null;
        OperadorAtual.Nome = null;
        Mudou?.Invoke();
    }

    /// <summary>Sem login ativo tudo é permitido; com login, só o que o perfil do funcionário permite.</summary>
    public static bool Pode(Permissao permissao) => LoginAtivo ? Atual?.Pode(permissao) ?? false : true;
}
