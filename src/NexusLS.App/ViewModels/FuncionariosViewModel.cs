using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.App.Seguranca;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record OpcaoPerfil(PerfilFuncionario Perfil, string Nome);

/// <summary>Uma caixinha de permissão do formulário.</summary>
public partial class ItemPermissao : ObservableObject
{
    public ItemPermissao(Permissao permissao) { Permissao = permissao; Nome = Permissoes.Nome(permissao); }

    public Permissao Permissao { get; }
    public string Nome { get; }

    [ObservableProperty] private bool _marcada;
}

/// <summary>
/// Cadastro de funcionários, perfis e permissões. Enquanto ninguém está cadastrado o login fica desligado: a tela oferece
/// criar o primeiro administrador (que liga o login e recebe o código de recuperação).
/// </summary>
public partial class FuncionariosViewModel : ViewModelBase
{
    private readonly FuncionarioService _servico;
    private bool _silenciar;

    public FuncionariosViewModel(FuncionarioService servico)
    {
        _servico = servico;
        _perfilSelecionado = Perfis[1];
        foreach (var p in Permissoes.Lista) PermissoesLista.Add(new ItemPermissao(p));
        Novo();
        Carregamento = CarregarAsync();
    }

    public Task Carregamento { get; }

    /// <summary>Mostra um texto importante numa janela (a View preenche). Usado para o código de recuperação.</summary>
    public Action<string, string> Avisar { get; set; } = (_, _) => { };

    public IReadOnlyList<OpcaoPerfil> Perfis { get; } = new[]
    {
        PerfilFuncionario.Administrador, PerfilFuncionario.Gerente, PerfilFuncionario.OperadorDeCaixa,
        PerfilFuncionario.Vendedor, PerfilFuncionario.Personalizado
    }.Select(p => new OpcaoPerfil(p, Permissoes.Nome(p))).ToList();

    public ObservableCollection<Funcionario> Funcionarios { get; } = new();
    public ObservableCollection<ItemPermissao> PermissoesLista { get; } = new();

    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private Funcionario? _selecionado;

    // ----- Formulário -----
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _cargo = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private OpcaoPerfil _perfilSelecionado;
    [ObservableProperty] private string _pinNovo = "";
    [ObservableProperty] private string _pinConfirmacao = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Editando))]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    private int? _editandoId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoAtivar))]
    private bool _editandoAtivo = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoginDesligado))]
    private bool _loginAtivo;

    public bool LoginDesligado => !LoginAtivo;
    public bool Editando => EditandoId is not null;
    public string TituloFormulario => LoginAtivo ? (Editando ? "Editar funcionário" : "Novo funcionário") : "Ativar o login com o primeiro administrador";
    public string TextoAtivar => EditandoAtivo ? "Desativar" : "Reativar";
    public bool PerfilPersonalizado => PerfilSelecionado.Perfil == PerfilFuncionario.Personalizado;
    public bool EhAdministradorEditado => Editando && PerfilSelecionado.Perfil == PerfilFuncionario.Administrador;

    partial void OnMostrarInativosChanged(bool value) => _ = CarregarAsync();
    partial void OnLoginAtivoChanged(bool value) => OnPropertyChanged(nameof(TituloFormulario));

    partial void OnPerfilSelecionadoChanged(OpcaoPerfil value)
    {
        OnPropertyChanged(nameof(PerfilPersonalizado));
        OnPropertyChanged(nameof(EhAdministradorEditado));
        if (value.Perfil != PerfilFuncionario.Personalizado)
        {
            // Perfil pronto: as caixinhas só mostram o que o perfil permite (e ficam travadas na tela).
            var permitido = Permissoes.DoPerfil(value.Perfil);
            foreach (var i in PermissoesLista) i.Marcada = (permitido & i.Permissao) == i.Permissao;
        }
    }

    partial void OnSelecionadoChanged(Funcionario? value)
    {
        if (_silenciar || value is null) return;
        EditandoId = value.Id;
        EditandoAtivo = value.Ativo;
        Nome = value.Nome;
        Cargo = value.Cargo ?? "";
        Telefone = value.Telefone ?? "";
        PinNovo = PinConfirmacao = "";
        PerfilSelecionado = Perfis.First(p => p.Perfil == value.Perfil);
        if (value.Perfil == PerfilFuncionario.Personalizado)
            foreach (var i in PermissoesLista) i.Marcada = (value.PermissoesPersonalizadas & i.Permissao) == i.Permissao;
        OnPropertyChanged(nameof(EhAdministradorEditado));
        Mensagem = "";
    }

    [RelayCommand]
    private void Novo()
    {
        EditandoId = null;
        EditandoAtivo = true;
        Nome = Cargo = Telefone = PinNovo = PinConfirmacao = "";
        PerfilSelecionado = LoginAtivo ? Perfis[3] : Perfis[0]; // novo funcionário começa como Vendedor; o primeiro é administrador
        Mensagem = "";
        _silenciar = true;
        Selecionado = null;
        _silenciar = false;
        OnPropertyChanged(nameof(EhAdministradorEditado));
    }

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        LoginAtivo = await _servico.ExigeLoginAsync();
        var lista = await _servico.ListarAsync(MostrarInativos);
        var id = EditandoId;
        _silenciar = true;
        Funcionarios.Clear();
        foreach (var f in lista) Funcionarios.Add(f);
        Selecionado = Funcionarios.FirstOrDefault(f => f.Id == id);
        _silenciar = false;
        if (!LoginAtivo) Novo();
    });

    private Permissao PermissoesMarcadas() =>
        PermissoesLista.Where(i => i.Marcada).Aggregate(Permissao.Nenhuma, (soma, i) => soma | i.Permissao);

    private void ConferirPin(bool obrigatorio)
    {
        if (PinNovo.Length == 0 && PinConfirmacao.Length == 0 && !obrigatorio) return;
        FuncionarioService.ValidarPin(PinNovo);
        if (PinNovo != PinConfirmacao) throw new RegraDeNegocioException("O PIN e a confirmação não são iguais.");
    }

    // ================= ativar o login =================

    [RelayCommand]
    private Task AtivarLoginAsync() => ExecutarAsync(async () =>
    {
        ConferirPin(obrigatorio: true);
        var (admin, codigo) = await _servico.CriarPrimeiroAdministradorAsync(Nome, PinNovo);
        Sessao.Entrar(admin); // quem ativou já fica logado
        Avisar("Código de recuperação",
            $"Login ativado! Administrador: {admin.Nome}.{Environment.NewLine}{Environment.NewLine}" +
            $"Seu código de recuperação (para redefinir o PIN se esquecer):{Environment.NewLine}{Environment.NewLine}{codigo}{Environment.NewLine}{Environment.NewLine}" +
            "Anote em lugar seguro: ele só aparece agora.");
        Novo();
        await CarregarAsync();
        MostrarSucesso("Login ativado. Agora o programa pede o PIN ao abrir.");
    });

    // ================= cadastro =================

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        if (!LoginAtivo) { await AtivarLoginAsync(); return; }

        var dados = new DadosFuncionario(Nome, PerfilSelecionado.Perfil, PermissoesMarcadas(), Cargo, Telefone);
        if (EditandoId is int id)
        {
            ConferirPin(obrigatorio: false);
            await _servico.EditarAsync(id, dados);
            if (PinNovo.Length > 0) await _servico.DefinirPinAsync(id, PinNovo);
            await AtualizarSessaoSeForOUsuarioAsync(id);
            Limpar(id);
            await CarregarAsync();
            MostrarSucesso("Funcionário atualizado.");
        }
        else
        {
            ConferirPin(obrigatorio: true);
            var novo = await _servico.CadastrarAsync(dados, PinNovo);
            Novo();
            await CarregarAsync();
            MostrarSucesso($"Funcionário \"{novo.Nome}\" cadastrado.");
        }
    });

    private void Limpar(int id)
    {
        PinNovo = PinConfirmacao = "";
        EditandoId = id;
    }

    private async Task AtualizarSessaoSeForOUsuarioAsync(int id)
    {
        if (Sessao.Atual?.Id != id) return;
        var atualizado = (await _servico.ListarAsync(incluirInativos: true)).First(f => f.Id == id);
        Sessao.Entrar(atualizado); // as permissões novas valem já
    }

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (EditandoId is not int id) return;
        if (Sessao.Atual?.Id == id && EditandoAtivo) throw new RegraDeNegocioException("Você não pode desativar o próprio usuário enquanto está usando o programa.");
        if (EditandoAtivo) await _servico.DesativarAsync(id);
        else await _servico.ReativarAsync(id);
        EditandoAtivo = !EditandoAtivo;
        await CarregarAsync();
        MostrarSucesso(EditandoAtivo ? "Funcionário reativado." : "Funcionário desativado (não consegue mais entrar).");
    });

    [RelayCommand]
    private Task NovoCodigoAsync() => ExecutarAsync(async () =>
    {
        if (EditandoId is not int id || !EhAdministradorEditado) return;
        var codigo = await _servico.NovoCodigoDeRecuperacaoAsync(id);
        Avisar("Novo código de recuperação",
            $"Novo código de recuperação de {Nome}:{Environment.NewLine}{Environment.NewLine}{codigo}{Environment.NewLine}{Environment.NewLine}" +
            "O código antigo deixou de valer. Anote em lugar seguro: ele só aparece agora.");
    });
}
