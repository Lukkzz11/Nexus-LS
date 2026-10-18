namespace NexusLS.Core.Data;

public static class CaminhoBanco
{
    private static string RaizLocal =>
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>
    /// %LOCALAPPDATA%\NexusLS\nexus.db (a pasta é criada se não existir).
    /// Se o banco ainda for o da versão antiga (PdvSimples\pdv.db), uma CÓPIA dele é usada na primeira
    /// abertura, para os produtos e vendas já cadastrados não se perderem. O arquivo antigo fica intacto.
    /// </summary>
    public static string Padrao()
    {
        var pasta = Path.Combine(RaizLocal, "NexusLS");
        Directory.CreateDirectory(pasta);
        var novo = Path.Combine(pasta, "nexus.db");

        var antigo = Path.Combine(RaizLocal, "PdvSimples", "pdv.db");
        if (!File.Exists(novo) && File.Exists(antigo))
        {
            // O SQLite pode ter dados ainda nos arquivos -wal e -shm (se o programa foi fechado à força);
            // por isso os três vão juntos.
            foreach (var sufixo in new[] { "", "-wal", "-shm" })
                if (File.Exists(antigo + sufixo))
                    File.Copy(antigo + sufixo, novo + sufixo);
        }

        return novo;
    }
}
