using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

public partial class MainViewModel
{
    private PreparacaoImpressaoViewModel? _preparacaoVM;

    /// <summary>
    /// Seleções já feitas por produto. Ficam guardadas para o operador poder ir ao Estúdio,
    /// voltar ao catálogo e reabrir o modal sem perder as quantidades digitadas.
    /// </summary>
    private readonly Dictionary<int, SelecaoImpressaoViewModel> _selecoesPorProduto = new();

    /// <summary>Cérebro da página de preparação. Criado na primeira vez que é usado.</summary>
    public PreparacaoImpressaoViewModel PreparacaoVM => _preparacaoVM ??= CriarPreparacao();

    /// <summary>
    /// Abre o modal do produto. Existe como propriedade para os testes abrirem o modal
    /// sem WPF e informarem a ação escolhida.
    /// </summary>
    public Func<SelecaoImpressaoViewModel, AcaoProdutoCatalogo> AbrirModalProduto { get; set; } =
        selecao => Views.JanelaProdutoCatalogo.Mostrar(null, selecao);

    private PreparacaoImpressaoViewModel CriarPreparacao()
    {
        var vm = new PreparacaoImpressaoViewModel(_dialogo, _impressoraEtiquetas)
        {
            AbrirModalSelecao = AbrirModalDaPreparacao,
            IrParaEstudio = () =>
            {
                if (PreparacaoVM.Selecao is { } selecao)
                    AbrirNoEstudio(selecao.Produto, PrimeiroTamanhoComQuantidade(selecao));
            },
            IrParaCatalogo = () => TelaAtiva = "Catalogo"
        };

        vm.DefinirImpressoras(ImpressorasInstaladas, ImpressoraSelecionada);
        return vm;
    }

    /// <summary>Duplo clique no catálogo: abre o modal com quantidades por tamanho.</summary>
    [RelayCommand]
    public void AbrirProdutoDoCatalogo(ProdutoItemViewModel? produtoItem)
    {
        if (produtoItem == null) return;

        ProdutoSelecionado = produtoItem;

        if (!_selecoesPorProduto.TryGetValue(produtoItem.Produto.Id, out var selecao))
        {
            selecao = new SelecaoImpressaoViewModel(produtoItem);
            _selecoesPorProduto[produtoItem.Produto.Id] = selecao;
        }

        AplicarAcaoDoModal(selecao, AbrirModalProduto(selecao));
    }

    /// <summary>
    /// Ctrl+P no Estúdio: com um produto aberto, mostra o mesmo modal do catálogo (quantidade por tamanho) e, em
    /// "Ir para Impressão", leva à preparação com o desenho atual do Estúdio. Sem produto, vai direto à fila.
    /// </summary>
    public void ImprimirDoEstudio()
    {
        var produto = DesignerVM.ProdutoAtual;
        var item = produto is null ? null : TodosProdutos.FirstOrDefault(p => p.Produto.Id == produto.Id);
        if (item is null)
        {
            IrParaFilaImpressaoAtalho();
            return;
        }

        if (!_selecoesPorProduto.TryGetValue(item.Produto.Id, out var selecao))
        {
            selecao = new SelecaoImpressaoViewModel(item);
            _selecoesPorProduto[item.Produto.Id] = selecao;
        }

        // Sem nada digitado ainda: começa com 1 do tamanho que está em teste no Estúdio.
        if (!selecao.Linhas.Any(l => l.TemQuantidade))
        {
            var emTeste = DesignerVM.TamanhoParaTeste;
            var linha = selecao.Linhas.FirstOrDefault(l => !l.EhSemTamanho && l.Tamanho == emTeste);
            linha?.DefinirQuantidade(1);
        }

        // "Abrir no Estúdio" ou fechar: continua onde está (o desenho não é recarregado).
        if (AbrirModalProduto(selecao) == AcaoProdutoCatalogo.Impressao)
            MostrarPreparacao(selecao, item, usarModeloDoEstudio: true);
    }

    /// <summary>
    /// "Editar quantidades" dentro da preparação: reabre o mesmo modal com a seleção atual.
    /// A ação de Estúdio é tratada pela própria página (callback IrParaEstudio).
    /// </summary>
    private AcaoProdutoCatalogo AbrirModalDaPreparacao(SelecaoImpressaoViewModel selecao)
    {
        var acao = AbrirModalProduto(selecao);
        if (acao == AcaoProdutoCatalogo.Impressao)
            MostrarPreparacao(selecao);
        return acao;
    }

    private void AplicarAcaoDoModal(SelecaoImpressaoViewModel selecao, AcaoProdutoCatalogo acao)
    {
        switch (acao)
        {
            case AcaoProdutoCatalogo.Estudio:
                AbrirNoEstudio(selecao.Produto, PrimeiroTamanhoComQuantidade(selecao));
                break;

            case AcaoProdutoCatalogo.Impressao:
                MostrarPreparacao(selecao);
                break;
        }
    }

    private static string? PrimeiroTamanhoComQuantidade(SelecaoImpressaoViewModel selecao) =>
        selecao.Linhas.FirstOrDefault(l => l.TemQuantidade && !l.EhSemTamanho)?.Tamanho;

    /// <summary>Abre o produto no Estúdio com o modelo vinculado e o tamanho já escolhido.</summary>
    public void AbrirNoEstudio(Produto produto, string? tamanho = null)
    {
        if (produto == null) return;

        var item = TodosProdutos.FirstOrDefault(p => p.Produto.Id == produto.Id);
        if (item is not null) ProdutoSelecionado = item;

        DesignerVM.DefinirProduto(produto, Config.NomeLoja, carregarEtiquetaSalvaSeHouver: true);

        var escolhido = string.IsNullOrWhiteSpace(tamanho)
            ? DesignerVM.TamanhosParaTesteDisponiveis.FirstOrDefault()
            : tamanho;

        if (!string.IsNullOrWhiteSpace(escolhido) && DesignerVM.TamanhosParaTesteDisponiveis.Contains(escolhido))
            DesignerVM.TamanhoParaTeste = escolhido;

        TelaAtiva = "Estudio";
    }

    /// <summary>
    /// Leva a seleção para a página de preparação usando o modelo vinculado ao produto
    /// (ou o modelo atual do Estúdio quando o produto ainda não tem etiqueta própria).
    /// </summary>
    public void MostrarPreparacao(SelecaoImpressaoViewModel selecao, ProdutoItemViewModel? item = null, bool usarModeloDoEstudio = false)
    {
        if (selecao == null) return;

        // Tipo escolhido no modal (tag, composição, preço...) vale só para esta impressão.
        // Vindo do Estúdio (Ctrl+P), a etiqueta "vinculada" é o desenho que está na tela, mesmo sem salvar.
        var modelo = selecao.TipoEtiquetaSelecionado.Fabrica?.Invoke()
                     ?? (usarModeloDoEstudio ? DesignerVM.Modelo : ModeloDoProduto(selecao.Produto));
        _selecoesPorProduto[selecao.Produto.Id] = selecao;

        PreparacaoVM.DefinirImpressoras(
            ImpressorasInstaladas,
            PreparacaoVM.ImpressoraSelecionada ?? ImpressoraSelecionada);
        PreparacaoVM.Carregar(selecao, modelo, Config.NomeLoja);
        TelaAtiva = "Preparacao";
    }

    private ModeloEtiquetaDesign ModeloDoProduto(Produto produto)
    {
        var salvo = GerenciadorEtiquetasProdutoService.TemEtiquetaPersonalizada(produto.Id)
            ? GerenciadorEtiquetasProdutoService.CarregarEtiquetaProduto(produto.Id)
            : null;

        return salvo ?? DesignerVM.Modelo;
    }

    /// <summary>Item do menu "Preparação": abre a página com a última seleção usada.</summary>
    [RelayCommand]
    public void IrParaPreparacao()
    {
        var selecao = PreparacaoVM.Selecao
                      ?? _selecoesPorProduto.Values.LastOrDefault();

        if (selecao is null)
        {
            TelaAtiva = "Catalogo";
            _dialogo.Informar("Escolha um produto no catálogo para preparar a impressão.");
            return;
        }

        MostrarPreparacao(selecao);
    }
}
