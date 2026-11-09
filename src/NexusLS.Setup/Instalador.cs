using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using NexusLS.Core.Atualizacao;
using NexusLS.Core.Licenciamento;
using NexusLS.Core.Services;

namespace NexusLS.Setup;

/// <summary>Opções do instalador: parâmetros de linha de comando ou escolhas da janela.</summary>
public sealed class OpcoesSetup
{
    public const string EmpresaPadrao = "lifesurf-principal";

    public static string PastaPadrao => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Nexus");

    public bool Silencioso { get; set; }
    public string? Empresa { get; set; }
    public string Pasta { get; set; }
    public bool Desinstalar { get; set; }
    public string ChaveLicenca { get; set; } = "";
    public bool InstalarNexusLS { get; set; } = true;
    public bool InstalarBarTender { get; set; } = true;
    public bool InstalarAtualizador { get; set; } = true;
    public bool CriarAtalhosDesktop { get; set; } = true;

    public OpcoesSetup(bool silencioso = false, string? empresa = null, string? pasta = null, bool desinstalar = false)
    {
        Silencioso = silencioso;
        Empresa = SomenteIdValido(empresa);
        Pasta = string.IsNullOrWhiteSpace(pasta) ? PastaPadrao : pasta;
        Desinstalar = desinstalar;
    }

    public static OpcoesSetup Ler(string[] args)
    {
        var op = new OpcoesSetup();
        foreach (var a in args)
        {
            var t = a.Trim();
            if (t.Equals("/S", StringComparison.OrdinalIgnoreCase) || t.Equals("--silencioso", StringComparison.OrdinalIgnoreCase)) op.Silencioso = true;
            else if (t.Equals("--desinstalar", StringComparison.OrdinalIgnoreCase) || t.Equals("/desinstalar", StringComparison.OrdinalIgnoreCase)) op.Desinstalar = true;
            else if (t.StartsWith("--empresa=", StringComparison.OrdinalIgnoreCase)) op.Empresa = SomenteIdValido(t["--empresa=".Length..].Trim('"'));
            else if (t.StartsWith("--pasta=", StringComparison.OrdinalIgnoreCase)) op.Pasta = t["--pasta=".Length..].Trim('"');
            else if (t.StartsWith("--chave=", StringComparison.OrdinalIgnoreCase)) op.ChaveLicenca = t["--chave=".Length..].Trim('"').ToUpperInvariant();
            else if (t.Equals("--sem-bartender", StringComparison.OrdinalIgnoreCase)) op.InstalarBarTender = false;
            else if (t.Equals("--sem-nexus", StringComparison.OrdinalIgnoreCase)) op.InstalarNexusLS = false;
            else if (t.Equals("--sem-atualizador", StringComparison.OrdinalIgnoreCase)) op.InstalarAtualizador = false;
        }
        return op;
    }

    public static string? SomenteIdValido(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 80 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? id : null;
}

/// <summary>Gerencia a validação da licença e a instalação dos módulos selecionados.</summary>
public sealed class Instalador
{
    public const string Repositorio = "Lukkzz11/Nexus-LS---Update";

    private readonly OpcoesSetup _op;
    private readonly HttpClient _http;
    private readonly ServicoLicencaNuvem _servicoLicenca;

    public Instalador(OpcoesSetup op, HttpClient? http = null)
    {
        _op = op;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Nexus-Setup/1.0");

        var fonteLicenca = new FonteLicencaComReserva(
            new FonteLicencaCloudFunction(_http),
            new FonteLicencaFirestore(_http));
        _servicoLicenca = new ServicoLicencaNuvem(fonteLicenca, ServicoLicencaNuvem.CaminhoPadrao());
    }

    public ServicoLicencaNuvem ServicoLicenca => _servicoLicenca;

    public Task<ResultadoLicencaNuvem> ValidarEAtivarLicencaAsync(string chave, CancellationToken ct = default)
    {
        return _servicoLicenca.AtivarAsync(chave, ct);
    }

    public List<string> ObterProgramasSelecionados()
    {
        var lista = new List<string>();
        if (_op.InstalarNexusLS) lista.Add("NexusLS.exe");
        if (_op.InstalarBarTender) lista.Add("NexusBarTender.exe");
        if (_op.InstalarAtualizador) lista.Add("atualizador.exe");
        return lista;
    }

    public bool HaProgramaAberto()
    {
        var selecionados = ObterProgramasSelecionados();
        return selecionados.Any(p => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(p)).Length > 0);
    }

    /// <summary>
    /// True se o texto do arquivo .sha256 ("hash  nome") bate com o hash calculado do arquivo.
    /// Mantido público porque é a regra de conferência usada também pelos testes do instalador.
    /// </summary>
    public static bool HashConfere(string textoSha, string hashDoArquivo)
    {
        var esperado = (textoSha ?? "").Trim().Split(' ', '\t', '\n', '\r')[0];
        return esperado.Length == 64 && string.Equals(esperado, hashDoArquivo, StringComparison.OrdinalIgnoreCase);
    }

    public async Task InstalarAsync(IProgress<(double Percentual, string Texto)> progresso, CancellationToken ct = default)
    {
        var programas = ObterProgramasSelecionados();
        if (programas.Count == 0)
            throw new InvalidOperationException("Selecione pelo menos um programa para instalar (Nexus LS, BarTender ou Atualizador).");

        if (HaProgramaAberto())
            throw new InvalidOperationException("Feche os programas do Nexus que estiverem abertos antes de prosseguir com a instalação.");

        // 1. Validação e Ativação da Licença Comercial na nuvem (se informada)
        if (!string.IsNullOrWhiteSpace(_op.ChaveLicenca))
        {
            progresso.Report((5, "Validando licença comercial no servidor central Nexus..."));
            var resLic = await _servicoLicenca.AtivarAsync(_op.ChaveLicenca.Trim(), ct).ConfigureAwait(false);
            if (!resLic.Ativa)
            {
                throw new InvalidOperationException($"Não foi possível ativar a licença: {resLic.Mensagem}");
            }
        }

        Directory.CreateDirectory(_op.Pasta);
        var temp = Path.Combine(Path.GetTempPath(), "NexusSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);

        try
        {
            var baixados = new List<(string Nome, string Caminho)>();

            for (var i = 0; i < programas.Count; i++)
            {
                var nome = programas[i];
                var destinoTemp = Path.Combine(temp, nome);
                double baseP = 10.0 + (i * 75.0 / programas.Count);
                double fatia = 75.0 / programas.Count;

                // 2. Tenta primeiro usar o arquivo local se o instalador foi executado na mesma pasta dos executáveis
                var localCandidatos = new[]
                {
                    Path.Combine(AppContext.BaseDirectory, nome),
                    Path.Combine(AppContext.BaseDirectory, "para-outro-pc", nome),
                    Path.Combine(@"C:\NexusLS\publicar", nome)
                };
                var localExe = localCandidatos.FirstOrDefault(File.Exists);

                if (localExe != null && new FileInfo(localExe).Length > 1024 * 1024)
                {
                    progresso.Report((baseP, $"Copiando pacote local {nome} ({i + 1} de {programas.Count})…"));
                    File.Copy(localExe, destinoTemp, overwrite: true);
                }
                else
                {
                    // 3. Baixa da nuvem oficial via CDN GitHub
                    progresso.Report((baseP, $"Consultando {nome} na nuvem oficial ({i + 1} de {programas.Count})…"));
                    var fonte = new FonteGitHub(_http, Repositorio, nome, nome + ".sha256");
                    var infoVersao = await fonte.UltimaAsync(ct).ConfigureAwait(false);
                    if (infoVersao == null)
                        throw new InvalidOperationException($"Versão oficial de {nome} não encontrada no repositório de lançamentos.");

                    progresso.Report((baseP + fatia * 0.1, $"Baixando {nome} versão {infoVersao.Versao}…"));
                    var progressoBytes = new Progress<long>(bytes =>
                    {
                        if (infoVersao.Tamanho > 0)
                        {
                            var perc = Math.Min(1.0, (double)bytes / infoVersao.Tamanho);
                            progresso.Report((baseP + fatia * (0.1 + perc * 0.8), $"Baixando {nome}… {bytes / 1048576} de {infoVersao.Tamanho / 1048576} MB"));
                        }
                    });

                    await using (var fs = File.Create(destinoTemp))
                    {
                        await fonte.BaixarAsync(infoVersao, fs, progressoBytes, ct).ConfigureAwait(false);
                    }

                    // Conferência SHA-256
                    if (!string.IsNullOrWhiteSpace(infoVersao.Sha256))
                    {
                        progresso.Report((baseP + fatia * 0.95, $"Conferindo integridade SHA-256 de {nome}…"));
                        await using var f = File.OpenRead(destinoTemp);
                        var hash = Convert.ToHexString(await SHA256.HashDataAsync(f, ct).ConfigureAwait(false)).ToLowerInvariant();
                        if (!string.Equals(hash, infoVersao.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException($"O arquivo {nome} baixado não confere com a assinatura digital oficial (SHA-256).");
                    }
                }

                baixados.Add((nome, destinoTemp));
            }

            // 4. Grava os executáveis na pasta final de programas
            progresso.Report((88, "Instalando programas no computador…"));
            foreach (var (nome, caminho) in baixados)
            {
                var destinoFinal = Path.Combine(_op.Pasta, nome);
                AtualizadorService.TrocarExecutavel(destinoFinal, caminho);
            }

            // 5. Configuração da Conexão com o ERP
            if (_op.InstalarNexusLS)
            {
                EscreverConexaoErp();
            }

            // 6. Atalhos na Área de Trabalho e Menu Iniciar
            if (_op.CriarAtalhosDesktop)
            {
                progresso.Report((94, "Criando atalhos na Área de Trabalho e Menu Iniciar…"));
                CriarAtalhos(programas);
            }

            // 7. Registro de Desinstalação no Painel de Controle do Windows
            RegistrarDesinstalacao();

            progresso.Report((100, "Instalação e ativação concluídas com sucesso!"));
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }

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

    private void CriarAtalhos(IEnumerable<string> programasInstalados)
    {
        var shellTipo = Type.GetTypeFromProgID("WScript.Shell");
        if (shellTipo is null) return;
        dynamic shell = Activator.CreateInstance(shellTipo)!;
        var menu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Nexus");
        Directory.CreateDirectory(menu);

        var mapaNomes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NexusLS.exe"] = "Nexus LS",
            ["NexusBarTender.exe"] = "Nexus BarTender",
            ["atualizador.exe"] = "Nexus Atualizador"
        };

        foreach (var pasta in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), menu })
        {
            foreach (var exe in programasInstalados)
            {
                if (!mapaNomes.TryGetValue(exe, out var nomeAmigavel)) nomeAmigavel = Path.GetFileNameWithoutExtension(exe);
                var atalho = Path.Combine(pasta, nomeAmigavel + ".lnk");
                dynamic s = shell.CreateShortcut(atalho);
                s.TargetPath = Path.Combine(_op.Pasta, exe);
                s.WorkingDirectory = _op.Pasta;
                s.IconLocation = Path.Combine(_op.Pasta, exe) + ",0";
                s.Save();
            }
        }
    }

    private void RegistrarDesinstalacao()
    {
        var script = Path.Combine(_op.Pasta, "Desinstalar-Nexus.ps1");
        File.WriteAllText(script, DesinstaladorPs1(_op.Pasta), new UTF8Encoding(true));
        using var chave = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Nexus");
        chave.SetValue("DisplayName", "Nexus");
        chave.SetValue("Publisher", "Nexus LS");
        chave.SetValue("InstallLocation", _op.Pasta);
        chave.SetValue("DisplayIcon", Path.Combine(_op.Pasta, "NexusLS.exe"));
        chave.SetValue("UninstallString", $"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{script}\"");
        chave.SetValue("NoModify", 1, RegistryValueKind.DWord);
        chave.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static string DesinstaladorPs1(string pasta) =>
        "$ErrorActionPreference = 'SilentlyContinue'\n" +
        "$pasta = '" + pasta.Replace("'", "''") + "'\n" +
        "Get-Process NexusLS, atualizador, NexusBarTender | Stop-Process -Force\n" +
        "$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Nexus'\n" +
        "$desktop = [Environment]::GetFolderPath('DesktopDirectory')\n" +
        "foreach ($n in 'Nexus LS','Nexus BarTender','Nexus Atualizador','Nexus','Nexus Etiquetas') { Remove-Item (Join-Path $desktop \"$n.lnk\") -Force }\n" +
        "Remove-Item $menu -Recurse -Force\n" +
        "Remove-Item 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Nexus' -Recurse -Force\n" +
        "Start-Sleep 1\n" +
        "Remove-Item $pasta -Recurse -Force\n" +
        "Write-Host 'Nexus removido com sucesso. Os dados locais em %LOCALAPPDATA%\\NexusLS foram preservados.'\n" +
        "Start-Sleep 2\n";
}
