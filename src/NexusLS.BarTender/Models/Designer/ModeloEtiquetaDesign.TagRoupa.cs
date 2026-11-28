using System.Windows;

namespace NexusLS.BarTender.Models.Designer;

/// <summary>
/// Tags de roupa 50 × 75 mm (rolo de 2 colunas, com furo para o barbante no topo).
/// Regras de todas as variantes: topo livre para o furo, nada a menos de 4 mm das laterais e do pé,
/// e caixa do código com 37,4 mm, que deixa área branca dos dois lados para o leitor, também quando a
/// impressão encolhe a tag para 63 mm ("Descer a impressão").
/// </summary>
public partial class ModeloEtiquetaDesign
{
    public const double TagRoupaLarguraMm = 50.0;
    public const double TagRoupaAlturaMm = 75.0;

    /// <summary>Nome da camada que recebe o logotipo da loja. Sem logotipo, mostra o nome da loja.</summary>
    public const string NomeCamadaMarca = "Marca da loja";

    public static ModeloEtiquetaDesign CriarTagRoupaSemPreco50x75()
    {
        var m = NovaTagRoupa("Tag Vestuário 50 × 75 mm · A (sem preço)");
        Marca(m, x: 11, y: 11, largura: 28, altura: 10.2);
        NomeDoProduto(m, y: 22.1, fonte: 11);
        Divisoria(m, 30.5);
        Tamanho(m, y: 28.3, altura: 18.6, fonte: 52);
        Referencia(m, y: 44.4, fonte: 12);
        Codigo(m, y: 50, altura: 20.5);
        return m;
    }

    public static ModeloEtiquetaDesign CriarTagRoupaComPreco50x75()
    {
        var m = NovaTagRoupa("Tag Vestuário 50 × 75 mm · B (com preço)");
        Marca(m, x: 13, y: 10.5, largura: 24, altura: 8.7);
        NomeDoProduto(m, y: 19.4, fonte: 10.5);
        Tamanho(m, y: 24.8, altura: 14.4, fonte: 40);
        Referencia(m, y: 37, fonte: 11);
        Codigo(m, y: 41.8, altura: 20);
        Divisoria(m, 62.4);
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Preço",
            Tipo = TipoElemento.Texto,
            X = 4, Y = 62.6, Largura = 42, Altura = 8.6,
            Texto = "R$ {Preco}",
            Variavel = VariavelEtiqueta.PrecoVarejo,
            TamanhoFonte = 23,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });
        return m;
    }

    public static ModeloEtiquetaDesign CriarTagRoupaCompacta50x75()
    {
        var m = NovaTagRoupa("Tag Vestuário 50 × 75 mm · C (compacta, mais folga)");
        Marca(m, x: 15, y: 12.5, largura: 20, altura: 7.3);
        NomeDoProduto(m, y: 20.2, fonte: 10);
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Faixa do tamanho",
            Tipo = TipoElemento.Retangulo,
            X = 6, Y = 29, Largura = 38, Altura = 11.6,
            Preenchido = true,
            CorFundo = "#000000",
            RaioCanto = 1.5,
            EspessuraBorda = 0.4
        });
        // A caixa do texto é mais alta que a faixa de propósito: o desenho da letra fica no meio dela.
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Tamanho",
            Tipo = TipoElemento.Texto,
            X = 6, Y = 27.65, Largura = 38, Altura = 13.4,
            Texto = "{Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = 36,
            Negrito = true,
            CorTexto = "#FFFFFF",
            Alinhamento = TextAlignment.Center
        });
        Referencia(m, y: 41.4, fonte: 11);
        Codigo(m, y: 47, altura: 20);
        return m;
    }

    /// <summary>
    /// Troca o nome da loja escrito pelo logotipo (PNG em base64). Sem logotipo, ou em modelo sem
    /// a camada da marca, nada muda.
    /// </summary>
    public ModeloEtiquetaDesign AplicarLogo(string? imagemBase64)
    {
        if (string.IsNullOrWhiteSpace(imagemBase64)) return this;

        foreach (var el in Elementos)
        {
            if (el.Nome != NomeCamadaMarca || el.Variavel != VariavelEtiqueta.NomeLoja) continue;

            el.Tipo = TipoElemento.Imagem;
            el.Variavel = VariavelEtiqueta.Nenhuma;
            el.Texto = "";
            el.ImagemBase64 = imagemBase64;
            el.ManterProporcao = true;
        }

        return this;
    }

    private static ModeloEtiquetaDesign NovaTagRoupa(string nome) => new()
    {
        Nome = nome,
        LarguraMm = TagRoupaLarguraMm,
        AlturaMm = TagRoupaAlturaMm
    };

    private static void Marca(ModeloEtiquetaDesign m, double x, double y, double largura, double altura) =>
        m.Elementos.Add(new ElementoDesign
        {
            Nome = NomeCamadaMarca,
            Tipo = TipoElemento.Texto,
            X = x, Y = y, Largura = largura, Altura = altura,
            Texto = "{Loja}",
            Variavel = VariavelEtiqueta.NomeLoja,
            TamanhoFonte = 15,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

    // Altura para duas linhas: nome comprido quebra e, se ainda não couber, termina em reticências.
    private static void NomeDoProduto(ModeloEtiquetaDesign m, double y, double fonte) =>
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Nome do Produto",
            Tipo = TipoElemento.Texto,
            X = 4, Y = y, Largura = 42, Altura = Math.Round(fonte * 2.75 * 25.4 / 96, 1),
            Texto = "{Nome}",
            Variavel = VariavelEtiqueta.NomeProduto,
            TamanhoFonte = fonte,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

    private static void Divisoria(ModeloEtiquetaDesign m, double y) =>
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Divisória",
            Tipo = TipoElemento.Linha,
            X = 8, Y = y, Largura = 34, Altura = 0.5,
            EspessuraBorda = 1.0
        });

    private static void Tamanho(ModeloEtiquetaDesign m, double y, double altura, double fonte) =>
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Tamanho",
            Tipo = TipoElemento.Texto,
            X = 4, Y = y, Largura = 42, Altura = altura,
            Texto = "{Tamanho}",
            Variavel = VariavelEtiqueta.Tamanho,
            TamanhoFonte = fonte,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

    private static void Referencia(ModeloEtiquetaDesign m, double y, double fonte) =>
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Referência",
            Tipo = TipoElemento.Texto,
            X = 4, Y = y, Largura = 42, Altura = 5,
            Texto = "REF. {Ref}",
            Variavel = VariavelEtiqueta.Referencia,
            TamanhoFonte = fonte,
            Negrito = true,
            Alinhamento = TextAlignment.Center
        });

    private static void Codigo(ModeloEtiquetaDesign m, double y, double altura) =>
        m.Elementos.Add(new ElementoDesign
        {
            Nome = "Código de Barras 1D",
            Tipo = TipoElemento.CodigoBarras,
            X = 6.3, Y = y, Largura = 37.4, Altura = altura,
            TipoCodigo = TipoSimboloCodigo.Code128,
            Variavel = VariavelEtiqueta.CodigoBarras,
            MostrarTextoLegivel = true
        });
}
