namespace NexusLS.Core.Services;

/// <summary>Como o Nexus escolhe as cores: sempre claro, sempre escuro, ou seguindo o tema do Windows.</summary>
public enum ModoTema { Automatico, Claro, Escuro }

/// <summary>Regras do tema, sem nenhuma dependência de tela (por isso testáveis).</summary>
public static class RegrasDeTema
{
    /// <summary>Decide se a interface fica escura. No Automático, vale o que o Windows está usando.</summary>
    public static bool UsarEscuro(ModoTema modo, bool windowsEstaEscuro) => modo switch
    {
        ModoTema.Claro => false,
        ModoTema.Escuro => true,
        _ => windowsEstaEscuro
    };

    /// <summary>Lê o texto guardado nas preferências; qualquer coisa desconhecida vira Automático.</summary>
    public static ModoTema Ler(string? texto) =>
        Enum.TryParse<ModoTema>(texto, ignoreCase: true, out var modo) && Enum.IsDefined(modo) ? modo : ModoTema.Automatico;

    /// <summary>
    /// Interpreta o valor "AppsUseLightTheme" do registro do Windows (HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize):
    /// 0 = aplicativos no tema escuro, 1 = claro. Valor ausente ou estranho = claro (o padrão do Windows).
    /// </summary>
    public static bool WindowsEscuroPeloRegistro(object? appsUseLightTheme) => appsUseLightTheme is int i && i == 0;
}
