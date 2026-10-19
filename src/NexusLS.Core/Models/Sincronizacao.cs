namespace NexusLS.Core.Models;

/// <summary>
/// O que já foi enviado ao Firestore para um produto: o "retrato" (hash) do que foi publicado.
/// Comparando o hash de hoje com este, sabe-se o que mudou sem reenviar tudo. Fica no banco local;
/// nada aqui é dado comercial novo, só o controle da sincronização.
/// </summary>
public class SincronizacaoProduto
{
    public int Id { get; set; }

    /// <summary>Identificador global do produto (<see cref="Produto.Guid"/>), o mesmo usado como id do documento remoto.</summary>
    public Guid ProdutoGuid { get; set; }

    /// <summary>Hash do conteúdo publicado na última vez que foi confirmado pelo servidor.</summary>
    public string HashEnviado { get; set; } = string.Empty;

    /// <summary>O produto está publicado como ativo? Falso = foi enviado como desativado (nada é apagado no remoto).</summary>
    public bool AtivoNoRemoto { get; set; } = true;

    /// <summary>Horário local do último envio confirmado.</summary>
    public DateTime EnviadoEm { get; set; }
}

/// <summary>Como terminou uma sincronização.</summary>
public enum ResultadoSincronizacao
{
    /// <summary>Tudo o que era preciso foi enviado.</summary>
    Sucesso = 1,

    /// <summary>Parte foi enviada, parte falhou (ou foi pulada por dados inválidos). A próxima continua de onde parou.</summary>
    Parcial = 2,

    /// <summary>Nada foi enviado (sem internet, licença inválida, erro do servidor...).</summary>
    Falhou = 3,

    /// <summary>Não havia nada a enviar.</summary>
    SemMudancas = 4
}

/// <summary>Um registro por vez que alguém clicou em "Atualizar Firestore": quando, quanto e como terminou.</summary>
public class SincronizacaoLog
{
    public int Id { get; set; }
    public DateTime IniciadaEm { get; set; }
    public DateTime? ConcluidaEm { get; set; }
    public string? Operador { get; set; }
    public int Criados { get; set; }
    public int Alterados { get; set; }
    public int Desativados { get; set; }
    public int Inalterados { get; set; }

    /// <summary>Produtos pulados por dados inválidos (nunca são enviados).</summary>
    public int Ignorados { get; set; }

    /// <summary>Operações de envio que falharam.</summary>
    public int Erros { get; set; }

    public ResultadoSincronizacao Resultado { get; set; }

    /// <summary>Texto para mostrar ao usuário: o resumo ou o motivo da falha.</summary>
    public string? Mensagem { get; set; }
}
