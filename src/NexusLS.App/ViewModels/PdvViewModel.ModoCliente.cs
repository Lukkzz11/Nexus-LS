using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>
/// Venda Cliente: o MESMO PDV, com o cliente já escolhido. Tabela atacado, pagamento a prazo no nome dele
/// e desconto por peça (R$ 2,00 por unidade, editável) já sugerido, como no ERP.
/// </summary>
public partial class PdvViewModel
{
    /// <summary>Desconto por peça que a Venda Cliente sugere (o operador pode mudar na tela de desconto).</summary>
    /// <summary>Desconto sugerido por peça no modo cliente. O programa troca por uma leitura das Preferências; o padrão é R$ 2,00.</summary>
    public static Func<long> DescontoPorPecaPadrao { get; set; } = () => 200;

    private Cliente? _clienteFixo;

    public bool EmModoCliente => _clienteFixo is not null;

    [ObservableProperty] private string _clienteFixoNome = "";
    [ObservableProperty] private string _clienteFixoDivida = "";

    /// <summary>Dados do cliente da Venda Cliente: desconto por peça que ele ganha, telefone, limite e crédito.</summary>
    [ObservableProperty] private string _clienteFixoInfo = "";

    private void AtualizarInfoDoCliente()
    {
        if (_clienteFixo is not { } c) { ClienteFixoInfo = ""; return; }
        var partes = new List<string> { $"Desconto: {Dinheiro.Formatar(DescontoPorPecaPadrao())} por peça" };
        if (!string.IsNullOrWhiteSpace(c.Telefone)) partes.Add($"Tel. {c.Telefone}");
        partes.Add(c.LimiteCreditoCentavos > 0 ? $"Limite {Dinheiro.Formatar(c.LimiteCreditoCentavos)}" : "Sem limite de crédito");
        if (c.CreditoCentavos > 0) partes.Add($"Crédito {Dinheiro.Formatar(c.CreditoCentavos)}");
        ClienteFixoInfo = string.Join(Environment.NewLine, partes);
    }

    /// <summary>Escolhe o cliente da venda: a partir daí o PDV vende no atacado, a prazo, no nome dele.</summary>
    public Task EntrarModoClienteAsync(Cliente cliente) => ExecutarAsync(async () =>
    {
        _clienteFixo = cliente;
        ClienteFixoNome = cliente.Nome;
        AtualizarInfoDoCliente();
        OnPropertyChanged(nameof(EmModoCliente));
        AplicarTabelaDoCliente();
        await AtualizarDividaDoClienteAsync();
        MostrarSucesso($"Venda para {cliente.Nome}: atacado, a prazo e desconto por peça.");
        FocoSolicitado?.Invoke(AlvoFoco.Busca);
    });

    /// <summary>Volta ao PDV comum (varejo, escolhendo a forma de pagamento). O carrinho continua como está.</summary>
    [RelayCommand]
    private void SairDoModoCliente()
    {
        if (_clienteFixo is null || Etapa != EtapaPdv.Busca) return;
        _clienteFixo = null;
        ClienteFixoNome = "";
        ClienteFixoDivida = "";
        ClienteFixoInfo = "";
        OnPropertyChanged(nameof(EmModoCliente));
        MostrarSucesso("Voltou ao PDV comum.");
        FocoSolicitado?.Invoke(AlvoFoco.Busca);
    }

    /// <summary>Pede à tela principal para escolher outro cliente.</summary>
    public event Action? TrocarClienteSolicitado;

    [RelayCommand]
    private void TrocarCliente()
    {
        if (Etapa == EtapaPdv.Busca) TrocarClienteSolicitado?.Invoke();
    }

    private void AplicarTabelaDoCliente()
    {
        _carrinho.MudarTabela(TabelaPreco.Atacado);
        Sincronizar();
        OnPropertyChanged(nameof(TabelaTexto));
        OnPropertyChanged(nameof(EhAtacado));
    }

    /// <summary>Texto inicial da tela de desconto: R$ 2,00 por peça no modo cliente; vazio no PDV comum.</summary>
    private string DescontoInicial()
    {
        if (_clienteFixo is null) return "";
        var desconto = Math.Min(DescontoPorPecaPadrao() * _carrinho.QuantidadeTotal, _carrinho.SubtotalCentavos);
        return desconto > 0 ? Dinheiro.FormatarParaEdicao(desconto) : "";
    }

    /// <summary>Deixa o pagamento a prazo e no nome do cliente escolhido.</summary>
    private void PrepararPagamentoDoCliente()
    {
        if (_pagamento is null || _clienteFixo is null) return;
        while (_pagamento.Forma != FormaPagamento.APrazo) _pagamento.Proxima();
        _pagamento.ClienteId = _clienteFixo.Id;
        ClientesResultado.Clear();
        ClientesResultado.Add(_clienteFixo);
        ClienteDestacado = _clienteFixo;
    }

    private async Task AtualizarDividaDoClienteAsync()
    {
        if (_clienteFixo is null) return;
        var conta = await _clientes.ContaAsync(_clienteFixo.Id);
        ClienteFixoDivida = conta.SaldoDevedorCentavos > 0
            ? $"Deve {Dinheiro.Formatar(conta.SaldoDevedorCentavos)}"
            : "Sem dívida";
    }

    /// <summary>Depois da venda a prazo: quanto o cliente passou a dever.</summary>
    private async Task<string> DetalheDoClienteAposVendaAsync()
    {
        try
        {
            await AtualizarDividaDoClienteAsync();
            var situacao = ClienteFixoDivida.StartsWith("Deve", StringComparison.Ordinal) ? "d" + ClienteFixoDivida[1..] : "sem dívida";
            return $"  ·  {_clienteFixo!.Nome}: {situacao} (saldo devedor após esta venda)";
        }
        catch (Exception)
        {
            return ""; // a venda já foi gravada; o saldo é só informação
        }
    }
}
