using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>As respostas do cancelamento: o motivo e se os itens voltam ao estoque.</summary>
public record DadosCancelamento(string Motivo, bool DevolverEstoque);

/// <summary>Uma forma de pagamento no relatório do dia, com os textos prontos.</summary>
public record LinhaFormaDoDia(string Forma, string Total, int Vendas);

public partial class VendasViewModel : ViewModelBase
{
    private readonly VendaService _vendas;
    private readonly CupomService _cupons;
    private readonly CaixaService? _caixa;
    private int _versao;

    /// <summary>
    /// O Caixa reúne o dia (vendas, formas de pagamento) e o fechamento. O <paramref name="fechamento"/> é opcional
    /// para que os testes e as capturas continuem montando só a lista de vendas.
    /// </summary>
    public VendasViewModel(VendaService vendas, CupomService cupons, CaixaService? caixa = null, FechamentoViewModel? fechamento = null)
    {
        _vendas = vendas;
        _cupons = cupons;
        _caixa = caixa;
        Fechamento = fechamento;
        _data = DateTime.Today; // campo direto: não dispara o carregamento duas vezes
        _ = CarregarAsync();
    }

    /// <summary>A tela define como perguntar "tem certeza?" (ver PdvViewModel).</summary>
    public Func<string, bool> Confirmar { get; set; } = _ => true;

    /// <summary>
    /// A tela define como pedir o motivo do cancelamento (e se o estoque volta). Recebe a pergunta e devolve
    /// as respostas, ou nulo se a pessoa desistiu. O padrão (usado nos testes) responde com um motivo fixo.
    /// </summary>
    public Func<string, DadosCancelamento?> PedirCancelamento { get; set; } = _ => new DadosCancelamento("Cancelamento pelo operador", true);

    /// <summary>
    /// A tela define como o operador escolhe onde salvar o arquivo (caixa "Salvar como"). Recebe o nome sugerido
    /// e devolve o caminho escolhido, ou nulo se a pessoa cancelou.
    /// </summary>
    public Func<string, string?> EscolherArquivoParaSalvar { get; set; } = _ => null;

    public ObservableCollection<Venda> Vendas { get; } = new();

    /// <summary>Abrir, movimentar, conferir e fechar o caixa (dentro da mesma tela). Nulo = só a lista de vendas.</summary>
    public FechamentoViewModel? Fechamento { get; }
    public bool TemFechamento => Fechamento is not null;

    /// <summary>Quanto entrou em cada forma de pagamento no dia mostrado (cartões do relatório do dia).</summary>
    public ObservableCollection<LinhaFormaDoDia> FormasDoDia { get; } = new();

    [ObservableProperty] private string _totalDiaTexto = "R$ 0,00";
    [ObservableProperty] private string _quantidadeVendasTexto = "0";
    [ObservableProperty] private string _ticketMedioTexto = "R$ 0,00";
    [ObservableProperty] private string _canceladasTexto = "";

    [ObservableProperty] private DateTime? _data;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PodeCancelar))]
    [NotifyPropertyChangedFor(nameof(TemSelecao))]
    [NotifyPropertyChangedFor(nameof(TextoOperador))]
    [NotifyPropertyChangedFor(nameof(TextoCliente))]
    [NotifyPropertyChangedFor(nameof(TextoCancelamento))]
    [NotifyCanExecuteChangedFor(nameof(CancelarVendaCommand))]
    private Venda? _selecionada;

    [ObservableProperty] private string _resumo = "";
    [ObservableProperty] private string _resumoFormas = "";

    public bool TemSelecao => Selecionada is not null;

    /// <summary>Linha de texto sobre quem vendeu (vazia se a venda é antiga e não tem operador).</summary>
    public string TextoOperador => Selecionada?.Operador is { Length: > 0 } o ? $"Operador: {o}" : "";

    /// <summary>Para quem foi a venda (a prazo ou com cliente). Venda sem cliente mostra "Consumidor final".</summary>
    public string TextoCliente
    {
        get
        {
            if (Selecionada is not { } v) return "";
            if (v.Cliente is { } c)
                return $"Cliente: {c.Nome}" + (string.IsNullOrWhiteSpace(c.Telefone) ? "" : $"  ·  {c.Telefone}");
            return v.FormaPagamento == FormaPagamento.APrazo ? "Cliente: (não identificado)" : "Cliente: Consumidor final";
        }
    }

    /// <summary>Quem cancelou, quando, por quê e se o estoque voltou. Vazio se a venda não foi cancelada.</summary>
    public string TextoCancelamento => Selecionada is { Cancelada: true } v
        ? $"Cancelada em {v.DataCancelamento:dd/MM/yyyy HH:mm}"
          + (string.IsNullOrWhiteSpace(v.CanceladoPor) ? "" : $" por {v.CanceladoPor}")
          + (string.IsNullOrWhiteSpace(v.MotivoCancelamento) ? "" : $"{Environment.NewLine}Motivo: {v.MotivoCancelamento}")
          + (v.EstoqueDevolvido ? $"{Environment.NewLine}Estoque devolvido." : $"{Environment.NewLine}Estoque NÃO foi devolvido.")
        : "";
    public bool PodeCancelar => Selecionada is { Cancelada: false } && NexusLS.App.Seguranca.Sessao.Pode(NexusLS.Core.Models.Permissao.CancelarVendas);

    partial void OnDataChanged(DateTime? value) => _ = CarregarAsync();

    [RelayCommand]
    private Task CarregarAsync() => ExecutarAsync(async () =>
    {
        var versao = ++_versao;
        var idSelecionado = Selecionada?.Id;

        var lista = await _vendas.ListarDoDiaAsync(Data ?? DateTime.Today);
        if (versao != _versao) return;

        Vendas.Clear();
        foreach (var v in lista) Vendas.Add(v);

        var validas = lista.Count(v => !v.Cancelada);
        var canceladas = lista.Count - validas;
        Resumo = $"{validas} venda(s) · Total: {Dinheiro.Formatar(VendaService.TotalValido(lista))}"
                 + (canceladas > 0 ? $" · {canceladas} cancelada(s) (não somadas)" : "");

        // Quanto entrou em cada forma de pagamento (útil para fechar o caixa: o dinheiro tem que bater com a gaveta).
        ResumoFormas = string.Join("   ·   ", RelatorioService.ResumoPorForma(lista)
            .Select(r => $"{SelecaoPagamento.Nome(r.Forma)}: {Dinheiro.Formatar(r.TotalCentavos)} ({r.Vendas})"));

        var totalValido = VendaService.TotalValido(lista);
        TotalDiaTexto = Dinheiro.Formatar(totalValido);
        QuantidadeVendasTexto = validas.ToString();
        TicketMedioTexto = Dinheiro.Formatar(validas == 0 ? 0 : totalValido / validas);
        CanceladasTexto = canceladas == 0 ? "" : $"{canceladas} cancelada(s), não somadas";
        FormasDoDia.Clear();
        foreach (var r in RelatorioService.ResumoPorForma(lista))
            FormasDoDia.Add(new LinhaFormaDoDia(SelecaoPagamento.Nome(r.Forma), Dinheiro.Formatar(r.TotalCentavos), r.Vendas));

        if (idSelecionado is int id) Selecionada = Vendas.FirstOrDefault(v => v.Id == id);
    });

    /// <summary>Salva as vendas do dia mostrado num CSV (uma linha por venda), para abrir no Excel.</summary>
    [RelayCommand]
    private Task ExportarVendasAsync() => ExportarAsync("vendas", ExportadorCsv.Vendas);

    /// <summary>Salva os itens vendidos no dia num CSV (uma linha por item), para abrir no Excel.</summary>
    [RelayCommand]
    private Task ExportarItensAsync() => ExportarAsync("itens", ExportadorCsv.Itens);

    private Task ExportarAsync(string tipo, Func<IEnumerable<Venda>, string> gerar) => ExecutarAsync(() =>
    {
        if (Vendas.Count == 0)
            throw new RegraDeNegocioException("Não há vendas neste dia para exportar.");

        var dia = (Data ?? DateTime.Today).ToString("yyyy-MM-dd");
        var caminho = EscolherArquivoParaSalvar($"{tipo}-{dia}.csv");
        if (caminho is null) return Task.CompletedTask; // a pessoa desistiu na caixa "Salvar como"

        ExportadorCsv.Salvar(caminho, gerar(Vendas.ToList()));
        MostrarSucesso($"Arquivo salvo: {caminho}");
        return Task.CompletedTask;
    });

    /// <summary>Imprime de novo o cupom da venda selecionada (se a venda foi cancelada, o cupom sai marcado como cancelado).</summary>
    [RelayCommand]
    private Task ReimprimirCupomAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } venda) return;
        await _cupons.ImprimirAsync(venda.Id);
        MostrarSucesso($"Cupom da venda #{venda.Id} enviado para a impressora.");
    });

    /// <summary>Salva a nota da venda selecionada em PDF.</summary>
    [RelayCommand]
    private Task NotaEmPdfAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { } venda) return;
        var caminho = EscolherArquivoParaSalvar($"nota-{venda.Id}.pdf");
        if (caminho is null) return;
        await File.WriteAllBytesAsync(caminho, await _cupons.GerarPdfAsync(venda.Id));
        MostrarSucesso($"Nota salva: {caminho}");
    });

    [RelayCommand(CanExecute = nameof(PodeCancelar))]
    private Task CancelarVendaAsync() => ExecutarAsync(async () =>
    {
        if (Selecionada is not { Cancelada: false } venda) return;
        if (!NexusLS.App.Seguranca.Sessao.Pode(NexusLS.Core.Models.Permissao.CancelarVendas))
            throw new RegraDeNegocioException("Seu perfil não pode cancelar vendas. Peça a um gerente ou administrador.");

        var pergunta = $"Cancelar a venda #{venda.Id} de {Dinheiro.Formatar(venda.TotalCentavos)}?";
        if (_caixa is not null && await _caixa.TurnoFechadoDoHorarioAsync(venda.DataHora) is { } fechado)
            pergunta += $"{Environment.NewLine}ATENÇÃO: esta venda é do caixa nº {fechado.Id}, que já foi fechado. "
                        + "O fechamento antigo não muda; depois do cancelamento, refaça a conferência desse caixa.";
        var dados = PedirCancelamento(pergunta);
        if (dados is null) return;

        await _vendas.CancelarAsync(venda.Id, dados.Motivo, dados.DevolverEstoque);
        await CarregarAsync();
        if (Fechamento is not null) await Fechamento.AtualizarCommand.ExecuteAsync(null);   // o esperado do caixa muda com o cancelamento
        MostrarSucesso($"Venda #{venda.Id} cancelada" + (dados.DevolverEstoque ? " e estoque devolvido." : "; o estoque não foi alterado."));
    });
}
