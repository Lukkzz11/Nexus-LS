namespace NexusLS.Core.Models;

public enum EstadoFila
{
    /// <summary>Esperando para ser enviado (novo, alterado ou reagendado após falha).</summary>
    Pendente = 1,

    /// <summary>Foi pego por um envio em curso. Se o programa cair aqui, a recuperação volta para Pendente.</summary>
    Enviando = 2,

    /// <summary>O servidor confirmou a versão registrada.</summary>
    Enviado = 3,

    /// <summary>O servidor recusou os dados (ou o produto é inválido). Só volta à fila quando o produto mudar.</summary>
    Falhou = 4
}

/// <summary>
/// Fila de saída (outbox): uma linha por produto que precisa ser enviado ao catálogo. É gravada na MESMA transação
/// que altera o produto, então não existe produto salvo sem aviso na fila. Não guarda cópia dos dados: na hora de
/// enviar, lê o produto atual (por isso várias mudanças seguidas viram um envio só e nunca vai dado velho).
/// </summary>
public class ItemFilaSincronizacao
{
    public int Id { get; set; }

    /// <summary>Id global do produto; é também o id do documento remoto (reenviar apenas sobrescreve, nunca duplica).</summary>
    public Guid ProdutoGuid { get; set; }

    /// <summary>Sobe a cada mudança do produto. Se mudou durante um envio, a confirmação não encerra o item.</summary>
    public int Versao { get; set; } = 1;

    public EstadoFila Estado { get; set; } = EstadoFila.Pendente;
    public int Tentativas { get; set; }

    /// <summary>Não tentar antes deste horário (espera crescente após falha de conexão).</summary>
    public DateTime? ProximaTentativa { get; set; }

    public string? UltimoErro { get; set; }

    /// <summary>Uid do Firebase de quem fez a última alteração (nulo se não havia sessão).</summary>
    public string? UsuarioUid { get; set; }

    public DateTime CriadoEm { get; set; }
    public DateTime AtualizadoEm { get; set; }
    public DateTime? EnviadoEm { get; set; }
}
