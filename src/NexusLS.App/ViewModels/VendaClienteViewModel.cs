using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Venda Cliente, passo 1: escolher o cliente. Depois o PDV abre em modo cliente (atacado, a prazo).</summary>
public partial class VendaClienteViewModel : ViewModelBase
{
    private const int MaximoNaLista = 30;

    private readonly ClienteService _clientes;
    private readonly Func<Cliente, Task> _escolher;
    private CancellationTokenSource? _cts;

    public VendaClienteViewModel(ClienteService clientes, Func<Cliente, Task> escolher)
    {
        _clientes = clientes;
        _escolher = escolher;
        _ = CarregarAsync();
    }

    public ObservableCollection<LinhaCliente> Clientes { get; } = new();

    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private LinhaCliente? _selecionado;
    [ObservableProperty] private string _resumo = "";

    public record LinhaCliente(Cliente Cliente, string Contato, string Divida, bool Deve);

    partial void OnBuscaChanged(string value) => _ = CarregarAsync();

    /// <summary>Busca por nome, documento ou telefone; mostra quanto cada um já deve.</summary>
    public async Task CarregarAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        try
        {
            await Task.Delay(120, cts.Token);
            var achados = await _clientes.BuscarAsync(Busca);
            var linhas = new List<LinhaCliente>();
            foreach (var c in achados.Take(MaximoNaLista))
            {
                var conta = await _clientes.ContaAsync(c.Id);
                var deve = conta.SaldoDevedorCentavos > 0;
                var contato = string.Join(" · ", new[] { c.Telefone, c.Documento }.Where(t => !string.IsNullOrWhiteSpace(t)));
                linhas.Add(new LinhaCliente(c, contato, deve ? $"Deve {Dinheiro.Formatar(conta.SaldoDevedorCentavos)}" : "Sem dívida", deve));
            }
            if (cts.IsCancellationRequested) return;

            Clientes.Clear();
            foreach (var l in linhas) Clientes.Add(l);
            Selecionado = Clientes.FirstOrDefault();
            Resumo = achados.Count > MaximoNaLista
                ? $"Mostrando {MaximoNaLista} de {achados.Count}. Digite mais para filtrar."
                : achados.Count == 0 ? "Nenhum cliente encontrado. Cadastre em Clientes." : $"{achados.Count} cliente(s)";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MostrarErro("Erro ao buscar clientes: " + ex.Message);
        }
    }

    [RelayCommand]
    private Task VenderAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is null)
        {
            MostrarErro("Escolha um cliente na lista.");
            return;
        }
        await _escolher(Selecionado.Cliente);
    });
}
