using System.Globalization;
using System.Windows;
using System.Windows.Markup;

namespace NexusLS.BarTender;

/// <summary>
/// Fixa português do Brasil no processo inteiro. Sem isso, num Windows configurado em inglês,
/// medidas e preços apareceriam com ponto decimal ("50.3 mm", "119.90") nas telas do Estúdio.
/// Vale só para exibição e leitura de texto; o banco continua guardando centavos e datas.
/// </summary>
internal static class CulturaBr
{
    public static void Aplicar()
    {
        var cultura = CultureInfo.GetCultureInfo("pt-BR");

        CultureInfo.DefaultThreadCurrentCulture = cultura;
        CultureInfo.DefaultThreadCurrentUICulture = cultura;
        CultureInfo.CurrentCulture = cultura;
        CultureInfo.CurrentUICulture = cultura;

        // Faz os controles WPF (texto, datas, campos numéricos) usarem a mesma cultura.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(cultura.IetfLanguageTag)));
    }
}
