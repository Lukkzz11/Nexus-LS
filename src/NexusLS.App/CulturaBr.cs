using System.Globalization;

namespace NexusLS.App;

/// <summary>
/// O Nexus é um programa brasileiro: valores, datas, porcentagens e leituras de texto usam pt-BR
/// mesmo quando o Windows está configurado com outro "Formato regional".
///
/// Motivo: até aqui a formatação de vários textos dependia da cultura do sistema. Num computador em
/// inglês o programa mostrava "2.5 metros" e "66.67%" no meio de uma tela em português; no CI, que
/// roda em inglês, os testes das telas ficavam vermelhos por isso. Fixar a cultura uma vez na
/// abertura resolve o problema para todo o programa (inclusive threads em segundo plano e WPF).
/// </summary>
internal static class CulturaBr
{
    /// <summary>Cultura fixa do programa. É só para exibir e ler texto; o banco guarda centavos e datas.</summary>
    public static CultureInfo Cultura { get; } = CultureInfo.GetCultureInfo("pt-BR");

    private static bool _aplicada;
    private static readonly object _trava = new();

    /// <summary>
    /// Aplica pt-BR na thread atual e nas próximas. Pode ser chamado mais de uma vez sem efeito
    /// colateral: o app chama na abertura e o host de testes chama antes de rodar as telas.
    /// </summary>
    public static void Aplicar()
    {
        lock (_trava)
        {
            if (_aplicada) return;
            _aplicada = true;
        }

        CultureInfo.DefaultThreadCurrentCulture = Cultura;
        CultureInfo.DefaultThreadCurrentUICulture = Cultura;
        CultureInfo.CurrentCulture = Cultura;
        CultureInfo.CurrentUICulture = Cultura;
    }
}
