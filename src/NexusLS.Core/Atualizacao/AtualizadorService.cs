using System.Security.Cryptography;

namespace NexusLS.Core.Atualizacao;

public enum EstadoVerificacao { SemNovidade, Disponivel, SemConexao }

public record ResultadoVerificacao(EstadoVerificacao Estado, VersaoPrograma Atual, InfoVersao? Nova, string Mensagem);

/// <summary>
/// Atualizador do programa: consulta se há versão nova, baixa conferindo o arquivo (tamanho, SHA-256 e se é mesmo um
/// executável) e troca o .exe. Sem internet ou com qualquer falha na consulta, o programa segue normal e só avisa:
/// o atualizador NUNCA atrapalha o uso.
/// </summary>
public class AtualizadorService
{
    public const string SufixoDoAntigo = ".antigo";

    private readonly IFonteAtualizacao _fonte;
    private readonly string _pasta;
    private readonly VersaoPrograma _atual;

    /// <param name="pasta">Onde ficam os arquivos baixados (padrão: %LOCALAPPDATA%\NexusLS\atualizacao).</param>
    public AtualizadorService(IFonteAtualizacao fonte, string pasta, VersaoPrograma atual)
    {
        _fonte = fonte;
        _pasta = pasta;
        _atual = atual;
    }

    public VersaoPrograma Atual => _atual;

    public static string PastaPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "atualizacao");

    public Task<ResultadoVerificacao> VerificarAsync(CancellationToken ct = default)
        => VerificarAsync(caminhoArquivoInstalado: null, ct);

    /// <summary>
    /// Consulta se há versão mais nova publicada.
    /// Se <paramref name="caminhoArquivoInstalado"/> for informado e existir, compara também a integridade SHA-256
    /// do binário instalado contra o release do Git. Se os hashes baterem, reconhece que o programa já está com
    /// os mesmos binários do Git, evitando falsos positivos de "nova versão disponível".
    /// </summary>
    public async Task<ResultadoVerificacao> VerificarAsync(string? caminhoArquivoInstalado, CancellationToken ct = default)
    {
        try
        {
            var ultima = await _fonte.UltimaAsync(ct).ConfigureAwait(false);
            if (ultima is null)
                return new ResultadoVerificacao(EstadoVerificacao.SemNovidade, _atual, null, "Você já está com a versão mais nova.");

            // 1. Verificação por integridade SHA-256 do binário:
            // Se o arquivo local informado existe e seu SHA-256 for idêntico ao do release do GitHub,
            // o programa instalado já é exatamente o binário mais recente do Git (evita falso "nova versão disponível").
            var arquivoParaConferir = caminhoArquivoInstalado;
            if (string.IsNullOrWhiteSpace(arquivoParaConferir) || !File.Exists(arquivoParaConferir))
            {
                var processoAtual = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(processoAtual) && File.Exists(processoAtual))
                {
                    arquivoParaConferir = processoAtual;
                }
            }

            if (!string.IsNullOrWhiteSpace(arquivoParaConferir) && File.Exists(arquivoParaConferir) && !string.IsNullOrWhiteSpace(ultima.Sha256))
            {
                try
                {
                    var hashLocal = await Sha256DoArquivoAsync(arquivoParaConferir, ct).ConfigureAwait(false);
                    if (string.Equals(hashLocal, ultima.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return new ResultadoVerificacao(
                            EstadoVerificacao.SemNovidade,
                            ultima.Versao,
                            null,
                            $"Você já está com a versão mais recente instalada (v{ultima.Versao} sincronizada com o GitHub).");
                    }
                }
                catch
                {
                    // Falha ao ler ou calcular hash não bloqueia o fluxo principal
                }
            }

            // 2. Verificação tradicional por número de versão
            if (ultima.Versao <= _atual)
                return new ResultadoVerificacao(EstadoVerificacao.SemNovidade, _atual, null, "Você já está com a versão mais nova.");

            return new ResultadoVerificacao(EstadoVerificacao.Disponivel, _atual, ultima, $"Versão {ultima.Versao} disponível.");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            return new ResultadoVerificacao(EstadoVerificacao.SemConexao, _atual, null,
                "Não foi possível consultar as atualizações agora (sem internet ou o serviço não respondeu). Tente de novo mais tarde.");
        }
    }

    /// <summary>
    /// Baixa a versão e só devolve o caminho se o arquivo passou em todas as conferências. Se algo falha o arquivo
    /// parcial é apagado e nada muda no programa.
    /// </summary>
    public Task<string> BaixarAsync(InfoVersao versao, IProgress<double>? progresso, CancellationToken ct)
        => BaixarAsync(versao, progresso, "NexusLS", ct);

    /// <summary>
    /// Baixa a versão e só devolve o caminho se o arquivo passou em todas as conferências. Permite definir o prefixo do nome do arquivo.
    /// </summary>
    public async Task<string> BaixarAsync(InfoVersao versao, IProgress<double>? progresso = null, string prefixoArquivo = "NexusLS", CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(versao.Sha256))
            throw new RegraDeAtualizacaoException("Esta versão não traz a assinatura de conferência (SHA-256) e por segurança não será instalada.");

        Directory.CreateDirectory(_pasta);
        var final = Path.Combine(_pasta, $"{prefixoArquivo}-{versao.Versao}.exe");
        var parcial = final + ".parte";
        ApagarSilencioso(parcial);
        ApagarSilencioso(final);

        try
        {
            await using (var arquivo = new FileStream(parcial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var relatorio = progresso is null || versao.Tamanho <= 0
                    ? null
                    : new Progress<long>(bytes => progresso.Report(Math.Min(1.0, (double)bytes / versao.Tamanho)));
                await _fonte.BaixarAsync(versao, arquivo, relatorio, ct).ConfigureAwait(false);
            }

            var info = new FileInfo(parcial);
            if (versao.Tamanho > 0 && info.Length != versao.Tamanho)
                throw new RegraDeAtualizacaoException($"O arquivo veio incompleto ({info.Length} de {versao.Tamanho} bytes). Tente de novo.");
            if (!ParecePrograma(parcial))
                throw new RegraDeAtualizacaoException("O arquivo baixado não é um programa válido e foi descartado.");

            var hash = await Sha256DoArquivoAsync(parcial, ct).ConfigureAwait(false);
            if (!string.Equals(hash, versao.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                throw new RegraDeAtualizacaoException("O arquivo baixado não confere com a assinatura (SHA-256) da versão e foi descartado. Nada foi instalado.");

            File.Move(parcial, final, overwrite: true);
            progresso?.Report(1.0);
            return final;
        }
        catch
        {
            ApagarSilencioso(parcial);
            ApagarSilencioso(final);
            throw;
        }
    }

    /// <summary>
    /// Troca o executável: o Windows não deixa apagar nem sobrescrever um programa em uso, mas deixa RENOMEAR. Então o
    /// atual vira <c>.antigo</c>, o novo ocupa o nome certo e, se qualquer passo falhar, tudo volta como estava.
    /// Quem chama inicia o novo e fecha este.
    /// </summary>
    public static void TrocarExecutavel(string executavelAtual, string novo)
    {
        if (!File.Exists(novo)) throw new RegraDeAtualizacaoException("O arquivo da atualização não foi encontrado.");

        var pasta = Path.GetDirectoryName(executavelAtual);
        if (!string.IsNullOrWhiteSpace(pasta) && !Directory.Exists(pasta))
        {
            Directory.CreateDirectory(pasta);
        }

        // Se o executável ainda não existe no computador de destino (ex: instalação inicial), apenas copia diretamente
        if (!File.Exists(executavelAtual))
        {
            File.Copy(novo, executavelAtual, overwrite: true);
            ApagarSilencioso(novo);
            return;
        }

        var antigo = executavelAtual + SufixoDoAntigo;
        ApagarSilencioso(antigo);

        File.Move(executavelAtual, antigo);
        try
        {
            File.Copy(novo, executavelAtual, overwrite: false);
        }
        catch
        {
            // Não conseguiu colocar o novo: devolve o programa de sempre ao lugar.
            ApagarSilencioso(executavelAtual);
            File.Move(antigo, executavelAtual);
            throw;
        }
        ApagarSilencioso(novo);
    }

    /// <summary>Na abertura seguinte à atualização, apaga o programa antigo que ficou ao lado.</summary>
    public static void LimparRestos(string executavelAtual, string? pastaDownload = null)
    {
        ApagarSilencioso(executavelAtual + SufixoDoAntigo);
        if (pastaDownload is not null && Directory.Exists(pastaDownload))
            foreach (var f in Directory.EnumerateFiles(pastaDownload, "NexusLS-*.exe*"))
                ApagarSilencioso(f);
    }

    /// <summary>Todo executável do Windows começa com "MZ" e um programa deste tamanho passa de 1 MB.</summary>
    private static bool ParecePrograma(string caminho)
    {
        using var f = File.OpenRead(caminho);
        if (f.Length < 1024) return false;
        return f.ReadByte() == 'M' && f.ReadByte() == 'Z';
    }

    public static async Task<string> Sha256DoArquivoAsync(string caminho, CancellationToken ct = default)
    {
        await using var f = File.OpenRead(caminho);
        return Convert.ToHexString(await SHA256.HashDataAsync(f, ct).ConfigureAwait(false)).ToLowerInvariant();
    }

    private static void ApagarSilencioso(string caminho)
    {
        try { if (File.Exists(caminho)) File.Delete(caminho); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Falha esperada da atualização (arquivo adulterado, incompleto...): a mensagem já é para o usuário.</summary>
public class RegraDeAtualizacaoException : Exception
{
    public RegraDeAtualizacaoException(string mensagem) : base(mensagem) { }
}
