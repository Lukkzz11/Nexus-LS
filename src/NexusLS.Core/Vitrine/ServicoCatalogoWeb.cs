using System.Text.Json;
using System.Text.Json.Nodes;
using NexusLS.Core.Services;

namespace NexusLS.Core.Vitrine;

/// <summary>
/// Guarda neste computador a vitrine completa do editor visual (o JSON do editor, sem interpretar). O editor em JSX
/// é quem conhece o formato; o programa só guarda e publica.
/// </summary>
public class VitrineCompletaStore
{
    private readonly string _caminho;
    public VitrineCompletaStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "vitrine-completa.json");

    public string? Carregar()
    {
        try { return File.Exists(_caminho) ? File.ReadAllText(_caminho) : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    public void Salvar(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _caminho, overwrite: true);
    }
}

public record DadosCatalogoWeb(string? VitrineJson, JsonObject Empresa, JsonArray Produtos);

/// <summary>
/// O que o editor e o catálogo em JSX (rodando dentro do WebView2) pedem ao programa: produtos do estoque, a vitrine
/// salva, imagens do editor e a publicação. Nada vai ao Firebase fora de <see cref="PublicarAsync"/> (só no clique).
/// </summary>
public class ServicoCatalogoWeb
{
    /// <summary>Endereços virtuais que o WebView2 serve a partir de pastas do computador (nunca da internet).</summary>
    public const string HostFotos = "fotos.nexus.local";
    public const string HostImagensVitrine = "vitrine.nexus.local";

    public const long TamanhoMaximoImagem = 12L * 1024 * 1024;

    private readonly ProdutoService _produtos;
    private readonly ConfiguracaoService _configuracao;
    private readonly VitrineCompletaStore _store;
    private readonly string _pastaImagens;
    private readonly IEnviadorFoto? _enviador;
    private readonly IPublicadorVitrineCompleta? _publicador;
    private readonly Func<DateTime> _agora;
    private readonly AjustesProdutoStore _ajustes;

    public ServicoCatalogoWeb(ProdutoService produtos, ConfiguracaoService configuracao, VitrineCompletaStore store,
        string pastaImagens, IEnviadorFoto? enviador, IPublicadorVitrineCompleta? publicador, Func<DateTime>? agora = null,
        AjustesProdutoStore? ajustes = null)
    {
        _produtos = produtos;
        _configuracao = configuracao;
        _store = store;
        _pastaImagens = pastaImagens;
        _enviador = enviador;
        _publicador = publicador;
        _agora = agora ?? (() => DateTime.UtcNow);
        _ajustes = ajustes ?? new AjustesProdutoStore(AjustesProdutoStore.CaminhoPadrao());
    }

    public static string PastaImagensPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "vitrine-imagens");

    public static string UrlImagemLocal(string arquivo) => $"https://{HostImagensVitrine}/{arquivo}";

    /// <summary>Estoque local no formato que o catálogo em JSX espera (preços em reais, grade como objeto).</summary>
    public async Task<DadosCatalogoWeb> CarregarAsync()
    {
        var loja = await _configuracao.ObterAsync();
        var lista = await _produtos.BuscarAsync(null);
        var produtos = new JsonArray();
        var ajustes = _ajustes.Carregar();
        foreach (var p in lista)
        {
            var id = p.Guid.ToString("D").ToLowerInvariant();
            var aj = ajustes.GetValueOrDefault(id) ?? new AjusteProdutoCatalogo();
            var grade = new JsonObject();
            foreach (var t in await _produtos.ListarTamanhosAsync(p.Id))
                if (!t.EhSemTamanho) grade[t.Tamanho] = t.Estoque;

            string foto = p.FotoUrl ?? (string.IsNullOrWhiteSpace(p.FotoArquivo) ? "" : $"https://{HostFotos}/{Path.GetFileName(p.FotoArquivo)}");
            produtos.Add(new JsonObject
            {
                ["id"] = id,
                ["nome"] = p.Nome,
                ["categoria"] = p.Categoria ?? "",
                ["referencia"] = p.Referencia ?? "",
                ["precoVarejo"] = p.PrecoVendaCentavos / 100.0,
                ["precoAtacado"] = p.PrecoAtacadoCentavos / 100.0,
                ["precoPromocional"] = aj.PromocaoCentavos / 100.0,
                ["descricao"] = "",
                ["fotoUrl"] = foto,
                ["cores"] = new JsonArray(),
                ["ativo"] = p.Ativo,
                ["ativoNoCatalogo"] = aj.Visivel,
                ["destaque"] = aj.Destaque,
                ["gradeTamanhos"] = grade,
                ["createdAt"] = p.AtualizadoEm.ToString("O")
            });
        }
        var empresa = new JsonObject
        {
            ["nome"] = loja.NomeLoja,
            ["telefone"] = loja.Telefone ?? "",
            ["whatsapp"] = "",
            ["endereco"] = loja.Endereco ?? "",
            ["cidade"] = "",
            ["bairro"] = "",
            ["chavePix"] = "",
            ["mensagemCatalogo"] = ""
        };
        return new DadosCatalogoWeb(_store.Carregar(), empresa, produtos);
    }

    /// <summary>
    /// Guarda neste computador como o produto aparece no catálogo (visível, destaque, promoção). A promoção, se houver,
    /// tem de ser menor que o preço de varejo. Só vai para a nuvem em <see cref="PublicarAsync"/>.
    /// </summary>
    public async Task SalvarAjusteProdutoAsync(string id, bool visivel, bool destaque, long promocaoCentavos)
    {
        id = (id ?? "").Trim().ToLowerInvariant();
        if (!Guid.TryParse(id, out _)) throw new RegraDeNegocioException("Produto inválido.");
        if (promocaoCentavos < 0) throw new RegraDeNegocioException("O preço promocional não pode ser negativo.");
        var produto = (await _produtos.BuscarAsync(null, incluirInativos: true)).FirstOrDefault(p => p.Guid.ToString("D") == id)
            ?? throw new RegraDeNegocioException("Produto não encontrado.");
        if (promocaoCentavos > 0 && promocaoCentavos >= produto.PrecoVendaCentavos)
            throw new RegraDeNegocioException("O preço promocional precisa ser menor que o preço de varejo.");

        var todos = _ajustes.Carregar();
        var novo = new AjusteProdutoCatalogo(visivel, destaque, promocaoCentavos);
        if (novo == new AjusteProdutoCatalogo()) todos.Remove(id); // tudo no padrão: não guarda nada
        else todos[id] = novo;
        _ajustes.Salvar(todos);
    }

    /// <summary>Guarda a imagem (já reduzida pelo editor, data URL) na pasta desta instalação e devolve o endereço local e o "path".</summary>
    public (string Url, string Path) SalvarImagem(string dataUrl)
    {
        var (bytes, extensao) = LerDataUrl(dataUrl);
        if (bytes.Length == 0) throw new RegraDeNegocioException("O arquivo da imagem está vazio.");
        if (bytes.Length > TamanhoMaximoImagem) throw new RegraDeNegocioException("A imagem é grande demais (máximo 12 MB).");
        Directory.CreateDirectory(_pastaImagens);
        var arquivo = $"{Guid.NewGuid():N}{extensao}";
        File.WriteAllBytes(System.IO.Path.Combine(_pastaImagens, arquivo), bytes);
        return (UrlImagemLocal(arquivo), $"vitrine/{arquivo}");
    }

    public void ApagarImagem(string path)
    {
        // Só apaga arquivos desta pasta, pelo nome (nada de caminhos vindos da página).
        var nome = System.IO.Path.GetFileName(path ?? "");
        if (nome.Length == 0) return;
        var caminho = System.IO.Path.Combine(_pastaImagens, nome);
        try { if (File.Exists(caminho)) File.Delete(caminho); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* fica no disco; não atrapalha */ }
    }

    /// <summary>Guarda o rascunho neste computador (sem internet).</summary>
    public void SalvarLocal(string vitrineJson)
    {
        ValidarJson(vitrineJson);
        _store.Salvar(vitrineJson);
    }

    /// <summary>
    /// Publica: guarda local, sobe ao Storage as imagens que ainda são locais, troca os endereços e grava no Firestore.
    /// Devolve o JSON já com os endereços públicos. Se a internet falhar, o rascunho local continua salvo.
    /// </summary>
    public async Task<(string Json, DateTime PublicadoEm, int AjustesEnviados)> PublicarAsync(string vitrineJson, CancellationToken ct = default)
    {
        if (_publicador is null) throw new RegraDeNegocioException("A publicação do catálogo não está configurada.");
        var raiz = ValidarJson(vitrineJson);
        _store.Salvar(vitrineJson);

        var enviadas = new Dictionary<string, string>();
        await SubirImagensAsync(raiz, enviadas, ct);
        var final = raiz.ToJsonString();
        await _publicador.PublicarCompletaAsync(final, ct);
        _store.Salvar(final);
        var ajustesEnviados = await PublicarAjustesAsync(ct);
        return (final, _agora(), ajustesEnviados);
    }

    /// <summary>Manda os ajustes dos produtos (visível, destaque, promoção). Produto que ainda não foi sincronizado é pulado.</summary>
    private async Task<int> PublicarAjustesAsync(CancellationToken ct)
    {
        if (_publicador is not IPublicadorAjustesProduto envio) return 0;
        var enviados = 0;
        foreach (var (id, aj) in _ajustes.Carregar())
            if (await envio.PublicarAjusteAsync(id, aj.Visivel, aj.Destaque, aj.PromocaoCentavos, ct)) enviados++;
        return enviados;
    }

    private async Task SubirImagensAsync(JsonNode? no, Dictionary<string, string> enviadas, CancellationToken ct)
    {
        var prefixo = $"https://{HostImagensVitrine}/";
        switch (no)
        {
            case JsonObject objeto:
                foreach (var chave in objeto.Select(k => k.Key).ToList())
                {
                    if (objeto[chave] is JsonValue v && v.TryGetValue<string>(out var texto) && texto.StartsWith(prefixo, StringComparison.Ordinal))
                        objeto[chave] = await UrlPublicaAsync(texto[prefixo.Length..], enviadas, ct);
                    else
                        await SubirImagensAsync(objeto[chave], enviadas, ct);
                }
                break;
            case JsonArray lista:
                for (var i = 0; i < lista.Count; i++)
                {
                    if (lista[i] is JsonValue v && v.TryGetValue<string>(out var texto) && texto.StartsWith(prefixo, StringComparison.Ordinal))
                        lista[i] = await UrlPublicaAsync(texto[prefixo.Length..], enviadas, ct);
                    else
                        await SubirImagensAsync(lista[i], enviadas, ct);
                }
                break;
        }
    }

    private async Task<string> UrlPublicaAsync(string arquivo, Dictionary<string, string> enviadas, CancellationToken ct)
    {
        arquivo = System.IO.Path.GetFileName(arquivo);
        if (enviadas.TryGetValue(arquivo, out var pronta)) return pronta;
        if (_enviador is null) throw new RegraDeNegocioException("O envio de imagens não está configurado.");
        var caminho = System.IO.Path.Combine(_pastaImagens, arquivo);
        if (!File.Exists(caminho)) throw new RegraDeNegocioException("Uma imagem do editor não está mais no computador. Envie-a de novo.");
        var url = await _enviador.EnviarAsync($"vitrine/{arquivo}", await File.ReadAllBytesAsync(caminho, ct), ct);
        enviadas[arquivo] = url;
        return url;
    }

    private static JsonObject ValidarJson(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject ?? throw new RegraDeNegocioException("A vitrine enviada pelo editor é inválida.");
        }
        catch (JsonException)
        {
            throw new RegraDeNegocioException("A vitrine enviada pelo editor é inválida.");
        }
    }

    private static (byte[] Bytes, string Extensao) LerDataUrl(string dataUrl)
    {
        var virgula = dataUrl?.IndexOf(',') ?? -1;
        if (dataUrl is null || !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || virgula < 0)
            throw new RegraDeNegocioException("O arquivo escolhido não é uma imagem.");
        var cabecalho = dataUrl[..virgula].ToLowerInvariant();
        var extensao = cabecalho.Contains("image/png") ? ".png" : ".jpg";
        try { return (Convert.FromBase64String(dataUrl[(virgula + 1)..]), extensao); }
        catch (FormatException) { throw new RegraDeNegocioException("O arquivo escolhido não é uma imagem."); }
    }
}
