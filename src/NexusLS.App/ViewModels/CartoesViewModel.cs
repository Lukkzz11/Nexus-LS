using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Financeiro;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public record LinhaCartaoAliquidar(CartaoAliquidar Cartao)
{
    public int VendaId => Cartao.VendaId;
    public string Venda => $"#{Cartao.VendaId}";
    public string Data => Cartao.DataVenda.ToString("dd/MM/yyyy");
    public string Forma => Cartao.Forma == FormaPagamento.CartaoDebito ? "Débito" : $"Crédito {Cartao.Parcelas}x";
    public string Valor => Dinheiro.Formatar(Cartao.TotalCentavos);
    public string Previsao => Cartao.PrevisaoDeCaida.ToString("dd/MM/yyyy");
}

public record LinhaCartaoLiquidado(CartaoLiquidado Liquidado)
{
    public int VendaId => Liquidado.VendaId;
    public string Venda => $"#{Liquidado.VendaId}";
    public string Data => Liquidado.DataLiquidacao.ToString("dd/MM/yyyy");
    public string Valor => Dinheiro.Formatar(Liquidado.TotalCentavos);
    public string Taxa => Dinheiro.Formatar(Liquidado.TaxaCentavos);
    public string Conta => Liquidado.Conta;
}

/// <summary>Vendas no cartão que ainda não caíram na conta, e as já liquidadas (que dá para estornar).</summary>
public partial class CartoesViewModel : ViewModelBase
{
    private readonly LiquidacaoCartaoService _servico;
    private readonly ContaFinanceiraService _contas;
    private readonly Action _aoMudar;
    private readonly Func<DateTime> _agora;

    public CartoesViewModel(LiquidacaoCartaoService servico, ContaFinanceiraService contas, Action? aoMudar = null, Func<DateTime>? agora = null)
    {
        _servico = servico;
        _contas = contas;
        _aoMudar = aoMudar ?? (() => { });
        _agora = agora ?? (() => DateTime.Now);
        _data = _agora().Date;
        Carregamento = CarregarAsync();
    }

    public Task Carregamento { get; }
    public Func<string, string?> PedirMotivo { get; set; } = _ => null;

    public ObservableCollection<LinhaCartaoAliquidar> Aliquidar { get; } = new();
    public ObservableCollection<LinhaCartaoLiquidado> Liquidados { get; } = new();
    public ObservableCollection<ContaFinanceira> Contas { get; } = new();

    [ObservableProperty] private LinhaCartaoAliquidar? _selecionado;
    [ObservableProperty] private LinhaCartaoLiquidado? _liquidadoSelecionado;
    [ObservableProperty] private ContaFinanceira? _conta;
    [ObservableProperty] private DateTime? _data;
    [ObservableProperty] private string _taxaTexto = "";
    [ObservableProperty] private string _resumo = "";

    partial void OnSelecionadoChanged(LinhaCartaoAliquidar? value)
    {
        if (value is null) return;
        // Sugere o dia previsto, mas nunca no futuro (o dinheiro só se registra quando já caiu).
        Data = value.Cartao.PrevisaoDeCaida.Date > _agora().Date ? _agora().Date : value.Cartao.PrevisaoDeCaida.Date;
    }

    [RelayCommand]
    private Task LiquidarAsync() => ExecutarAsync(async () =>
    {
        if (Selecionado is null) throw new RegraDeNegocioException("Escolha a venda que já caiu na conta.");
        if (Conta is null) throw new RegraDeNegocioException("Escolha a conta onde o dinheiro caiu.");
        if (Data is not DateTime data) throw new RegraDeNegocioException("Informe o dia em que caiu.");
        long taxa = 0;
        if (!string.IsNullOrWhiteSpace(TaxaTexto) && !Dinheiro.TentarConverter(TaxaTexto, out taxa))
            throw new RegraDeNegocioException("A taxa não é um valor válido.");

        var venda = Selecionado.Venda;
        await _servico.LiquidarAsync(Selecionado.VendaId, data, Conta.Id, taxa);
        TaxaTexto = "";
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso($"Venda {venda} liquidada em {Conta.Nome}.");
    });

    [RelayCommand]
    private Task EstornarAsync() => ExecutarAsync(async () =>
    {
        if (LiquidadoSelecionado is not { } l) throw new RegraDeNegocioException("Escolha a liquidação que será estornada.");
        var motivo = PedirMotivo($"Estornar a liquidação da venda {l.Venda} ({l.Valor})?\nO dinheiro volta a ser \"cartão a liquidar\". Informe o motivo:");
        if (motivo is null) return;
        await _servico.EstornarAsync(l.VendaId, motivo);
        await CarregarAsync();
        _aoMudar();
        MostrarSucesso("Liquidação estornada.");
    });

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var hoje = _agora().Date;
        var lista = await _servico.ListarAliquidarAsync();
        Aliquidar.Clear();
        foreach (var c in lista) Aliquidar.Add(new LinhaCartaoAliquidar(c));
        Resumo = lista.Count == 0 ? "Nenhuma venda no cartão esperando cair."
            : $"{lista.Count} venda(s) esperando cair · {Dinheiro.Formatar(lista.Sum(c => c.TotalCentavos))}";

        Liquidados.Clear();
        foreach (var l in await _servico.ListarLiquidadosAsync(hoje.AddDays(-60), hoje)) Liquidados.Add(new LinhaCartaoLiquidado(l));

        var anterior = Conta?.Id;
        var bancos = (await _contas.ListarAsync()).Where(c => c.Tipo == TipoContaFin.Banco).ToList();
        Contas.Clear();
        foreach (var c in bancos) Contas.Add(c);
        Conta = bancos.FirstOrDefault(c => c.Id == anterior) ?? bancos.FirstOrDefault(c => c.Padrao) ?? bancos.FirstOrDefault();
    });
}
