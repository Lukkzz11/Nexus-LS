using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
/// Seleção de um produto para impressão: tamanhos, quantidades e os dados que o modal e a
/// preparação mostram. Fica viva enquanto o operador navega, para não perder o que já digitou.
/// </summary>
public partial class SelecaoImpressaoViewModel : ObservableObject
{
    public Produto Produto { get; }

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

        var quantidadesAtuais = Linhas.ToDictionary(l => l.Tamanho, l => l.Quantidade, StringComparer.OrdinalIgnoreCase);
        var estoquesAtuais = Linhas.ToDictionary(l => l.Tamanho, l => l.Estoque, StringComparer.OrdinalIgnoreCase);

        Produto.Tamanhos ??= new List<ProdutoTamanho>();
        var maiorOrdem = Produto.Tamanhos.Select(t => t.Ordem).DefaultIfEmpty(0).Max();

        foreach (var tam in novosTamanhos)
        {
            if (!Produto.Tamanhos.Any(t => string.Equals(t.Tamanho?.Trim(), tam, StringComparison.OrdinalIgnoreCase)))
            {
                maiorOrdem++;
                Produto.Tamanhos.Add(new ProdutoTamanho
                {
                    ProdutoId = Produto.Id,
                    Tamanho = tam,
                    Ordem = maiorOrdem,
                    Estoque = estoquesAtuais.GetValueOrDefault(tam, 0)
                });
            }
        }

        // Reconstrói as linhas mantendo o que já estava e acrescentando os novos na ordem natural
        var semTamanhoLinha = Linhas.FirstOrDefault(l => l.EhSemTamanho);
        foreach (var tam in novosTamanhos)
        {
            if (Linhas.Any(l => string.Equals(l.Tamanho, tam, StringComparison.OrdinalIgnoreCase)))
                continue;

            var estoque = Produto.Tamanhos
                .FirstOrDefault(t => string.Equals(t.Tamanho?.Trim(), tam, StringComparison.OrdinalIgnoreCase))?.Estoque ?? 0;
            var vm = new LinhaSelecaoViewModel(tam, estoque, quantidadesAtuais.GetValueOrDefault(tam, 0));
            vm.Alterada += (_, _) => AtualizarTotais();

            if (semTamanhoLinha is not null)
            {
                var idx = Linhas.IndexOf(semTamanhoLinha);
                Linhas.Insert(Math.Max(0, idx), vm);
            }
            else
            {
                Linhas.Add(vm);
            }
        }

        AtualizarTotais();
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
