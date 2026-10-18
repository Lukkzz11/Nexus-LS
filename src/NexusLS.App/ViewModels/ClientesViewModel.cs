using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Cadastro de clientes (nome, documento, telefone, observação) com o histórico de compras de cada um.</summary>
public partial class ClientesViewModel : ViewModelBase
{
    private readonly ClienteService _clientes;
    private int _versaoBusca;
    private bool _silenciar; // true enquanto a seleção da lista é refeita pelo programa (não pelo operador)

    public ClientesViewModel(ClienteService clientes)
    {
        _clientes = clientes;
        Novo();
        _ = BuscarAsync();
    }

    public ObservableCollection<Cliente> Clientes { get; } = new();
    public ObservableCollection<Venda> Compras { get; } = new();

    // ----- Lista -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private Cliente? _selecionado;

    // ----- Cadastro -----
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _documento = "";
    [ObservableProperty] private string _telefone = "";
    [ObservableProperty] private string _observacao = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaEditando))]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    private int? _idEditando; // null = cadastrando um cliente novo

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoBotaoAtivo))]
    private bool _clienteAtivo = true;

    // ----- Histórico do cliente aberto -----
    [ObservableProperty] private string _resumoCompras = "";
    [ObservableProperty] private string _resumoAPrazo = "";

    public bool EstaEditando => IdEditando is not null;
    public string TituloFormulario => EstaEditando ? "Editar cliente" : "Novo cliente";
    public string TextoBotaoAtivo => ClienteAtivo ? "Desativar" : "Reativar";

    partial void OnBuscaChanged(string value) => _ = BuscarAsync();
    partial void OnMostrarInativosChanged(bool value) => _ = BuscarAsync();

    /// <summary>O operador escolheu um cliente na lista: abre o cadastro e o histórico dele.</summary>
    partial void OnSelecionadoChanged(Cliente? value)
    {
        if (_silenciar || value is null) return; // a lista foi recarregada: não apaga o que está sendo editado
        _ = ExecutarAsync(() => AbrirAsync(value));
    }

    [RelayCommand]
    private Task BuscarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versaoBusca; // se o operador digita rápido, só a busca mais recente atualiza a lista
        var lista = await _clientes.BuscarAsync(Busca, MostrarInativos);
        if (versao != _versaoBusca) return;

        Clientes.Clear();
        foreach (var c in lista) Clientes.Add(c);
        if (IdEditando is int id) Reselecionar(id);
    });

    private void Reselecionar(int id)
    {
        _silenciar = true;
        try { Selecionado = Clientes.FirstOrDefault(c => c.Id == id); }
        finally { _silenciar = false; }
    }

    [RelayCommand]
    private void Novo()
    {
        _silenciar = true;
        try { Selecionado = null; }
        finally { _silenciar = false; }

        IdEditando = null;
        Nome = "";
        Documento = "";
        Telefone = "";
        Observacao = "";
        ClienteAtivo = true;
        ResumoCompras = "";
        ResumoAPrazo = "";
        Compras.Clear();
        Mensagem = "";
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var dados = new DadosCliente(Nome, Documento, Telefone, Observacao);

        if (IdEditando is int id)
        {
            await _clientes.EditarAsync(id, dados);
            await BuscarAsync();
            await CarregarHistoricoAsync(id);
            MostrarSucesso("Cliente atualizado.");
        }
        else
        {
            var novo = await _clientes.CadastrarAsync(dados);
            await BuscarAsync();
            await AbrirAsync(novo);
            MostrarSucesso("Cliente cadastrado.");
        }
    });

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;

        var estavaAtivo = ClienteAtivo;
        if (estavaAtivo) await _clientes.DesativarAsync(id);
        else await _clientes.ReativarAsync(id);

        Novo();
        await BuscarAsync();
        MostrarSucesso(estavaAtivo ? "Cliente desativado." : "Cliente reativado.");
    });

    private async Task AbrirAsync(Cliente cliente)
    {
        IdEditando = cliente.Id;
        Nome = cliente.Nome;
        Documento = cliente.Documento ?? "";
        Telefone = cliente.Telefone ?? "";
        Observacao = cliente.Observacao ?? "";
        ClienteAtivo = cliente.Ativo;
        Mensagem = "";

        await CarregarHistoricoAsync(cliente.Id);
        Reselecionar(cliente.Id);
    }

    private async Task CarregarHistoricoAsync(int clienteId)
    {
        var h = await _clientes.HistoricoAsync(clienteId);

        Compras.Clear();
        foreach (var v in h.Vendas) Compras.Add(v);

        ResumoCompras = h.Compras == 0
            ? "Ainda não comprou."
            : $"{h.Compras} compra(s) · Total {Dinheiro.Formatar(h.TotalCompradoCentavos)} · Última em {h.UltimaCompra:dd/MM/yyyy}";
        ResumoAPrazo = h.TotalAPrazoCentavos > 0
            ? $"Vendido a prazo: {Dinheiro.Formatar(h.TotalAPrazoCentavos)} (o programa não controla os recebimentos)"
            : "";
    }
}
