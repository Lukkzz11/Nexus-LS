using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

public partial class ProdutosViewModel : ViewModelBase
{
    private readonly ProdutoService _servico;
    private int _versaoBusca;

    public ProdutosViewModel(ProdutoService servico)
    {
        _servico = servico;
        Novo();
        _ = BuscarAsync();
    }

    /// <summary>
    /// ObservableCollection avisa a tela quando itens entram ou saem, então a lista se atualiza sozinha.
    /// (Uma List comum não faz isso.)
    /// </summary>
    public ObservableCollection<Produto> Produtos { get; } = new();

    // ----- Busca e lista -----
    [ObservableProperty] private string _busca = "";
    [ObservableProperty] private bool _mostrarInativos;
    [ObservableProperty] private Produto? _selecionado;

    // ----- Formulário (tudo como texto: é o que o operador digita; a conversão acontece ao salvar) -----
    [ObservableProperty] private string _nome = "";
    [ObservableProperty] private string _codigoBarras = "";
    [ObservableProperty] private string _precoVenda = "";
    [ObservableProperty] private string _precoCusto = "";
    [ObservableProperty] private string _estoqueMinimo = "0";
    [ObservableProperty] private string _estoqueInicial = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EstaEditando))]
    [NotifyPropertyChangedFor(nameof(EhNovo))]
    [NotifyPropertyChangedFor(nameof(TituloFormulario))]
    private int? _idEditando; // null = cadastrando um produto novo

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TextoBotaoAtivo))]
    private bool _produtoAtivo = true;

    [ObservableProperty] private int _estoqueAtualEditando;

    public bool EstaEditando => IdEditando is not null;
    public bool EhNovo => !EstaEditando;
    public string TituloFormulario => EstaEditando ? "Editar produto" : "Novo produto";
    public string TextoBotaoAtivo => ProdutoAtivo ? "Desativar" : "Reativar";

    // Métodos "partial void On...Changed" são ganchos gerados pelo toolkit: rodam quando a propriedade muda.
    partial void OnBuscaChanged(string value) => _ = BuscarAsync();

    partial void OnMostrarInativosChanged(bool value) => _ = BuscarAsync();

    partial void OnSelecionadoChanged(Produto? value)
    {
        if (value is null) return; // a lista foi recarregada; não apaga o que está sendo editado

        IdEditando = value.Id;
        Nome = value.Nome;
        CodigoBarras = value.CodigoBarras ?? "";
        PrecoVenda = Dinheiro.FormatarParaEdicao(value.PrecoVendaCentavos);
        PrecoCusto = Dinheiro.FormatarParaEdicao(value.PrecoCustoCentavos);
        EstoqueMinimo = value.EstoqueMinimo.ToString(CultureInfo.InvariantCulture);
        EstoqueAtualEditando = value.EstoqueAtual;
        ProdutoAtivo = value.Ativo;
        Mensagem = "";
    }

    [RelayCommand]
    private async Task BuscarAsync()
    {
        // Se o operador digita rápido, várias buscas ficam "no ar". Só a mais recente pode atualizar a lista.
        var versao = ++_versaoBusca;
        await ExecutarAsync(async () =>
        {
            var lista = await _servico.BuscarAsync(Busca, MostrarInativos);
            if (versao != _versaoBusca) return;
            Produtos.Clear();
            foreach (var p in lista) Produtos.Add(p);
        });
    }

    [RelayCommand]
    private void Novo()
    {
        Selecionado = null;
        IdEditando = null;
        Nome = "";
        CodigoBarras = "";
        PrecoVenda = "";
        PrecoCusto = "";
        EstoqueMinimo = "0";
        EstoqueInicial = "0";
        EstoqueAtualEditando = 0;
        ProdutoAtivo = true;
        Mensagem = "";
    }

    [RelayCommand]
    private Task SalvarAsync() => ExecutarAsync(async () =>
    {
        var (dados, estoqueInicial) = LerFormulario();

        if (IdEditando is int id)
        {
            await _servico.EditarAsync(id, dados);
            await BuscarAsync();
            Selecionado = Produtos.FirstOrDefault(p => p.Id == id);
            MostrarSucesso("Produto atualizado.");
        }
        else
        {
            await _servico.CadastrarAsync(dados, estoqueInicial);
            await BuscarAsync();
            Novo();
            MostrarSucesso("Produto cadastrado.");
        }
    });

    [RelayCommand]
    private Task AlternarAtivoAsync() => ExecutarAsync(async () =>
    {
        if (IdEditando is not int id) return;

        var estavaAtivo = ProdutoAtivo;
        if (estavaAtivo) await _servico.DesativarAsync(id);
        else await _servico.ReativarAsync(id);

        await BuscarAsync();
        Novo();
        MostrarSucesso(estavaAtivo ? "Produto desativado." : "Produto reativado.");
    });

    private (DadosProduto dados, int estoqueInicial) LerFormulario()
    {
        if (!Dinheiro.TentarConverter(PrecoVenda, out var venda))
            throw new RegraDeNegocioException("Preço de venda inválido. Exemplo: 12,50");

        long custo = 0;
        if (!string.IsNullOrWhiteSpace(PrecoCusto) && !Dinheiro.TentarConverter(PrecoCusto, out custo))
            throw new RegraDeNegocioException("Preço de custo inválido. Exemplo: 8,00");

        var minimo = LerInteiro(EstoqueMinimo, "Estoque mínimo");
        var inicial = EhNovo ? LerInteiro(EstoqueInicial, "Estoque inicial") : 0;

        return (new DadosProduto(Nome, CodigoBarras, venda, custo, minimo), inicial);
    }

    private static int LerInteiro(string texto, string campo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor))
            throw new RegraDeNegocioException($"{campo} inválido. Use um número inteiro, sem sinal.");
        return valor;
    }
}
