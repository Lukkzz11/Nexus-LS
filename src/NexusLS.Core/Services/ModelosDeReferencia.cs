using System.Globalization;
using System.Text;

namespace NexusLS.Core.Services;

/// <summary>
/// Os primeiros dígitos da referência indicam o modelo da roupa (bermuda 14xx, calça 15xx, camisa 10xx...).
/// Daqui sai o prefixo de cada categoria, usado para sugerir a próxima referência livre.
/// </summary>
public static class ModelosDeReferencia
{
    /// <summary>Categorias conhecidas, na ordem em que aparecem na lista, com o prefixo da referência.</summary>
    public static readonly IReadOnlyList<(string Categoria, string Prefixo)> Todos = new[]
    {
        ("Bermuda", "14"), ("Calça", "15"), ("Camisa", "10"), ("Camisa botão", "11"),
        ("Camisa polo", "12"), ("Camiseta", "80"), ("Casaco", "16"), ("Short", "08"),
    };

    public static string? PrefixoDa(string? categoria)
    {
        var chave = Chave(categoria);
        foreach (var (nome, prefixo) in Todos) if (Chave(nome) == chave) return prefixo;
        return null;
    }

    /// <summary>Primeira referência quando a categoria ainda não tem nenhuma (ex.: bermuda → 1401).</summary>
    public static string PrimeiraDe(string prefixo) => prefixo + "01";

    /// <summary>Sem acento, sem maiúscula e sem espaço duplicado, para comparar nomes de categoria.</summary>
    public static string Chave(string? texto)
    {
        var t = (texto ?? "").Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in t)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
