using CommunityToolkit.Mvvm.ComponentModel;
using NexusLS.Core.Fiscal;
using NexusLS.Core.Models;

namespace NexusLS.App.ViewModels;

/// <summary>Campos fiscais do cadastro de produto (CEST, ICMS, PIS/COFINS, unidade e IBS/CBS da reforma tributária).</summary>
public partial class FiscalProdutoViewModel : ObservableObject
{
    public IReadOnlyList<string> Unidades => RegrasFiscaisProduto.Unidades;
    public IReadOnlyList<string> Origens => RegrasFiscaisProduto.OrigensTexto;
    public IReadOnlyList<string> Csosn => RegrasFiscaisProduto.CsosnConhecidos;

    [ObservableProperty] private string _cest = "";
    [ObservableProperty] private string _origem = RegrasFiscaisProduto.OrigensTexto[0];
    [ObservableProperty] private string _cstIcms = "";
    [ObservableProperty] private string _aliquotaIcms = "";
    [ObservableProperty] private string _cstPis = "";
    [ObservableProperty] private string _aliquotaPis = "";
    [ObservableProperty] private string _cstCofins = "";
    [ObservableProperty] private string _aliquotaCofins = "";
    [ObservableProperty] private string _unidade = "";
    [ObservableProperty] private string _cstIbsCbs = "";
    [ObservableProperty] private string _classTribIbsCbs = "";

    public void Limpar() => Carregar(null);

    public void Carregar(Produto? p)
    {
        var d = p is null ? new DadosFiscaisProduto() : RegrasFiscaisProduto.Ler(p);
        Cest = d.Cest ?? "";
        var o = int.TryParse(d.Origem, out var oi) && oi is >= 0 and <= 8 ? oi : 0;
        Origem = RegrasFiscaisProduto.OrigensTexto[o];
        CstIcms = d.CstIcms ?? "";
        AliquotaIcms = d.AliquotaIcms ?? "";
        CstPis = d.CstPis ?? "";
        AliquotaPis = d.AliquotaPis ?? "";
        CstCofins = d.CstCofins ?? "";
        AliquotaCofins = d.AliquotaCofins ?? "";
        Unidade = d.Unidade ?? "";
        CstIbsCbs = d.CstIbsCbs ?? "";
        ClassTribIbsCbs = d.ClassTribIbsCbs ?? "";
    }

    public DadosFiscaisProduto ParaDados() => new(Cest, Origem, CstIcms, AliquotaIcms, CstPis, AliquotaPis, CstCofins,
        AliquotaCofins, Unidade, CstIbsCbs, ClassTribIbsCbs);
}
