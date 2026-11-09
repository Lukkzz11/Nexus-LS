using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace NexusLS.Setup;

/// <summary>Opções do instalador (linha de comando): /S silencioso, --empresa=ID, --pasta=CAMINHO, --desinstalar.</summary>
public sealed record OpcoesSetup(bool Silencioso, string? Empresa, string Pasta, bool Desinstalar)
{
    public const string EmpresaPadrao = "lifesurf-principal";

    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus");

    public static OpcoesSetup Ler(string[] args)
    {
        bool silencioso = false, desinstalar = false;
        string? empresa = null, pasta = null;
        foreach (var a in args)
        {
            var t = a.Trim();
            if (t.Equals("/S", StringComparison.OrdinalIgnoreCase) || t.Equals("--silencioso", StringComparison.OrdinalIgnoreCase)) silencioso = true;
            else if (t.Equals("--desinstalar", StringComparison.OrdinalIgnoreCase) || t.Equals("/desinstalar", StringComparison.OrdinalIgnoreCase)) desinstalar = true;
            else if (t.StartsWith("--empresa=", StringComparison.OrdinalIgnoreCase)) empresa = t["--empresa=".Length..].Trim('"');
            else if (t.StartsWith("--pasta=", StringComparison.OrdinalIgnoreCase)) pasta = t["--pasta=".Length..].Trim('"');
        }
        empresa = SomenteIdValido(empresa);
        return new OpcoesSetup(silencioso, empresa, string.IsNullOrWhiteSpace(pasta) ? PastaPadrao : pasta, desinstalar);
    }

    /// <summary>O id da empresa vem de um link: só letras, números, '-' e '_' (nada de caracteres estranhos no erp.json).</summary>
    public static string? SomenteIdValido(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
}

/// <summary>Baixa a última versão publicada, confere o SHA-256 e instala (sem precisar de administrador).</summary>
public sealed class Instalador
{
    public const string Repositorio = "Lukkzz11/Nexus-LS---Update";
    public static readonly string[] Programas = ["NexusLS.exe", "atualizador.exe", "NexusBarTender.exe"];

    private readonly OpcoesSetup _op;
    private readonly HttpClient _http;

    public Instalador(OpcoesSetup op, HttpClient? http = null)
    {
        _op = op;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0) _http.DefaultRequestHeaders.UserAgent.ParseAdd("Nexus-Setup");
    }

    private static string Url(string arquivo) => $"https://github.com/{Repositorio}/releases/latest/download/{arquivo}";

    public static bool HaProgramaAberto() =>
        Programas.Any(p => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p)).Length > 0);

    /// <summary>True se o texto do .sha256 ("hash  nome") bate com o hash do arquivo.</summary>
    public static bool HashConfere(string textoSha, string hashDoArquivo)
    {
        var esperado = (textoSha ?? "").Trim().Split(' ', '\t', '\n', '\r')[0];
        return esperado.Length == 64 && string.Equals(esperado, hashDoArquivo, StringComparison.OrdinalIgnoreCase);
    }

    public async Task InstalarAsync(IProgress<(double Percentual, string Texto)> progresso, CancellationToken ct = default)
    {
        if (HaProgramaAberto()) throw new InvalidOperationException("Feche o Nexus, o Atualizador e o Nexus Etiquetas e tente de novo.");
        Directory.CreateDirectory(_op.Pasta);
        var temp = Path.Combine(Path.GetTempPath(), "NexusSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var baixados = new List<(string Nome, string Caminho)>();
            for (var i = 0; i < Programas.Length; i++)
            {
                var nome = Programas[i];
                var destinoTemp = Path.Combine(temp, nome);
                double baseP = i * 85.0 / Programas.Length, fatia = 85.0 / Programas.Length;
                progresso.Report((baseP, $"Baixando {nome} ({i + 1} de {Programas.Length})…"));
                var sha = await _http.GetStringAsync(Url(nome + ".sha256"), ct);
                using (var resp = await _http.GetAsync(Url(nome), HttpCompletionOption.ResponseHeadersRead, ct))
                {
                    resp.EnsureSuccessStatusCode();
                    var total = resp.Content.Headers.ContentLength ?? 0;
                    await using var origem = await resp.Content.ReadAsStreamAsync(ct);
                    await using var saida = File.Create(destinoTemp);
                    var buf = new byte[81920];
                    long lido = 0;
                    int n;
                    while ((n = await origem.ReadAsync(buf, ct)) > 0)
                    {
                        await saida.WriteAsync(buf.AsMemory(0, n), ct);
                        lido += n;
                        if (total > 0) progresso.Report((baseP + fatia * lido / total, $"Baixando {nome}… {lido / 1048576} de {total / 1048576} MB"));
                    }
                }
                progresso.Report((baseP + fatia, $"Conferindo {nome}…"));
                string hash;
                await using (var f = File.OpenRead(destinoTemp)) hash = Convert.ToHexString(await SHA256.HashDataAsync(f, ct));
                if (!HashConfere(sha, hash)) throw new InvalidDataException($"O arquivo {nome} baixado está corrompido (SHA-256 não confere). Nada foi instalado.");
                baixados.Add((nome, destinoTemp));
            }

            progresso.Report((88, "Instalando…"));
            foreach (var (nome, caminho) in baixados) File.Copy(caminho, Path.Combine(_op.Pasta, nome), overwrite: true);
            EscreverConexaoErp();
            CriarAtalhos();
            RegistrarDesinstalacao();
            progresso.Report((100, "Instalação concluída."));
        }
        finally { try { Directory.Delete(temp, true); } catch { } }
    }

    /// <summary>Grava o erp.json só se ainda não existir (não apaga uma conexão que o cliente já configurou).</summary>
    public void EscreverConexaoErp()
    {
        var dados = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS");
        Directory.CreateDirectory(dados);
        var arquivo = Path.Combine(dados, "erp.json");
        if (File.Exists(arquivo)) return;
        var empresa = _op.Empresa ?? OpcoesSetup.EmpresaPadrao;
        File.WriteAllText(arquivo, ConteudoErpJson(empresa), new UTF8Encoding(false));
    }

    public static string ConteudoErpJson(string empresa) =>
        "{\n    \"ProjectId\":  \"pdv---lifesurf\",\n    \"ApiKey\":  \"AIzaSyA6L0L-aPIe6SxqjRz-lWZPdv1bcRJD3zg\",\n    \"EmpresaId\":  \"" + empresa + "\"\n}\n";

    private void CriarAtalhos()
    {
        var shellTipo = Type.GetTypeFromProgID("WScript.Shell");
        if (shellTipo is null) return;
        dynamic shell = Activator.CreateInstance(shellTipo)!;
        var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus");
        Directory.CreateDirectory(menu);
        foreach (var pasta in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), menu })
            foreach (var (nome, exe) in new[] { ("Nexus", "NexusLS.exe"), ("Nexus Atualizador", "atualizador.exe"), ("Nexus Etiquetas", "NexusBarTender.exe") })
            {
                dynamic s = shell.CreateShortcut(Path.Combine(pasta, nome + ".lnk"));
                s.TargetPath = Path.Combine(_op.Pasta, exe);
                s.WorkingDirectory = _op.Pasta;
                s.IconLocation = Path.Combine(_op.Pasta, exe) + ",0";
                s.Save();
            }
    }

    private void RegistrarDesinstalacao()
    {
        var script = Path.Combine(_op.Pasta, "Desinstalar-Nexus.ps1");
        File.WriteAllText(script, DesinstaladorPs1(_op.Pasta), new UTF8Encoding(true));
        using var chave = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Nexus");
        chave.SetValue("DisplayName", "Nexus");
        chave.SetValue("Publisher", "Nexus");
        chave.SetValue("InstallLocation", _op.Pasta);
        chave.SetValue("DisplayIcon", Path.Combine(_op.Pasta, "NexusLS.exe"));
        chave.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\"");
        chave.SetValue("NoModify", 1, RegistryValueKind.DWord);
        chave.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    /// <summary>Remove programas e atalhos. Os dados (vendas, produtos) em %LOCALAPPDATA%\NexusLS NUNCA são apagados.</summary>
    public static string DesinstaladorPs1(string pasta) =>
        "$ErrorActionPreference = 'SilentlyContinue'\n" +
        "$pasta = '" + pasta.Replace("'", "''") + "'\n" +
        "Get-Process NexusLS, atualizador, NexusBarTender | Stop-Process -Force\n" +
        "$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nexus'\n" +
        "$desktop = [Environment]::GetFolderPath('DesktopDirectory')\n" +
        "foreach ($n in 'Nexus','Nexus Atualizador','Nexus Etiquetas') { Remove-Item (Join-Path $desktop \"$n.lnk\") -Force }\n" +
        "Remove-Item $menu -Recurse -Force\n" +
        "Remove-Item 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Nexus' -Recurse -Force\n" +
        "Start-Sleep 1\n" +
        "Remove-Item $pasta -Recurse -Force\n" +
        "Write-Host 'Nexus removido. Seus dados (vendas e cadastros) foram mantidos em %LOCALAPPDATA%\\NexusLS.'\n" +
        "Start-Sleep 3\n";
}
