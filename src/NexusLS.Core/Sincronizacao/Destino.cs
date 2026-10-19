namespace NexusLS.Core.Sincronizacao;

/// <summary>O que fazer com um produto no destino. Desativar também grava o produto, só que como inativo (nada é apagado).</summary>
public enum TipoOperacao { Gravar, Desativar }

public record OperacaoCatalogo(ItemPlano Item, TipoOperacao Tipo);

/// <summary>Como a falha deve ser tratada pelo sincronizador.</summary>
public enum GravidadeFalha
{
    /// <summary>
    /// Rede, login, permissão, limite de uso ou servidor fora do ar: continuar não adianta. Para tudo; o que já foi
    /// confirmado fica anotado e a próxima sincronização continua de onde parou.
    /// </summary>
    Interrompe = 1,

    /// <summary>O servidor recusou o CONTEÚDO (dado inválido). O lote é refeito produto a produto para achar qual é o culpado.</summary>
    DadosRecusados = 2
}

/// <summary>Falha ao falar com o destino. A mensagem já vem em português, pronta para mostrar, e nunca contém senha nem token.</summary>
public class FalhaDestinoException : Exception
{
    public GravidadeFalha Gravidade { get; }

    public FalhaDestinoException(string mensagem, GravidadeFalha gravidade = GravidadeFalha.Interrompe, Exception? causa = null)
        : base(mensagem, causa) => Gravidade = gravidade;
}

/// <summary>
/// Para onde o catálogo é publicado. A regra de sincronização só conhece esta interface; o Firestore é uma
/// implementação (e nos testes entra um destino falso). Trocar de destino não mexe em nenhuma regra.
/// </summary>
public interface IDestinoCatalogo
{
    /// <summary>Confere, sem gravar nada, que dá para publicar: há conexão, o login vale e há permissão.</summary>
    Task VerificarAsync(CancellationToken ct = default);

    /// <summary>Envia um lote de produtos de uma vez: ou o servidor grava todos, ou nenhum.</summary>
    Task EnviarLoteAsync(IReadOnlyList<OperacaoCatalogo> lote, CancellationToken ct = default);

    /// <summary>Anota no destino quando o catálogo foi atualizado pela última vez (a vitrine mostra "atualizado em ...").</summary>
    Task RegistrarAtualizacaoAsync(DateTime agora, int totalProdutosAtivos, CancellationToken ct = default);
}
