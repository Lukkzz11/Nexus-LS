using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Models;

public partial class ItemFila : ObservableObject
{
    public Produto Produto { get; }

    [ObservableProperty]
    private int _quantidade = 1;

    [ObservableProperty]
    private string _tamanho = "";

    public string Nome => Produto.Nome;
    public string? Referencia => Produto.Referencia;
    public string? CodigoInterno => Produto.CodigoInterno;

    /// <summary>Código que vai impresso: EAN-13 do produto ou referência/código interno + número do tamanho.</summary>
    public string? CodigoBarras => CodigoBarrasTamanho.Resolver(Produto, Tamanho);

    public string TamanhoExibicao => string.IsNullOrWhiteSpace(Tamanho) ? Tamanhos.SemTamanho : Tamanho;

    public long PrecoVarejoCentavos => Produto.PrecoVendaCentavos;
    public long PrecoAtacadoCentavos => Produto.PrecoAtacadoCentavos;

    public string PrecoVarejoFormatado => Dinheiro.Formatar(PrecoVarejoCentavos);
    public string PrecoAtacadoFormatado => PrecoAtacadoCentavos > 0 ? Dinheiro.Formatar(PrecoAtacadoCentavos) : "-";

    public ItemFila(Produto produto, int quantidade = 1, string tamanho = "")
    {
        Produto = produto;
        _quantidade = Math.Max(1, quantidade);
        _tamanho = NormalizarTamanho(tamanho);
    }

    partial void OnTamanhoChanged(string value)
    {
        var normalizado = NormalizarTamanho(value);
        if (!string.Equals(normalizado, value, StringComparison.Ordinal))
        {
            _tamanho = normalizado;
            OnPropertyChanged(nameof(Tamanho));
        }

        OnPropertyChanged(nameof(CodigoBarras));
        OnPropertyChanged(nameof(TamanhoExibicao));
    }

    private static string NormalizarTamanho(string? tamanho)
    {
        var limpo = tamanho?.Trim();
        return string.IsNullOrEmpty(limpo) || string.Equals(limpo, Tamanhos.SemTamanho, StringComparison.OrdinalIgnoreCase)
            ? ""
            : limpo;
    }
}
