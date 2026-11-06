namespace NexusLS.BarTender.Models.Designer;

public enum TipoElemento
{
    Texto,
    Imagem,
    CodigoBarras,
    Retangulo,
    Linha
}

public enum TipoSimboloCodigo
{
    Code128,
    Ean13,
    QrCode,
    Code39
}

public enum VariavelEtiqueta
{
    Nenhuma,
    NomeProduto,
    PrecoVarejo,
    PrecoAtacado,
    Referencia,
    Tamanho,
    CodigoBarras,
    CodigoInterno,
    NomeLoja
}
