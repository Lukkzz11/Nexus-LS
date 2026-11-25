using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NexusLS.BarTender.Models.Designer;
using NexusLS.BarTender.Services;
using NexusLS.Core.Etiquetas;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.ViewModels;

/// <summary>O que o operador escolheu fazer depois de conferir o produto no catálogo.</summary>
public enum AcaoProdutoCatalogo
{
    Nenhuma,
    Estudio,
    Impressao
}

/// <summary>Uma linha de tamanho no modal: quantidade digitável, com validação e total automático.</summary>
public partial class LinhaSelecaoViewModel : ObservableObject
{
    private const int QuantidadeMaxima = 99999;

    public string Tamanho { get; }

    public int Estoque { get; }

    public string TamanhoExibicao => string.IsNullOrWhiteSpace(Tamanho) ? "Único" : Tamanho.Trim();

    public bool EhSemTamanho => string.IsNullOrWhiteSpace(Tamanho);

    public string EstoqueTexto => Estoque > 0 ? $"{Estoque} em estoque" : "Sem saldo";

    public bool TemEstoque => Estoque > 0;

    [ObservableProperty]
    private string _quantidadeTexto = "0";

    [ObservableProperty]
    private bool _quantidadeInvalida;

    [ObservableProperty]
    private string _mensagemErro = "";

    /// <summary>Zero quando o texto é vazio; negativo/inválido é recusado e marcado em vermelho.</summary>
    public int Quantidade
    {
        get
        {
            var texto = (QuantidadeTexto ?? "").Trim();
            if (texto.Length == 0) return 0;
            return int.TryParse(texto, NumberStyles.Integer, CultureInfo.CurrentCulture, out var valor) && valor >= 0
                ? valor
                : 0;
        }
    }

    public bool TemQuantidade => Quantidade > 0;

    public event EventHandler? Alterada;

    public LinhaSelecaoViewModel(string tamanho, int estoque, int quantidade = 0)
    {
        Tamanho = tamanho?.Trim() ?? "";
        Estoque = estoque;
        _quantidadeTexto = Math.Max(0, quantidade).ToString(CultureInfo.CurrentCulture);
    }

    partial void OnQuantidadeTextoChanged(string value)
    {
        Validar();
        Alterada?.Invoke(this, EventArgs.Empty);
    }

    private void Validar()
    {
        var texto = (QuantidadeTexto ?? "").Trim();

        if (texto.Length == 0)
        {
            QuantidadeInvalida = false;
            MensagemErro = "";
            OnPropertyChanged(nameof(Quantidade));
            OnPropertyChanged(nameof(TemQuantidade));
            return;
        }

        if (!int.TryParse(texto, NumberStyles.Integer, CultureInfo.CurrentCulture, out var valor))
        {
            QuantidadeInvalida = true;
            MensagemErro = "Use um número inteiro.";
        }
        else if (valor < 0)
        {
            QuantidadeInvalida = true;
            MensagemErro = "A quantidade não pode ser negativa.";
        }
        else if (valor > QuantidadeMaxima)
        {
            QuantidadeInvalida = true;
            MensagemErro = $"O máximo por tamanho é {QuantidadeMaxima}.";
        }
        else
        {
            QuantidadeInvalida = false;
            MensagemErro = "";
        }

        OnPropertyChanged(nameof(Quantidade));
        OnPropertyChanged(nameof(TemQuantidade));
    }

    public void DefinirQuantidade(int quantidade)
    {
        QuantidadeTexto = Math.Max(0, quantidade).ToString(CultureInfo.CurrentCulture);
    }

    [RelayCommand]
    public void Aumentar() => DefinirQuantidade(Quantidade + 1);

    [RelayCommand]
    public void Diminuir()
    {
        if (Quantidade > 0) DefinirQuantidade(Quantidade - 1);
    }
}

/// <summary>
/// Tipo de etiqueta que o operador quer imprimir agora (tag, composição, preço...). A opção "vinculada"
/// (sem fábrica) usa a etiqueta do produto/Estúdio; as outras geram um modelo pronto só para esta impressão.
/// </summary>
public sealed record OpcaoTipoEtiqueta(string Nome, string Descricao, Func<ModeloEtiquetaDesign>? Fabrica)
{
    public bool EhVinculada => Fabrica is null;

    public static readonly OpcaoTipoEtiqueta Vinculada = new(
        "Etiqueta vinculada ao produto",
        "Usa a etiqueta salva do produto (ou o modelo atual do Estúdio).", null);

    public static readonly OpcaoTipoEtiqueta TagVestuario = new(
        "Tag de roupa · 35 × 60 mm",
        "Marca no topo, tamanho grande, REF e código de barras.", ModeloEtiquetaDesign.CriarTagVestuarioRefTamanho35x60);

    public static readonly OpcaoTipoEtiqueta ComposicaoVertical = new(
        "Composição têxtil · 40 × 70 mm",
        "Composição dos tecidos, CNPJ, tamanho e ícones de lavagem.", ModeloEtiquetaDesign.CriarComposicaoTextilVertical40x70);

    public static readonly OpcaoTipoEtiqueta ComposicaoCompacta = new(
        "Composição compacta · 35 × 50 mm",
        "Composição e lavagem resumida com código de barras.", ModeloEtiquetaDesign.CriarComposicaoTextilCompacta35x50);

    public static readonly OpcaoTipoEtiqueta Vestuario = new(
        "Vestuário completa · 60 × 40 mm",
        "Logo, composição, tamanho, preço de varejo e atacado.", ModeloEtiquetaDesign.CriarVestuario60x40);

    public static readonly OpcaoTipoEtiqueta PrecoGondola = new(
        "Preço de gôndola · 50 × 30 mm",
        "Nome, referência, tamanho, preço em destaque e código de barras.", ModeloEtiquetaDesign.CriarPadrao50x30);

    public static readonly OpcaoTipoEtiqueta Promocao = new(
        "Promoção DE / POR · 50 × 30 mm",
        "Preço 'DE' riscado e preço 'POR' em destaque.", ModeloEtiquetaDesign.CriarPromocaoDePor50x30);

    public static IReadOnlyList<OpcaoTipoEtiqueta> ListaPadrao { get; } =
        new[] { Vinculada, TagVestuario, ComposicaoVertical, ComposicaoCompacta, Vestuario, PrecoGondola, Promocao };
}

/// <summary>
/// Seleção de um produto para impressão: tamanhos, quantidades e os dados que o modal e a
/// preparação mostram. Fica viva enquanto o operador navega, para não perder o que já digitou.
/// </summary>
public partial class SelecaoImpressaoViewModel : ObservableObject
{
    public Produto Produto { get; }

    public IReadOnlyList<OpcaoTipoEtiqueta> TiposEtiqueta => OpcaoTipoEtiqueta.ListaPadrao;

    /// <summary>Tipo escolhido para esta impressão. Não altera o cadastro nem a etiqueta salva do produto.</summary>
    [ObservableProperty]
    private OpcaoTipoEtiqueta _tipoEtiquetaSelecionado = OpcaoTipoEtiqueta.Vinculada;

    public ObservableCollection<LinhaSelecaoViewModel> Linhas { get; } = new();

    [ObservableProperty]
    private int _total;

    [ObservableProperty]
    private bool _temQuantidade;

    [ObservableProperty]
    private bool _temErro;

    [ObservableProperty]
    private string _mensagemValidacao = "";

    public string Nome => Produto.Nome;

    public string ReferenciaExibicao => string.IsNullOrWhiteSpace(Produto.Referencia) ? "Sem referência" : Produto.Referencia!;

    public string CategoriaExibicao => string.IsNullOrWhiteSpace(Produto.Categoria) ? "Sem categoria" : Produto.Categoria!;

    public string PrecoVarejoFormatado => Dinheiro.Formatar(Produto.PrecoVendaCentavos);

    public string PrecoAtacadoFormatado => Produto.PrecoAtacadoCentavos > 0 ? Dinheiro.Formatar(Produto.PrecoAtacadoCentavos) : "—";

    public int EstoqueTotal => (Produto.Tamanhos ?? new List<ProdutoTamanho>()).Sum(t => t.Estoque);

    public string EstoqueTotalTexto => EstoqueTotal > 0 ? $"Estoque total: {EstoqueTotal}" : "Estoque total: 0";

    public bool ProdutoTemGrade => Linhas.Any(l => !string.IsNullOrWhiteSpace(l.Tamanho));

    public string InfoEtiquetaNome { get; }

    public string InfoEtiquetaDimensoes { get; }

    public bool TemEtiquetaPersonalizada { get; }

    public event EventHandler? QuantidadesAlteradas;

    public string ResumoSelecao => Total > 0
        ? string.Join(" · ", Linhas.Where(l => l.TemQuantidade).Select(l => $"{l.TamanhoExibicao}: {l.Quantidade}"))
        : "Nenhuma quantidade informada";

    public SelecaoImpressaoViewModel(ProdutoItemViewModel item, SelecaoImpressaoViewModel? anterior = null)
        : this(item.Produto, QuantidadesIniciais(item, anterior))
    {
    }

    public SelecaoImpressaoViewModel(Produto produto, IReadOnlyDictionary<string, int>? quantidades = null)
    {
        Produto = produto ?? throw new ArgumentNullException(nameof(produto));

        var selecao = SelecaoEtiquetas.DoProduto(produto, quantidades);
        foreach (var linha in selecao.Linhas)
        {
            var vm = new LinhaSelecaoViewModel(linha.Tamanho, linha.Estoque, linha.Quantidade);
            vm.Alterada += (_, _) => AtualizarTotais();
            Linhas.Add(vm);
        }

        _linhaUnica = Linhas.FirstOrDefault(l => l.EhSemTamanho);
        AjustarLinhaUnica();

        var info = GerenciadorEtiquetasProdutoService.ObterInfoEtiqueta(produto.Id);
        TemEtiquetaPersonalizada = info.TemPersonalizada;
        InfoEtiquetaNome = info.NomeModelo;
        InfoEtiquetaDimensoes = info.Dimensoes;

        AtualizarTotais();
    }

    private static IReadOnlyDictionary<string, int>? QuantidadesIniciais(ProdutoItemViewModel item, SelecaoImpressaoViewModel? anterior)
    {
        if (anterior is not null && anterior.Produto.Id == item.Produto.Id)
            return anterior.Linhas.ToDictionary(l => l.Tamanho, l => l.Quantidade, StringComparer.OrdinalIgnoreCase);

        // Ao abrir pelo catálogo, aproveita o tamanho já escolhido na linha e a quantidade rápida.
        var escolhido = item.TamanhoSelecionado?.Trim() ?? "";
        if (escolhido.Length > 0 &&
            !string.Equals(escolhido, ProdutoItemViewModel.TodosOsTamanhos, StringComparison.OrdinalIgnoreCase))
        {
            var chave = string.Equals(escolhido, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase) ? "" : escolhido;
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                [chave] = Math.Max(1, item.QuantidadeAdicionar)
            };
        }

        return null;
    }

    public void AtualizarTotais()
    {
        Total = Linhas.Sum(l => l.Quantidade);
        TemQuantidade = Total > 0;
        TemErro = Linhas.Any(l => l.QuantidadeInvalida);
        MensagemValidacao = "";

        OnPropertyChanged(nameof(ResumoSelecao));
        OnPropertyChanged(nameof(ProdutoTemGrade));
        QuantidadesAlteradas?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void AplicarGradeRapida(string? tipoGrade)
    {
        var novosTamanhos = (tipoGrade ?? "").Trim() switch
        {
            "Letras" => new[] { "P", "M", "G", "GG", "EXG" },
            "ExtraLetras" => new[] { "G1", "G2", "G3", "G4", "G5", "G6" },
            "SuperLetras" => new[] { "G7", "G8", "G9", "G10" },
            "Bermuda" => new[] { "42", "44", "46", "48", "50" },
            "Numerica" => new[] { "38", "40", "42", "44", "46", "48", "50" },
            "ExtraNumerica" => new[] { "52", "54", "56" },
            _ => Array.Empty<string>()
        };

        if (novosTamanhos.Length == 0) return;

        AcrescentarTamanhos(novosTamanhos);
        AjustarLinhaUnica();
        AtualizarTotais();
    }

    /// <summary>Linhas que entraram por causa da grade escolhida no Estúdio (saem se a grade mudar).</summary>
    private readonly List<LinhaSelecaoViewModel> _linhasDaGrade = new();

    /// <summary>
    /// Mostra aqui os tamanhos da grade escolhida no Estúdio. Trocar de grade tira os da anterior,
    /// menos os que já têm quantidade digitada.
    /// </summary>
    public void UsarGrade(IReadOnlyList<string> tamanhos)
    {
        foreach (var linha in _linhasDaGrade
                     .Where(l => !l.TemQuantidade && !tamanhos.Contains(l.Tamanho, StringComparer.OrdinalIgnoreCase))
                     .ToList())
        {
            Linhas.Remove(linha);
            _linhasDaGrade.Remove(linha);
        }

        _linhasDaGrade.AddRange(AcrescentarTamanhos(tamanhos));
        AjustarLinhaUnica();
        AtualizarTotais();
    }

    private readonly LinhaSelecaoViewModel? _linhaUnica;

    /// <summary>A linha "Único" só existe quando não há nenhum tamanho para escolher.</summary>
    private void AjustarLinhaUnica()
    {
        if (_linhaUnica is null) return;

        if (Linhas.Any(l => !l.EhSemTamanho))
        {
            if (Linhas.Remove(_linhaUnica)) _linhaUnica.DefinirQuantidade(0);
        }
        else if (!Linhas.Contains(_linhaUnica))
        {
            Linhas.Add(_linhaUnica);
        }
    }

    /// <summary>
    /// Acrescenta linhas só nesta tela. O produto não é alterado: um tamanho fora do cadastro
    /// sai com o código de barras do próprio produto, que o PDV sabe ler.
    /// </summary>
    private List<LinhaSelecaoViewModel> AcrescentarTamanhos(IEnumerable<string> tamanhos)
    {
        var novas = new List<LinhaSelecaoViewModel>();
        foreach (var tam in tamanhos)
        {
            if (Linhas.Any(l => string.Equals(l.Tamanho, tam, StringComparison.OrdinalIgnoreCase)))
                continue;

            var linha = new LinhaSelecaoViewModel(tam, 0);
            linha.Alterada += (_, _) => AtualizarTotais();

            Linhas.Add(linha);
            novas.Add(linha);
        }

        return novas;
    }

    /// <summary>Valida para o botão de impressão e deixa a mensagem pronta na tela.</summary>
    public bool ValidarParaImpressao()
    {
        var erros = ValidarParaImprimir();
        MensagemValidacao = erros.Count == 0 ? "" : string.Join(" ", erros);
        return erros.Count == 0;
    }

    public void DefinirQuantidade(string tamanho, int quantidade)
    {
        var linha = Linhas.FirstOrDefault(l =>
            string.Equals(l.Tamanho, tamanho?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));
        linha?.DefinirQuantidade(quantidade);
        AtualizarTotais();
    }

    public IReadOnlyList<string> ValidarParaImprimir()
    {
        var erros = new List<string>();

        if (TemErro)
            erros.Add("Corrija as quantidades destacadas em vermelho antes de continuar.");
        if (Total <= 0)
            erros.Add("Informe a quantidade de pelo menos um tamanho.");

        return erros;
    }

    public SelecaoEtiquetas ParaSelecao()
    {
        var quantidades = Linhas.ToDictionary(
            l => l.Tamanho,
            l => Math.Max(0, l.Quantidade),
            StringComparer.OrdinalIgnoreCase);

        return SelecaoEtiquetas.DoProduto(Produto, quantidades);
    }
}
