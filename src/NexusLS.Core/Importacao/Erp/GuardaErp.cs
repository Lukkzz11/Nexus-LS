using NexusLS.Core.Services;

namespace NexusLS.Core.Importacao.Erp;

/// <summary>
/// Trava de segurança do Firebase do ERP PDV Life (projeto de produção da loja). O Nexus só LÊ esse projeto
/// (<see cref="FirestoreErpFonte"/>). Os destinos de gravação do Nexus (catálogo online, nuvem operacional, vitrine e
/// fotos) se recusam a apontar para ele, para que uma configuração errada nunca grave nada na base real da loja.
/// A gravação no formato do ERP está especificada em docs/FIREBASE_MESMO_BANCO_DO_ERP.md e fica desligada
/// (<see cref="ConfiguracaoErp.GravacaoAtivada"/> = falso por padrão).
/// </summary>
public static class GuardaErp
{
    /// <summary>Projeto Firebase de produção do ERP PDV Life.</summary>
    public const string ProjetoDoErp = "pdv---lifesurf";

    public static bool EhProjetoDoErp(string? projectId) =>
        string.Equals((projectId ?? "").Trim(), ProjetoDoErp, StringComparison.OrdinalIgnoreCase);

    /// <summary>O bucket padrão do Storage do ERP começa pelo id do projeto (<c>pdv---lifesurf.firebasestorage.app</c>).</summary>
    public static bool EhBucketDoErp(string? bucket)
    {
        var b = (bucket ?? "").Trim();
        return b.StartsWith(ProjetoDoErp + ".", StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(ProjetoDoErp + "/", StringComparison.OrdinalIgnoreCase)
            || string.Equals(b, ProjetoDoErp, StringComparison.OrdinalIgnoreCase);
    }

    public const string MensagemSomenteLeitura =
        "Este é o banco do ERP PDV Life (somente leitura no Nexus). Para importar do ERP use a seção \"ERP PDV Life\"; " +
        "o catálogo online e a nuvem operacional precisam de outro projeto Firebase.";

    /// <summary>Falha (regra de negócio) se o projeto for o do ERP. Usada ao salvar configurações.</summary>
    public static void ExigirNaoSerOErp(string? projectId)
    {
        if (EhProjetoDoErp(projectId)) throw new RegraDeNegocioException(MensagemSomenteLeitura);
    }
}
