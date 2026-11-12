using System.Globalization;

namespace NexusLS.Core.Services;

/// <summary>
/// Números que aparecem em texto para o operador (mensagens, avisos, relatórios) sempre no formato
/// brasileiro, independente do "Formato regional" do Windows.
///
/// Motivo: as telas já usam pt-BR, mas o Core formatava alguns avisos com a cultura do sistema. Num
/// computador configurado em inglês, o mesmo aviso saía "as barras ficariam com menos de 0.25 mm" no
/// meio de um texto em português (e o CI, que roda em inglês, ficava vermelho por causa disso).
/// </summary>
public static class TextoBr
{
    /// <summary>Cultura usada só para formatar texto; nunca para gravar ou ler dados.</summary>
    public static CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Valor com casas decimais e vírgula: <c>1234.5m</c> com "N2" vira "1.234,50".</summary>
    public static string Numero(decimal valor, string formato = "N2") => valor.ToString(formato, Cultura);

    /// <summary>Mesma ideia para medidas em ponto flutuante: <c>0.25</c> com "0.00" vira "0,25".</summary>
    public static string Numero(double valor, string formato = "0.0") => valor.ToString(formato, Cultura);
}
