using System.Text.Json;

namespace NexusLS.Core.Sincronizacao.Nuvem;

/// <summary>Resumo de um dia de uma loja, como foi baixado (só o que o comparativo mostra).</summary>
public sealed class DiaLido
{
    public string Dia { get; set; } = "";
    public long FaturamentoCentavos { get; set; }
    public int NumeroDeVendas { get; set; }
    public int Pecas { get; set; }
}

/// <summary>Uma loja como foi baixada da nuvem, com os dias que já se conhecem dela.</summary>
public sealed class LojaLida
{
    public string LojaId { get; set; } = "";
    public string Nome { get; set; } = "";
    public string VersaoNexus { get; set; } = "";
    public DateTime AtualizadoEmUtc { get; set; }
    public List<DiaLido> Dias { get; set; } = new();
}

/// <summary>
/// O que este computador lembra da nuvem das lojas: o código da empresa, o último envio, o último dado baixado (para mostrar
/// sem internet, com a data) e os "carimbos" que evitam reenviar o que não mudou. Fica num JSON AO LADO do banco (não dentro dele).
/// </summary>
public sealed class EstadoNuvemLoja
{
    /// <summary>Código da empresa nos caminhos <c>empresas/{empresaId}/lojas/...</c> (o mesmo para todas as lojas da empresa).</summary>
    public string EmpresaId { get; set; } = "";

    public DateTime? UltimoEnvioUtc { get; set; }

    /// <summary>O "atualizadoEm" do documento desta loja que ESTE computador gravou por último (para notar outro computador com o mesmo código).</summary>
    public DateTime? UltimoDocLojaUtc { get; set; }

    public DateTime? UltimaLeituraUtc { get; set; }
    public DateTime? UltimoRecebimentoPrecosUtc { get; set; }
    public List<LojaLida> Lojas { get; set; } = new();

    /// <summary>Impressão digital do último estoque enviado de cada produto (produto → hash): só reenvia se mudou.</summary>
    public Dictionary<string, string> HashesEstoque { get; set; } = new();
}

public sealed class CacheNuvemLoja
{
    private readonly string _caminho;
    private readonly object _trava = new();

    public CacheNuvemLoja(string caminho) => _caminho = caminho;

    /// <summary>Ao lado do banco (mesma pasta).</summary>
    public static string CaminhoAoLadoDoBanco(string caminhoDoBanco) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(caminhoDoBanco))!, "nuvem_loja.json");

    /// <summary>Arquivo ausente ou estragado vira um estado vazio (nunca falha).</summary>
    public EstadoNuvemLoja Carregar()
    {
        lock (_trava)
        {
            try
            {
                return File.Exists(_caminho) ? JsonSerializer.Deserialize<EstadoNuvemLoja>(File.ReadAllText(_caminho)) ?? new() : new();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return new();
            }
        }
    }

    /// <summary>Grava e troca o arquivo de uma vez: uma queda no meio não deixa o arquivo pela metade.</summary>
    public void Salvar(EstadoNuvemLoja estado)
    {
        lock (_trava)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_caminho))!);
            var tmp = _caminho + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(estado, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, _caminho, overwrite: true);
        }
    }

    public void Atualizar(Action<EstadoNuvemLoja> mudar)
    {
        lock (_trava)
        {
            var e = Carregar();
            mudar(e);
            Salvar(e);
        }
    }
}
