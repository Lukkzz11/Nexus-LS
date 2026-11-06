using System.Text.Json.Serialization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusLS.BarTender.Models.Designer;

public partial class ElementoDesign : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString("N");

    [ObservableProperty]
    private TipoElemento _tipo = TipoElemento.Texto;

    // Coordenadas e dimensões em milímetros (mm)
    [ObservableProperty]
    private double _x = 2.0;

    [ObservableProperty]
    private double _y = 2.0;

    [ObservableProperty]
    private double _largura = 20.0;

    [ObservableProperty]
    private double _altura = 6.0;

    // Propriedades para TipoElemento.Texto
    [ObservableProperty]
    private string _texto = "Texto Exemplo";

    [ObservableProperty]
    private VariavelEtiqueta _variavel = VariavelEtiqueta.Nenhuma;

    [ObservableProperty]
    private double _tamanhoFonte = 10.0;

    [ObservableProperty]
    private bool _negrito = false;

    [ObservableProperty]
    private bool _italico = false;

    [ObservableProperty]
    private bool _riscado = false;

    [ObservableProperty]
    private TextAlignment _alinhamento = TextAlignment.Left;

    // Propriedades para TipoElemento.Imagem
    [ObservableProperty]
    private string? _caminhoImagem;

    [ObservableProperty]
    private string? _imagemBase64;

    [ObservableProperty]
    private bool _manterProporcao = true;

    [ObservableProperty]
    private bool _monocromatico = false;

    // Propriedades para TipoElemento.CodigoBarras
    [ObservableProperty]
    private TipoSimboloCodigo _tipoCodigo = TipoSimboloCodigo.Code128;

    [ObservableProperty]
    private string? _valorCodigo;

    [ObservableProperty]
    private bool _mostrarTextoLegivel = true;

    // Propriedades para Retângulo e Linha
    [ObservableProperty]
    private double _espessuraBorda = 1.0;

    [ObservableProperty]
    private bool _preenchido = false;

    // Estado de seleção visual na tela (não persiste em JSON)
    [JsonIgnore]
    [ObservableProperty]
    private bool _isSelected;

    public ElementoDesign Clonar()
    {
        return new ElementoDesign
        {
            Id = Guid.NewGuid().ToString("N"),
            Tipo = this.Tipo,
            X = this.X + 2.0,
            Y = this.Y + 2.0,
            Largura = this.Largura,
            Altura = this.Altura,
            Texto = this.Texto,
            Variavel = this.Variavel,
            TamanhoFonte = this.TamanhoFonte,
            Negrito = this.Negrito,
            Italico = this.Italico,
            Riscado = this.Riscado,
            Alinhamento = this.Alinhamento,
            CaminhoImagem = this.CaminhoImagem,
            ImagemBase64 = this.ImagemBase64,
            ManterProporcao = this.ManterProporcao,
            Monocromatico = this.Monocromatico,
            TipoCodigo = this.TipoCodigo,
            ValorCodigo = this.ValorCodigo,
            MostrarTextoLegivel = this.MostrarTextoLegivel,
            EspessuraBorda = this.EspessuraBorda,
            Preenchido = this.Preenchido
        };
    }
}
