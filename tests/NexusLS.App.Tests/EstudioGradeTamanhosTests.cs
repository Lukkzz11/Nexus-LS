using System.IO;
using NexusLS.BarTender.Services;
using NexusLS.BarTender.ViewModels;
using NexusLS.Core.Models;
using NexusLS.Core.Services;
using Xunit;

namespace NexusLS.App.Tests;

public sealed class EstudioGradeTamanhosTests : IDisposable
{
    private readonly string _preferencias = Path.Combine(Path.GetTempPath(), $"nexus-grade-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_preferencias)) File.Delete(_preferencias);
    }

    private static Produto SemTamanhos() => new()
    {
        Id = 9001,
        Nome = "BERMUDA SOCIAL DE LINHO",
        Referencia = "1453",
        PrecoVendaCentavos = 8500,
        Tamanhos = new List<ProdutoTamanho> { new() { Tamanho = Tamanhos.SemTamanho, Ordem = 0, Estoque = 3 } }
    };

    private DesignerViewModel Estudio(Produto produto)
    {
        var vm = new DesignerViewModel { CaminhoPreferencias = _preferencias };
        vm.DefinirProduto(produto, "LOJA", carregarEtiquetaSalvaSeHouver: false);
        return vm;
    }

    [Fact]
    public void ProdutoSemTamanhos_EscolheGrade_MostraOsTamanhosELembraDaEscolha()
    {
        var vm = Estudio(SemTamanhos());
        Assert.Equal(new[] { Tamanhos.SemTamanho }, vm.TamanhosParaTesteDisponiveis);

        vm.GradeSelecionada = GradesDeTamanho.CalcaBermuda.Nome;

        Assert.Equal(GradesDeTamanho.CalcaBermuda.Tamanhos, vm.TamanhosParaTesteDisponiveis);
        Assert.Equal("34", vm.TamanhoParaTeste);
        Assert.True(vm.TamanhoForaDoCadastro);

        var depois = Estudio(SemTamanhos());
        Assert.Equal(GradesDeTamanho.CalcaBermuda.Nome, depois.GradeSelecionada);

        depois.GradeSelecionada = DesignerViewModel.GradeDoCadastro;
        Assert.Equal(new[] { Tamanhos.SemTamanho }, depois.TamanhosParaTesteDisponiveis);
    }

    [Fact]
    public void EtiquetaSemCampoDeTamanho_Avisa_EOAvisoSomeAoColocar()
    {
        var vm = Estudio(SemTamanhos());
        vm.Modelo = NexusLS.BarTender.Models.Designer.ModeloEtiquetaDesign.CriarEmBranco(35, 60, "Tag");
        vm.GradeSelecionada = GradesDeTamanho.Letras.Nome;
        Assert.True(vm.FaltaTamanhoNaEtiqueta);

        vm.AdicionarTamanhoCommand.Execute(null);

        Assert.False(vm.FaltaTamanhoNaEtiqueta);
    }

    [Fact]
    public void TamanhoForaDoCadastro_NaoGanhaNumeroNoCodigo_ParaOPdvConseguirLer()
    {
        Assert.Equal("1453", CodigoBarrasTamanho.Resolver(SemTamanhos(), "M"));
    }
}
