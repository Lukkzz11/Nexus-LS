using System.Globalization;
using System.Text.Json;
using NexusLS.Core.Services;

namespace NexusLS.Core.Vitrine;

/// <summary>As escolhas que o editor oferece (as mesmas ids do editor do ERP, para a vitrine pública entender).</summary>
public record Opcao(string Id, string Rotulo);

public static class OpcoesVitrine
{
    public static readonly IReadOnlyList<Opcao> Modelos = new Opcao[]
    {
        new("padrao", "Atual (vidro)"), new("minimalista", "Minimalista"), new("elegante", "Elegante"),
        new("comercial", "Comercial"), new("moderno", "Moderno"), new("promocional", "Promocional")
    };

    public static readonly IReadOnlyList<Opcao> Fontes = new Opcao[]
    {
        new("sistema", "Padrão do sistema"), new("moderna", "Moderna (sem serifa)"), new("classica", "Clássica (serifada)"),
        new("elegante", "Elegante (serifada fina)"), new("arredondada", "Arredondada"), new("condensada", "Condensada")
    };

    public static readonly IReadOnlyList<Opcao> Ordens = new Opcao[]
    {
        new("padrao", "Como está no estoque"), new("nome", "Nome (A a Z)"), new("menorPreco", "Menor preço primeiro"),
        new("maiorPreco", "Maior preço primeiro"), new("recentes", "Mais recentes primeiro")
    };

    /// <summary>As cores editáveis (as principais do ERP: o resto da paleta vem do modelo escolhido).</summary>
    public static readonly IReadOnlyList<Opcao> Cores = new Opcao[]
    {
        new("fundo", "Fundo geral do catálogo"), new("cartao", "Fundo dos produtos"), new("titulo", "Títulos"),
        new("preco", "Preço normal"), new("promo", "Preço promocional"), new("botao", "Botões"), new("botaoTexto", "Texto dos botões")
    };
}

public class BannerVitrine
{
    public string Selo { get; set; } = "";
    public string Titulo { get; set; } = "";
    public string Destaque { get; set; } = "";
    public string Subtitulo { get; set; } = "";
    public string BotaoRotulo { get; set; } = "";
}

/// <summary>
/// Como o catálogo online aparece: modelo, fonte, cores, textos, dados da loja, atacado e banners.
/// É o subconjunto do editor visual do ERP que não depende de arrastar blocos; tudo é validado em <see cref="Normalizar"/>
/// antes de ser salvo ou publicado.
/// </summary>
public class ConfiguracaoVitrine
{
    public string Modelo { get; set; } = "padrao";
    public string Fonte { get; set; } = "sistema";
    /// <summary>Cor por id (fundo, cartao, titulo...), em #rrggbb. Ausente = vale a do modelo.</summary>
    public Dictionary<string, string> Cores { get; set; } = new();

    public string TextoAnuncio { get; set; } = "FRETE GRÁTIS para retirada no balcão da confecção • Peças com pronta-entrega.";
    public string Eyebrow { get; set; } = "Coleção 2026 • Fabricação Própria";
    public string Titulo { get; set; } = "Moda & Coleção Surfwear";

    public string LojaNome { get; set; } = "";
    public string LojaApresentacao { get; set; } = "";
    public string Telefone { get; set; } = "";
    public string Whatsapp { get; set; } = "";
    public string Instagram { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string Email { get; set; } = "";

    public int AtacadoMinPecas { get; set; } = 10;
    public int NovidadesQtd { get; set; } = 8;
    public string OrdemProdutos { get; set; } = "padrao";
    public bool MostrarDescricao { get; set; }

    public List<BannerVitrine> Banners { get; set; } = new()
    {
        new() { Selo = "Acabou de chegar", Titulo = "Novidades da", Destaque = "coleção", Subtitulo = "Peças novas com fabricação própria e pronta-entrega.", BotaoRotulo = "Ver novidades" },
        new() { Selo = "Para lojistas", Titulo = "Atacado a partir de", Destaque = "10 peças", Subtitulo = "Pedido de atacado separado do varejo, com o valor de fábrica.", BotaoRotulo = "Abrir o atacado" }
    };

    public static string LimparTexto(string? valor, int max)
    {
        var t = new string((valor ?? "").Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        t = System.Text.RegularExpressions.Regex.Replace(t, "\\s+", " ").Trim();
        return t.Length > max ? t[..max] : t;
    }

    /// <summary>Aceita #RGB ou #RRGGBB (com ou sem #) e devolve #rrggbb minúsculo; senão, nulo.</summary>
    public static string? NormalizarHex(string? valor)
    {
        var s = (valor ?? "").Trim().TrimStart('#');
        if (s.Length == 3 && s.All(Uri.IsHexDigit)) s = string.Concat(s.Select(c => $"{c}{c}"));
        return s.Length == 6 && s.All(Uri.IsHexDigit) ? "#" + s.ToLowerInvariant() : null;
    }

    /// <summary>Confere e limpa tudo. Lança <see cref="RegraDeNegocioException"/> dizendo o que corrigir (cor inválida, números fora da faixa).</summary>
    public void Normalizar()
    {
        if (!OpcoesVitrine.Modelos.Any(m => m.Id == Modelo)) Modelo = "padrao";
        if (!OpcoesVitrine.Fontes.Any(f => f.Id == Fonte)) Fonte = "sistema";
        if (!OpcoesVitrine.Ordens.Any(o => o.Id == OrdemProdutos)) OrdemProdutos = "padrao";

        var cores = new Dictionary<string, string>();
        foreach (var (id, valor) in Cores)
        {
            var opcao = OpcoesVitrine.Cores.FirstOrDefault(c => c.Id == id);
            if (opcao is null || string.IsNullOrWhiteSpace(valor)) continue;          // cor desconhecida ou vazia: ignora
            cores[id] = NormalizarHex(valor)
                ?? throw new RegraDeNegocioException($"A cor \"{opcao.Rotulo}\" é inválida. Use o formato #RRGGBB (ex.: #1e40af).");
        }
        Cores = cores;

        TextoAnuncio = LimparTexto(TextoAnuncio, 160);
        Eyebrow = LimparTexto(Eyebrow, 80);
        Titulo = LimparTexto(Titulo, 80);
        LojaNome = LimparTexto(LojaNome, 80);
        LojaApresentacao = LimparTexto(LojaApresentacao, 300);
        Telefone = LimparTexto(Telefone, 30);
        Whatsapp = new string(Whatsapp.Where(char.IsDigit).ToArray());                  // só números (link do WhatsApp)
        if (Whatsapp.Length > 0 && (Whatsapp.Length < 10 || Whatsapp.Length > 15))
            throw new RegraDeNegocioException("O WhatsApp deve ter de 10 a 15 números, com DDD (ex.: 5581999990000).");
        Instagram = LimparTexto(Instagram, 60).TrimStart('@');
        Endereco = LimparTexto(Endereco, 160);
        Email = LimparTexto(Email, 80);
        if (Email.Length > 0 && !Email.Contains('@')) throw new RegraDeNegocioException("O e-mail da loja parece errado (falta o @).");

        if (AtacadoMinPecas is < 1 or > 1000) throw new RegraDeNegocioException("O mínimo de peças do atacado deve ficar entre 1 e 1000.");
        if (NovidadesQtd is < 1 or > 24) throw new RegraDeNegocioException("O carrossel de novidades mostra de 1 a 24 produtos.");

        foreach (var b in Banners)
        {
            b.Selo = LimparTexto(b.Selo, 40);
            b.Titulo = LimparTexto(b.Titulo, 80);
            b.Destaque = LimparTexto(b.Destaque, 40);
            b.Subtitulo = LimparTexto(b.Subtitulo, 160);
            b.BotaoRotulo = LimparTexto(b.BotaoRotulo, 30);
        }
    }

    /// <summary>Razão de contraste (1 a 21) entre duas cores #rrggbb, pela fórmula do WCAG.</summary>
    public static double Contraste(string hexA, string hexB)
    {
        var la = Luminancia(hexA); var lb = Luminancia(hexB);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminancia(string hex)
    {
        double Canal(int i)
        {
            var v = int.Parse(hex.AsSpan(1 + i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Canal(0) + 0.7152 * Canal(1) + 0.0722 * Canal(2);
    }

    /// <summary>Avisos (não erros) de leitura difícil entre as cores escolhidas. Só compara pares em que as duas cores foram definidas.</summary>
    public IReadOnlyList<string> AvisosDeContraste()
    {
        var avisos = new List<string>();
        void Par(string a, string b, string o)
        {
            if (Cores.TryGetValue(a, out var ca) && Cores.TryGetValue(b, out var cb) && Contraste(ca, cb) < 4.5)
                avisos.Add($"{o}: contraste baixo ({Contraste(ca, cb):0.0}:1; o ideal é 4,5:1 ou mais).");
        }
        Par("titulo", "fundo", "Título sobre o fundo");
        Par("preco", "cartao", "Preço sobre o fundo dos produtos");
        Par("promo", "cartao", "Preço promocional sobre o fundo dos produtos");
        Par("botaoTexto", "botao", "Texto do botão sobre o botão");
        return avisos;
    }
}

/// <summary>Guarda a configuração da vitrine em arquivo (fora do banco), com gravação segura.</summary>
public class VitrineStore
{
    private readonly string _caminho;
    public VitrineStore(string caminho) => _caminho = caminho;

    public static string CaminhoPadrao() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexusLS", "vitrine.json");

    public ConfiguracaoVitrine Carregar()
    {
        try
        {
            if (!File.Exists(_caminho)) return new();
            return JsonSerializer.Deserialize<ConfiguracaoVitrine>(File.ReadAllText(_caminho)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Salvar(ConfiguracaoVitrine config)
    {
        config.Normalizar();
        Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
        var tmp = _caminho + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, _caminho, overwrite: true);
    }
}
