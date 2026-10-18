using System.Globalization;
using System.Windows.Data;
using NexusLS.Core.Models;
using NexusLS.Core.Services;

namespace NexusLS.App.Converters;

/// <summary>
/// Conversor de binding: fica entre o dado (long em centavos) e o que aparece na tela ("R$ 12,50").
/// A tela só mostra; o dado original continua intacto.
/// </summary>
public class CentavosConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long centavos ? Dinheiro.Formatar(centavos) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Mostra o tipo de movimentação em português ("CancelamentoVenda" -> "Cancelamento de venda").</summary>
public class TipoMovimentacaoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        TipoMovimentacao.Entrada => "Entrada",
        TipoMovimentacao.Perda => "Perda",
        TipoMovimentacao.Correcao => "Correção",
        TipoMovimentacao.Venda => "Venda",
        TipoMovimentacao.CancelamentoVenda => "Cancelamento de venda",
        _ => string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Visível quando o valor é FALSE (o contrário do BooleanToVisibilityConverter).</summary>
public class BoolInversoVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class FormaPagamentoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        FormaPagamento.Dinheiro => "Dinheiro",
        FormaPagamento.Pix => "Pix",
        FormaPagamento.CartaoDebito => "Cartão débito",
        FormaPagamento.CartaoCredito => "Cartão crédito",
        FormaPagamento.Cheque => "Cheque",
        FormaPagamento.APrazo => "A prazo",
        _ => string.Empty
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>true/false -> texto. O parâmetro tem os dois textos separados por "|", ex.: "Ativo|Inativo".</summary>
public class BoolParaTextoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var partes = (parameter as string ?? "Sim|Não").Split('|');
        return value is true ? partes[0] : partes[1];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
