using System.Globalization;
using NexusLS.Core.Services;

namespace NexusLS.App.ViewModels;

/// <summary>Uma fatia pronta para a tela: o desenho (texto de Path), a cor e a legenda.</summary>
public record FatiaDesenhada(string Rotulo, string Legenda, string Caminho, string Cor);

/// <summary>Monta o desenho de uma pizza (só texto, sem janela). Raio 100, centro em (100,100).</summary>
public static class PizzaGrafico
{
    public const double Raio = 100;
    private static readonly string[] Cores = { "#2563EB", "#16A34A", "#F59E0B", "#DC2626", "#7C3AED", "#0891B2" };
    public const string CorOutros = "#9CA3AF";

    public static List<FatiaDesenhada> Desenhar(IReadOnlyList<FatiaPizza> fatias)
    {
        var resultado = new List<FatiaDesenhada>();
        double inicio = 0;
        for (var i = 0; i < fatias.Count; i++)
        {
            var f = fatias[i];
            var cor = f.Rotulo == Participacao.RotuloOutros ? CorOutros : Cores[i % Cores.Length];
            var legenda = $"{f.Rotulo} · {Dinheiro.Formatar(f.ValorCentavos)} · {f.Percentual:0.#}%";
            resultado.Add(new FatiaDesenhada(f.Rotulo, legenda, Caminho(inicio, f.Fracao), cor));
            inicio += f.Fracao;
        }
        return resultado;
    }

    /// <summary>Caminho da fatia que começa em <paramref name="inicio"/> (0 a 1, a partir do topo) e ocupa <paramref name="fracao"/>.</summary>
    public static string Caminho(double inicio, double fracao)
    {
        const double c = Raio;
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        if (fracao >= 0.9999) // círculo inteiro: um arco só não fecha
            return $"M {N(c)},{N(c - Raio)} A {N(Raio)},{N(Raio)} 0 1 1 {N(c)},{N(c + Raio)} A {N(Raio)},{N(Raio)} 0 1 1 {N(c)},{N(c - Raio)} Z";

        (double x, double y) Ponto(double f)
        {
            var angulo = f * 2 * Math.PI - Math.PI / 2; // começa no topo, sentido horário
            return (c + Raio * Math.Cos(angulo), c + Raio * Math.Sin(angulo));
        }
        var (x1, y1) = Ponto(inicio);
        var (x2, y2) = Ponto(inicio + fracao);
        var grande = fracao > 0.5 ? 1 : 0;
        return $"M {N(c)},{N(c)} L {N(x1)},{N(y1)} A {N(Raio)},{N(Raio)} 0 {grande} 1 {N(x2)},{N(y2)} Z";
    }
}
