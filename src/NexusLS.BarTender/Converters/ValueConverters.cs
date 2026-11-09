using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NexusLS.BarTender.Models.Designer;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Converters;

public class IgualAoTagConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is not null && values[1] is not null
        && string.Equals(values[0].ToString(), values[1].ToString(), StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

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

public class CentavosParaMoedaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long centavos)
        {
            return Dinheiro.Formatar(centavos);
        }
        return "R$ 0,00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class StatusCodigoCorConverter : IValueConverter
{
    private static readonly SolidColorBrush Verde = new(Color.FromRgb(0x15, 0x80, 0x3D)); // #15803D
    private static readonly SolidColorBrush Amarelo = new(Color.FromRgb(0xB4, 0x53, 0x09)); // #B45309

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool temCodigo && temCodigo)
            return Verde;

        return Amarelo;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class StatusCodigoFundoConverter : IValueConverter
{
    private static readonly SolidColorBrush VerdeFundo = new(Color.FromRgb(0xDC, 0xFC, 0xE7)); // #DCFCE7
    private static readonly SolidColorBrush AmareloFundo = new(Color.FromRgb(0xFE, 0xF3, 0xC7)); // #FEF3C7

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool temCodigo && temCodigo)
            return VerdeFundo;

        return AmareloFundo;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class ComparisonConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString()?.Equals(parameter?.ToString(), StringComparison.OrdinalIgnoreCase) ?? false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? parameter?.ToString() ?? "" : Binding.DoNothing;
    }
}

public class BoolInversoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b ? !b : value;
}

public class NullParaColapsadoConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.Equals("invert", StringComparison.OrdinalIgnoreCase) ?? false;
        var isNull = (value == null);

        if (invert)
        {
            return isNull ? Visibility.Visible : Visibility.Collapsed;
        }

        return isNull ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class BoolParaVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var invert = parameter?.ToString()?.Equals("invert", StringComparison.OrdinalIgnoreCase) ?? false;
        var b = value is true;

        if (invert)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }

        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public class TipoElementoVisibilidadeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TipoElemento tipo || parameter == null)
            return Visibility.Collapsed;

        var paramStr = parameter.ToString();

        if (paramStr == "Forma")
        {
            return (tipo == TipoElemento.Retangulo || tipo == TipoElemento.Linha)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        return tipo.ToString().Equals(paramStr, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
