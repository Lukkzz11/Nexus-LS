using System.Text.Json;
using NexusLS.BarTender.Models.Designer;

namespace NexusLS.BarTender.Services;

/// <summary>
/// Prepara uma cópia do modelo de etiqueta para a mídia escolhida. A cópia é reduzida/ampliada
/// em escala única (sem esticar) e centralizada, então o desenho mantém a proporção e a posição
/// relativa dos elementos. O modelo original do produto nunca é alterado.
/// </summary>
public static class ModeloMidiaService
{
    public sealed record Ajuste(ModeloEtiquetaDesign Modelo, double Fator, bool Alterado, string Descricao);

    public static ModeloEtiquetaDesign Clonar(ModeloEtiquetaDesign modelo)
    {
        var json = JsonSerializer.Serialize(modelo, ModeloEtiquetaDesign.JsonOpts);
        return JsonSerializer.Deserialize<ModeloEtiquetaDesign>(json, ModeloEtiquetaDesign.JsonOpts)
               ?? new ModeloEtiquetaDesign { LarguraMm = modelo.LarguraMm, AlturaMm = modelo.AlturaMm };
    }

    public static Ajuste Ajustar(ModeloEtiquetaDesign modelo, double larguraMm, double alturaMm)
    {
        if (modelo.LarguraMm <= 0 || modelo.AlturaMm <= 0 || larguraMm <= 0 || alturaMm <= 0)
            return new Ajuste(modelo, 1, false, "Modelo sem medidas válidas.");

        var mesmaLargura = Math.Abs(modelo.LarguraMm - larguraMm) < 0.05;
        var mesmaAltura = Math.Abs(modelo.AlturaMm - alturaMm) < 0.05;
        if (mesmaLargura && mesmaAltura)
            return new Ajuste(modelo, 1, false, $"Modelo {modelo.LarguraMm:0.#} × {modelo.AlturaMm:0.#} mm.");

        var fator = Math.Min(larguraMm / modelo.LarguraMm, alturaMm / modelo.AlturaMm);
        var larguraDesenho = modelo.LarguraMm * fator;
        var alturaDesenho = modelo.AlturaMm * fator;
        var deslocamentoX = (larguraMm - larguraDesenho) / 2;
        var deslocamentoY = (alturaMm - alturaDesenho) / 2;

        var copia = Clonar(modelo);
        copia.Nome = $"{modelo.Nome} · ajustado para {larguraMm:0.#} × {alturaMm:0.#} mm";
        copia.LarguraMm = larguraMm;
        copia.AlturaMm = alturaMm;

        foreach (var el in copia.Elementos)
        {
            el.X = deslocamentoX + el.X * fator;
            el.Y = deslocamentoY + el.Y * fator;
            el.Largura *= fator;
            el.Altura *= fator;
            el.TamanhoFonte = Math.Max(1, el.TamanhoFonte * fator);
            el.EspessuraBorda = Math.Max(0.1, el.EspessuraBorda * fator);
            el.EspacamentoLetras *= fator;
            el.RaioCanto *= fator;
        }

        var descricao = fator < 1
            ? $"Modelo {modelo.LarguraMm:0.#} × {modelo.AlturaMm:0.#} mm reduzido para caber na mídia (escala {fator:P0}, centralizado)."
            : $"Modelo {modelo.LarguraMm:0.#} × {modelo.AlturaMm:0.#} mm ampliado para a mídia (escala {fator:P0}, centralizado).";

        return new Ajuste(copia, fator, true, descricao);
    }
}
