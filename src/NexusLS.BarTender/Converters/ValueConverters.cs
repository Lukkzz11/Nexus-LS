using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NexusLS.Core.Services;

namespace NexusLS.BarTender.Converters;

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
    private static readonly SolidColorBrush Verde = new(Color.FromRgb(0x10, 0xB9, 0x81)); // #10B981
    private static readonly SolidColorBrush Amarelo = new(Color.FromRgb(0xF5, 0x9E, 0x0B)); // #F59E0B

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
    private static readonly SolidColorBrush VerdeFundo = new(Color.FromArgb(0x28, 0x10, 0xB9, 0x81));
    private static readonly SolidColorBrush AmareloFundo = new(Color.FromArgb(0x28, 0xF5, 0x9E, 0x0B));

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
