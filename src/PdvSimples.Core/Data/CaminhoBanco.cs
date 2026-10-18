namespace PdvSimples.Core.Data;

public static class CaminhoBanco
{
    /// <summary>%LOCALAPPDATA%\PdvSimples\pdv.db (a pasta é criada se não existir).</summary>
    public static string Padrao()
    {
        var pasta = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PdvSimples");
        Directory.CreateDirectory(pasta);
        return Path.Combine(pasta, "pdv.db");
    }
}
