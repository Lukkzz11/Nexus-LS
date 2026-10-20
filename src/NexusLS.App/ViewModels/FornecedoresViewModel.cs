using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Cadastro de fornecedores: nome, CNPJ/CPF, telefone, e-mail e observação. Fornecedor desativado some da busca.</summary>
public partial class FornecedoresViewModel : ViewModelBase
{
    private readonly FornecedorService _fornecedores;
    private int _versaoBusca;
    private bool _silenciar;

    public FornecedoresViewModel(FornecedorService fornecedores)
    {
        _fornecedores = fornecedores;
        Novo();
        Carregamento = BuscarAsync();
    }

    public Task Carregamento { get; }

    public ObservableCollection<Fornecedor> Fornecedores { get; } = new();

    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private Fornecedor? _selecionado;

    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _documento = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _observacao = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    [NotifyPropertyChangedFor(nameof(Editando))]
    [NotifyPropertyChangedFor(nameof(TextoAtivar))]
    private int? _editandoId;

    [ObservableProperty] private bool _editandoAtivo = true;

    public bool Editando => EditandoId is not null;
    public string TituloFormulario => Editando ? "Editar fornecedor" : "Novo fornecedor";
    public string TextoAtivar => EditandoAtivo ? "Desativar" : "Reativar";

    partial void OnBuscaChanged(string value) => _ = BuscarAsync();
    partial void OnMostrarInativosChanged(bool value) => _ = BuscarAsync();

    partial void OnSelecionadoChanged(Fornecedor? value)
    {
        if (_silenciar || value is null) return;
        EditandoId = value.Id;
        EditandoAtivo = value.Ativo;
        Nome = value.Nome;
        Documento = value.Documento ?? "";
        Telefone = value.Telefone ?? "";
        Email = value.Email ?? "";
        Observacao = value.Observacao ?? "";
        Mensagem = "";
    }

    [RelayCommand]
    private void Novo()
    {
        EditandoId = null;
        EditandoAtivo = true;
        Nome = Documento = Telefone = Email = Observacao = "";
        Mensagem = "";
        _silenciar = true;
        Selecionado = null;
        _silenciar = false;
    }

    [RelayCommand]
    private Task BuscarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versaoBusca;
        var lista = await _fornecedores.BuscarAsync(Busca, MostrarInativos);
        if (versao != _versaoBusca) return;
        var id = EditandoId;
        _silenciar = true;
        Fornecedores.Clear();
        foreach (var f in lista) Fornecedores.Add(f);
        Selecionado = Fornecedores.FirstOrDefault(f => f.Id == id);
        _silenciar = false;
    });

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var dados = new DadosFornecedor(Nome, Documento, Telefone, Email, Observacao);
        if (EditandoId is int id)
        {
            await _fornecedores.EditarAsync(id, dados);
            await BuscarAsync();
            MostrarSucesso("Fornecedor atualizado.");
        }
        else
        {
            var novo = await _fornecedores.CadastrarAsync(dados);
            await BuscarAsync();
            MostrarSucesso($"Fornecedor \"{novo.Nome}\" cadastrado.");
            Novo();
            MostrarSucesso($"Fornecedor \"{novo.Nome}\" cadastrado.");
        }
    });

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (EditandoId is not int id) return;
        if (EditandoAtivo) await _fornecedores.DesativarAsync(id);
        else await _fornecedores.ReativarAsync(id);
        EditandoAtivo = !EditandoAtivo;
        await BuscarAsync();
        MostrarSucesso(EditandoAtivo ? "Fornecedor reativado." : "Fornecedor desativado.");
    });
}
