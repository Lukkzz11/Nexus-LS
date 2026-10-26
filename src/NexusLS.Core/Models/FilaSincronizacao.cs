namespace NexusLS.Core.Models;

public enum EstadoFila
{
    /// <summary>Esperando para ser enviado (novo, alterado ou reagendado após falha).</summary>
    Pendente = 1,

    /// <summary>Foi pego por um envio em curso. Se o programa cair aqui, a recuperação volta para Pendente.</summary>
    Enviando = 2,

    /// <summary>O servidor confirmou a versão registrada.</summary>
    Enviado = 3,

    /// <summary>O servidor recusou os dados (ou a entidade é inválida). Só volta à fila quando ela mudar.</summary>
    Falhou = 4
}

/// <summary>
/// Fila de saída (outbox): uma linha por entidade (produto, venda, cliente...) que precisa ser enviada. É gravada na MESMA
/// transação que altera a entidade, então não existe alteração salva sem aviso na fila. Não guarda cópia dos dados: na hora
/// de enviar, lê o registro atual (por isso várias mudanças seguidas viram um envio só e nunca vai dado velho).
/// </summary>
public class ItemFilaSincronizacao
{
    public int Id { get; set; }

    /// <summary>Qual tipo de entidade é (<see cref="NomesEntidade"/>). Linhas antigas, de antes da fila geral, são produtos.</summary>
    public string Entidade { get; set; } = NomesEntidade.Produto;

    /// <summary>Id global da entidade (<c>Guid</c> dela); é também o id do documento remoto (reenviar apenas sobrescreve, nunca duplica).</summary>
    public Guid EntidadeGuid { get; set; }

    /// <summary>Sobe a cada mudança da entidade. Se mudou durante um envio, a confirmação não encerra o item.</summary>
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
