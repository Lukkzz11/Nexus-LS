using System.Globalization;
using NexusLS.App.Converters;
using NexusLS.App.ViewModels;

namespace NexusLS.App.Tests;

/// <summary>O DRE partido em dois cartões (lado a lado em tela larga) não pode perder nem repetir nenhuma linha.</summary>
public class TrechoDoDreConverterTests
{
    private static readonly List<LinhaDre> Dre = new()
    {
        new("Receita bruta", "R$ 10,00"),
        new("= Resultado do período", "R$ 5,00", Destaque: true),
        new("Entrou ou saiu, mas NÃO é receita nem despesa", "", Titulo: true),
        new("Aportes", "R$ 1,00", Recuo: true),
        new("Fluxo de caixa", "", Titulo: true),
        new("= Fluxo líquido", "R$ 2,00", Destaque: true),
    };

    private static List<LinhaDre> Trecho(object lista, string parte) =>
        ((IEnumerable<LinhaDre>)new TrechoDoDreConverter().Convert(new[] { lista, (object)Dre.Count }, typeof(object), parte, CultureInfo.InvariantCulture)).ToList();

    [Fact]
    public void Parte_no_primeiro_titulo_sem_perder_linha()
    {
        var resultado = Trecho(Dre, "resultado");
        var fora = Trecho(Dre, "fora");

        Assert.Equal(Dre.Take(2), resultado);
        Assert.Equal(Dre.Skip(2), fora);
        Assert.Equal(Dre, resultado.Concat(fora));
    }

    [Fact]
    public void Sem_titulo_tudo_fica_no_resultado_e_lista_vazia_nao_quebra()
    {
        var semTitulo = Dre.Take(2).ToList();
        Assert.Equal(semTitulo, Trecho(semTitulo, "resultado"));
        Assert.Empty(Trecho(semTitulo, "fora"));
        Assert.Empty(Trecho(new List<LinhaDre>(), "resultado"));
    }
}
