using CommunityToolkit.Mvvm.ComponentModel;

namespace NexusLS.BarTender.Models;

public partial class ConfiguracaoEtiqueta : ObservableObject
{
    [ObservableProperty]
    private FormatoOpcao _formato = FormatoOpcao.Bobina50x30;

    [ObservableProperty]
    private string _nomeLoja = "NEXUS LS";

    [ObservableProperty]
    private bool _mostrarNomeLoja = true;

    [ObservableProperty]
    private bool _mostrarNomeProduto = true;

    [ObservableProperty]
    private bool _mostrarReferencia = true;

    [ObservableProperty]
    private bool _mostrarPrecoVarejo = true;

    [ObservableProperty]
    private bool _mostrarPrecoAtacado = true;

    [ObservableProperty]
    private bool _mostrarCodigoInterno = true;

    [ObservableProperty]
    private bool _mostrarTamanho = true;

    [ObservableProperty]
    private bool _mostrarCodigoBarras = true;

    [ObservableProperty]
    private TipoCodigoBarras _tipoCodigo = TipoCodigoBarras.Ean13;
}
