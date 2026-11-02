using Microsoft.EntityFrameworkCore;
using NexusLS.Core.Data;

namespace NexusLS.Core.Services;

/// <summary>
/// Reduz e recomprime uma imagem (JPEG). Fica atrás de uma interface para o Core não depender de nenhuma biblioteca
/// de imagem: no programa a implementação usa o WPF; nos testes, uma simples.
/// </summary>
public interface IProcessadorImagem
{
    /// <summary>Devolve a imagem em JPEG com o maior lado limitado a <paramref name="maiorLado"/> pixels. Lança <see cref="RegraDeNegocioException"/> se o arquivo não for uma imagem.</summary>
    byte[] ReduzirParaJpeg(byte[] original, int maiorLado);
}

/// <summary>Envia uma foto para a nuvem e devolve o endereço público. Falhas viram <see cref="Sincronizacao.FalhaDestinoException"/>.</summary>
public interface IEnviadorFoto
{
    Task<string> EnviarAsync(string nomeNoDestino, byte[] jpeg, CancellationToken ct = default);
}

public record ResultadoEnvioFotos(int Enviadas, int Falhas, string? PrimeiroErro);

/// <summary>
/// Fotos dos produtos. A foto é guardada no disco desta instalação (funciona sem internet); quando há conexão,
/// <see cref="EnviarPendentesAsync"/> manda ao Firebase Storage e grava o endereço no produto, que então segue
/// para o catálogo online na próxima sincronização. Trocar ou remover a foto limpa o endereço antigo.
/// </summary>
public class FotoProdutoService
{
    public const int MaiorLadoProduto = 1200;
    public const long TamanhoMaximoOriginal = 25L * 1024 * 1024;

    private readonly IDbContextFactory<PdvDbContext> _fabrica;
    private readonly IProcessadorImagem _imagem;
    private readonly string _pasta;
    private readonly IEnviadorFoto? _enviador;

    public FotoProdutoService(IDbContextFactory<PdvDbContext> fabrica, IProcessadorImagem imagem, string pasta, IEnviadorFoto? enviador = null)
    {
        _fabrica = fabrica;
        _imagem = imagem;
        _pasta = pasta;
        _enviador = enviador;
    }

    public static string PastaPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "fotos");

    /// <summary>Caminho completo do arquivo da foto, ou nulo se o produto não tem foto (ou o arquivo sumiu).</summary>
    public string? CaminhoDe(string? fotoArquivo)
    {
        if (string.IsNullOrWhiteSpace(fotoArquivo)) return null;
        var caminho = Path.Combine(_pasta, Path.GetFileName(fotoArquivo));
        return File.Exists(caminho) ? caminho : null;
    }

    /// <summary>Define (ou troca) a foto do produto a partir dos bytes do arquivo escolhido.</summary>
    public async Task<string> DefinirAsync(int produtoId, byte[] original)
    {
        if (original.Length == 0) throw new RegraDeNegocioException("O arquivo da foto está vazio.");
        if (original.Length > TamanhoMaximoOriginal) throw new RegraDeNegocioException("A foto é grande demais (máximo 25 MB).");
        var jpeg = _imagem.ReduzirParaJpeg(original, MaiorLadoProduto);

        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");

        Directory.CreateDirectory(_pasta);
        // O nome muda a cada troca: o endereço público antigo não é reaproveitado e nenhum cache mostra a foto velha.
        var nome = $"{produto.Guid:N}-{DateTime.UtcNow:yyyyMMddHHmmssfff}.jpg";
        await File.WriteAllBytesAsync(Path.Combine(_pasta, nome), jpeg);

        ApagarArquivo(produto.FotoArquivo);
        produto.FotoArquivo = nome;
        produto.FotoUrl = null;
        produto.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return nome;
    }

    public async Task RemoverAsync(int produtoId)
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        var produto = await db.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        ApagarArquivo(produto.FotoArquivo);
        produto.FotoArquivo = null;
        produto.FotoUrl = null;
        produto.AtualizadoEm = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    /// <summary>Quantas fotos ainda não foram enviadas.</summary>
    public async Task<int> ContarPendentesAsync()
    {
        await using var db = await _fabrica.CreateDbContextAsync();
        return await db.Produtos.CountAsync(p => p.FotoArquivo != null && p.FotoUrl == null);
    }

    /// <summary>Envia as fotos pendentes. A primeira falha de rede/login para tudo; o que já subiu fica gravado.</summary>
    public async Task<ResultadoEnvioFotos> EnviarPendentesAsync(CancellationToken ct = default)
    {
        if (_enviador is null) throw new RegraDeNegocioException("O envio de fotos não está configurado.");
        await using var db = await _fabrica.CreateDbContextAsync(ct);
        var pendentes = await db.Produtos.Where(p => p.FotoArquivo != null && p.FotoUrl == null).ToListAsync(ct);
        int enviadas = 0, falhas = 0;
        string? erro = null;
        foreach (var p in pendentes)
        {
            var caminho = CaminhoDe(p.FotoArquivo);
            if (caminho is null) { falhas++; erro ??= $"A foto de \"{p.Nome}\" não está mais no disco."; continue; }
            try
            {
                var url = await _enviador.EnviarAsync($"produtos/{p.FotoArquivo}", await File.ReadAllBytesAsync(caminho, ct), ct);
                p.FotoUrl = url;
                p.AtualizadoEm = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);
                enviadas++;
            }
            catch (Sincronizacao.FalhaDestinoException ex)
            {
                falhas++;
                erro ??= ex.Message;
                if (ex.Gravidade == Sincronizacao.GravidadeFalha.Interrompe) break;
            }
        }
        return new ResultadoEnvioFotos(enviadas, falhas, erro);
    }

    private void ApagarArquivo(string? fotoArquivo)
    {
        var caminho = CaminhoDe(fotoArquivo);
        if (caminho is null) return;
        try { File.Delete(caminho); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
