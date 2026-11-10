import React, { useState } from "react";
import {
  Search,
  Wifi,
  WifiOff,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Monitor,
  Copy,
  ClipboardPaste,
  RefreshCw,
  Check,
} from "lucide-react";
import { Licenca } from "../../types";
import {
  ResultadoVerificacaoChave,
  testarConexaoLicenca,
  verificarChaveLicenca,
} from "../../services/licenseService";

interface VerifyKeyViewProps {
  licencas: Licenca[];
}

type SituacaoVerificacao = ResultadoVerificacaoChave["situacao"];

function formatarData(data?: string): string {
  if (!data) return "—";
  const parsed = new Date(`${data}T00:00:00`);
  return Number.isNaN(parsed.getTime()) ? data : parsed.toLocaleDateString("pt-BR");
}

function descreverDias(dias?: number): string {
  if (dias === undefined || dias === null) return "—";
  if (dias < 0) return `Vencida há ${Math.abs(dias)} dia(s)`;
  if (dias === 0) return "Vence hoje";
  return `${dias} dia(s) restante(s)`;
}

function corDias(dias?: number): string {
  if (dias === undefined || dias === null) return "text-slate-800";
  if (dias < 0) return "text-red-600";
  if (dias <= 15) return "text-amber-600";
  return "text-slate-800";
}

function configurarBanner(situacao: SituacaoVerificacao) {
  switch (situacao) {
    case "ATIVA":
      return {
        icon: CheckCircle2,
        container: "bg-emerald-50/80 border-emerald-200",
        icone: "text-emerald-600",
        titulo: "text-emerald-900",
        rotulo: "Licença ativa",
      };
    case "VENCENDO":
      return {
        icon: AlertTriangle,
        container: "bg-amber-50/80 border-amber-200",
        icone: "text-amber-600",
        titulo: "text-amber-900",
        rotulo: "Licença a vencer",
      };
    case "PENDENTE":
      return {
        icon: AlertTriangle,
        container: "bg-amber-50/80 border-amber-200",
        icone: "text-amber-600",
        titulo: "text-amber-900",
        rotulo: "Licença pendente",
      };
    case "VENCIDA":
      return {
        icon: XCircle,
        container: "bg-red-50/80 border-red-200",
        icone: "text-red-600",
        titulo: "text-red-900",
        rotulo: "Licença vencida",
      };
    case "BLOQUEADA":
      return {
        icon: XCircle,
        container: "bg-red-50/80 border-red-200",
        icone: "text-red-600",
        titulo: "text-red-900",
        rotulo: "Licença bloqueada",
      };
    case "CANCELADA":
      return {
        icon: XCircle,
        container: "bg-red-50/80 border-red-200",
        icone: "text-red-600",
        titulo: "text-red-900",
        rotulo: "Licença cancelada",
      };
    case "ERRO":
      return {
        icon: WifiOff,
        container: "bg-red-50/80 border-red-200",
        icone: "text-red-600",
        titulo: "text-red-900",
        rotulo: "Falha de conexão",
      };
    default:
      return {
        icon: AlertTriangle,
        container: "bg-slate-50 border-slate-200",
        icone: "text-slate-500",
        titulo: "text-slate-800",
        rotulo: "Chave não encontrada",
      };
  }
}

export const VerifyKeyView: React.FC<VerifyKeyViewProps> = ({ licencas }) => {
  const [chave, setChave] = useState("");
  const [verificando, setVerificando] = useState(false);
  const [testandoConexao, setTestandoConexao] = useState(false);
  const [resultado, setResultado] = useState<ResultadoVerificacaoChave | null>(null);
  const [conexao, setConexao] = useState<{ online: boolean; latenciaMs: number } | null>(null);
  const [mensagemConexao, setMensagemConexao] = useState<string | null>(null);
  const [avisoColar, setAvisoColar] = useState<string | null>(null);
  const [copiado, setCopiado] = useState(false);

  const handleVerificar = async (e?: React.FormEvent) => {
    e?.preventDefault();
    const chaveLimpa = chave.trim();
    if (!chaveLimpa || verificando) return;

    setVerificando(true);
    setResultado(null);
    setAvisoColar(null);

    try {
      const res = await verificarChaveLicenca(chaveLimpa);
      setResultado(res);
      setConexao({ online: res.conectado, latenciaMs: res.latenciaMs });
      setMensagemConexao(
        res.conectado
          ? "Servidor de licenças online."
          : "Sem conexão com o servidor de licenças."
      );
    } catch (err: any) {
      setResultado({
        conectado: false,
        latenciaMs: 0,
        encontrada: false,
        situacao: "ERRO",
        chave: chaveLimpa.toUpperCase(),
        maquinas: [],
        mensagem:
          "Sem conexão com o servidor de licenças. Verifique a internet e tente novamente.",
      });
      setConexao({ online: false, latenciaMs: 0 });
      setMensagemConexao("Sem conexão com o servidor de licenças.");
      console.error("Erro ao verificar chave:", err);
    } finally {
      setVerificando(false);
    }
  };

  const handleTestarConexao = async () => {
    if (testandoConexao) return;
    setTestandoConexao(true);
    setAvisoColar(null);

    try {
      const res = await testarConexaoLicenca();
      setConexao({ online: res.online, latenciaMs: res.latenciaMs });
      setMensagemConexao(res.mensagem);
    } catch (err) {
      setConexao({ online: false, latenciaMs: 0 });
      setMensagemConexao("Sem conexão com o servidor de licenças.");
      console.error("Erro no teste de conexão:", err);
    } finally {
      setTestandoConexao(false);
    }
  };

  const handleColar = async () => {
    setAvisoColar(null);
    try {
      if (navigator.clipboard && typeof navigator.clipboard.readText === "function") {
        const texto = await navigator.clipboard.readText();
        if (texto && texto.trim()) {
          setChave(texto.trim().toUpperCase());
          setResultado(null);
        }
        return;
      }
      setAvisoColar("Leitura da área de transferência não disponível. Cole com Ctrl+V.");
    } catch {
      setAvisoColar("Não foi possível ler a área de transferência. Cole com Ctrl+V.");
    }
  };

  const handleCopiarChave = () => {
    const valor = resultado?.chave || chave;
    if (!valor) return;
    navigator.clipboard.writeText(valor);
    setCopiado(true);
    setTimeout(() => setCopiado(false), 2000);
  };

  const handleLimpar = () => {
    setChave("");
    setResultado(null);
    setConexao(null);
    setMensagemConexao(null);
    setAvisoColar(null);
    setCopiado(false);
  };

  const banner = resultado ? configurarBanner(resultado.situacao) : null;
  const BannerIcon = banner?.icon;

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* Cabeçalho explicativo */}
      <div className="bg-white rounded-xl p-5 sm:p-6 border border-slate-200 shadow-2xs">
        <div className="flex items-start gap-3">
          <div className="p-2 rounded-lg bg-blue-50 text-blue-600 mt-0.5">
            <Search className="w-5 h-5" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-slate-900">Verificar Chave de Licença</h3>
            <p className="text-xs text-slate-500 mt-1 max-w-2xl leading-relaxed">
              Consulta somente leitura: nenhuma máquina é vinculada e nenhum dado é
              alterado. O resultado reflete exatamente o que está gravado no servidor.
            </p>
          </div>
        </div>
      </div>

      {/* Formulário de consulta */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
        <div className="px-5 sm:px-6 py-4 border-b border-slate-100 flex items-center justify-between gap-2">
          <h4 className="text-xs font-bold uppercase tracking-wider text-slate-700">
            Chave da Licença
          </h4>

          {conexao ? (
            conexao.online ? (
              <span
                className="inline-flex items-center gap-1.5 text-[10px] font-semibold text-emerald-700 bg-emerald-50 border border-emerald-200 px-2 py-1 rounded-full shrink-0"
                title={mensagemConexao || undefined}
              >
                <Wifi className="w-3 h-3" />
                Conectado • {conexao.latenciaMs} ms
              </span>
            ) : (
              <span
                className="inline-flex items-center gap-1.5 text-[10px] font-semibold text-red-700 bg-red-50 border border-red-200 px-2 py-1 rounded-full shrink-0"
                title={mensagemConexao || undefined}
              >
                <WifiOff className="w-3 h-3" />
                Sem conexão
              </span>
            )
          ) : (
            <span className="inline-flex items-center gap-1.5 text-[10px] font-semibold text-slate-500 bg-slate-50 border border-slate-200 px-2 py-1 rounded-full shrink-0">
              <Wifi className="w-3 h-3" />
              Conexão não testada
            </span>
          )}
        </div>

        <form onSubmit={handleVerificar} className="p-5 sm:p-6 space-y-4">
          <div>
            <label className="block text-xs font-semibold text-slate-700 mb-1">
              Chave de Ativação
            </label>
            <div className="flex flex-col sm:flex-row gap-2">
              <div className="relative flex-1">
                <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
                <input
                  type="text"
                  value={chave}
                  onChange={(e) => setChave(e.target.value.toUpperCase())}
                  placeholder="Ex: NEXUS-8K29-XP41-MZ73"
                  autoComplete="off"
                  spellCheck={false}
                  className="w-full font-mono uppercase tracking-wide text-xs pl-9 pr-3 py-2.5 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>
              <button
                type="button"
                onClick={handleColar}
                className="flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 rounded-lg shadow-2xs transition shrink-0"
                title="Colar da área de transferência"
              >
                <ClipboardPaste className="w-4 h-4 text-slate-500" />
                <span>Colar</span>
              </button>
            </div>
            {avisoColar && (
              <p className="text-[11px] text-amber-700 mt-1.5">{avisoColar}</p>
            )}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            <button
              type="submit"
              disabled={verificando || !chave.trim()}
              className="flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition disabled:opacity-50"
            >
              {verificando ? (
                <RefreshCw className="w-4 h-4 animate-spin" />
              ) : (
                <Search className="w-4 h-4 stroke-[2.5]" />
              )}
              <span>{verificando ? "Verificando..." : "Conectar e verificar"}</span>
            </button>

            <button
              type="button"
              onClick={handleTestarConexao}
              disabled={testandoConexao}
              className="flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 rounded-lg shadow-2xs transition disabled:opacity-50"
            >
              {testandoConexao ? (
                <RefreshCw className="w-4 h-4 animate-spin" />
              ) : (
                <Wifi className="w-4 h-4 text-emerald-600" />
              )}
              <span>{testandoConexao ? "Testando..." : "Testar conexão"}</span>
            </button>

            {(resultado || chave || conexao) && (
              <button
                type="button"
                onClick={handleLimpar}
                className="flex items-center justify-center gap-2 px-3 py-2.5 text-xs font-medium text-slate-500 hover:text-slate-800 hover:bg-slate-50 rounded-lg transition"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                <span>Limpar</span>
              </button>
            )}
          </div>

          {licencas.length > 0 && (
            <div className="border-t border-slate-100 pt-3">
              <p className="text-[10px] font-bold uppercase tracking-wider text-slate-400 mb-2">
                Chaves cadastradas — clique para preencher
              </p>
              <div className="flex flex-wrap gap-1.5 max-h-28 overflow-y-auto">
                {licencas.slice(0, 8).map((lic) => (
                  <button
                    key={lic.id}
                    type="button"
                    onClick={() => {
                      setChave(lic.codigoFormatado);
                      setResultado(null);
                      setAvisoColar(null);
                    }}
                    className={`text-[10px] px-2 py-1 rounded border transition text-left ${
                      chave === lic.codigoFormatado
                        ? "bg-blue-50 border-blue-600 text-blue-700 font-bold"
                        : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
                    }`}
                  >
                    <span className="font-mono">{lic.codigoFormatado}</span>
                    <span className="text-slate-400 ml-1">· {lic.clienteNome}</span>
                  </button>
                ))}
              </div>
            </div>
          )}
        </form>
      </div>

      {/* Resultado da verificação */}
      {!resultado ? (
        <div className="bg-white rounded-xl border border-slate-200 shadow-2xs p-8 text-center">
          <Search className="w-8 h-8 text-slate-300 mx-auto mb-2" />
          <p className="text-xs text-slate-400 max-w-md mx-auto leading-relaxed">
            Informe uma chave e clique em "Conectar e verificar" para consultar a
            situação da licença. Esta operação não ativa nem vincula computadores.
          </p>
        </div>
      ) : (
        <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
          <div className="px-5 sm:px-6 py-4 border-b border-slate-100 flex items-center justify-between gap-2">
            <h4 className="text-xs font-bold uppercase tracking-wider text-slate-700">
              Resultado da Consulta
            </h4>
            <span className="text-[10px] font-mono text-slate-400 bg-slate-100 px-2 py-0.5 rounded">
              {resultado.latenciaMs} ms
            </span>
          </div>

          <div className="p-5 sm:p-6 space-y-4">
            {/* Banner de status */}
            <div
              className={`p-4 rounded-xl border flex items-start gap-3 ${banner!.container}`}
            >
              {BannerIcon && (
                <BannerIcon className={`w-5 h-5 shrink-0 mt-0.5 ${banner!.icone}`} />
              )}
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <span
                    className={`text-xs font-bold uppercase tracking-wider ${banner!.titulo}`}
                  >
                    {banner!.rotulo}
                  </span>
                  <span className="text-[10px] font-mono font-bold px-2 py-0.5 rounded-full bg-white/80 border border-slate-200 text-slate-700">
                    {resultado.situacao}
                  </span>
                  {resultado.chave && (
                    <button
                      type="button"
                      onClick={handleCopiarChave}
                      className="inline-flex items-center gap-1 text-[10px] font-mono text-slate-600 bg-white/80 border border-slate-200 px-2 py-0.5 rounded-full hover:bg-white transition"
                      title="Copiar chave consultada"
                    >
                      {copiado ? (
                        <Check className="w-3 h-3 text-emerald-600" />
                      ) : (
                        <Copy className="w-3 h-3" />
                      )}
                      <span>{resultado.chave}</span>
                    </button>
                  )}
                </div>
                <p className="text-xs mt-1 leading-relaxed text-slate-700">
                  {resultado.mensagem}
                </p>
                {resultado.motivoBloqueio && (
                  <p className="text-[11px] mt-1.5 text-red-700">
                    Motivo do bloqueio: {resultado.motivoBloqueio}
                  </p>
                )}
              </div>
            </div>

            {resultado.encontrada && (
              <>
                {/* Grade de detalhes */}
                <div className="grid grid-cols-2 md:grid-cols-4 gap-2.5">
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Cliente
                    </span>
                    <span className="text-xs font-semibold text-slate-800 break-words">
                      {resultado.cliente || "—"}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Empresa
                    </span>
                    <span className="text-xs font-semibold text-slate-800 break-words">
                      {resultado.empresa || "—"}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Plano
                    </span>
                    <span className="text-xs font-semibold text-slate-800 capitalize">
                      {resultado.plano || "—"}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Início
                    </span>
                    <span className="text-xs font-semibold text-slate-800">
                      {formatarData(resultado.dataInicio)}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Vencimento
                    </span>
                    <span className="text-xs font-semibold text-slate-800">
                      {formatarData(resultado.dataVencimento)}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Dias restantes
                    </span>
                    <span
                      className={`text-xs font-semibold ${corDias(resultado.diasRestantes)}`}
                    >
                      {descreverDias(resultado.diasRestantes)}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Dispositivos
                    </span>
                    <span className="text-xs font-semibold text-blue-600 font-mono">
                      {resultado.dispositivosAtivos ?? 0}/
                      {resultado.limiteDispositivos ?? 0}
                    </span>
                  </div>
                  <div className="bg-slate-50 border border-slate-200/80 rounded-lg p-3">
                    <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                      Conexão
                    </span>
                    <span
                      className={`text-xs font-semibold ${
                        resultado.conectado ? "text-emerald-600" : "text-red-600"
                      }`}
                    >
                      {resultado.conectado ? "Online" : "Offline"}
                    </span>
                  </div>
                </div>

                {/* HWIDs / terminais vinculados */}
                <div className="border-t border-slate-100 pt-4">
                  <div className="flex items-center justify-between mb-2">
                    <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400">
                      Computadores vinculados
                    </span>
                    <span className="text-[10px] font-mono text-slate-500">
                      {resultado.maquinas.length} de {resultado.limiteDispositivos ?? 0}
                    </span>
                  </div>

                  {resultado.maquinas.length === 0 ? (
                    <div className="flex items-center gap-2 text-xs text-slate-400 bg-slate-50 border border-dashed border-slate-200 rounded-lg p-3">
                      <Monitor className="w-4 h-4 text-slate-400" />
                      <span>Nenhum computador vinculado ainda.</span>
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-1.5">
                      {resultado.maquinas.map((maquina) => (
                        <span
                          key={maquina}
                          className="inline-flex items-center gap-1.5 font-mono text-[10px] bg-slate-100 border border-slate-200 text-slate-700 px-2 py-1 rounded"
                        >
                          <Monitor className="w-3 h-3 text-slate-500" />
                          {maquina}
                        </span>
                      ))}
                    </div>
                  )}
                </div>

                {/* Observações */}
                {resultado.observacoes && resultado.observacoes.trim() && (
                  <div className="bg-slate-50 border border-slate-200 rounded-lg p-3">
                    <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 block mb-1">
                      Observações
                    </span>
                    <p className="text-xs text-slate-600 leading-relaxed whitespace-pre-line">
                      {resultado.observacoes}
                    </p>
                  </div>
                )}
              </>
            )}
          </div>

          <div className="px-5 sm:px-6 py-3 bg-slate-50 border-t border-slate-100 text-[11px] text-slate-400">
            Consulta somente leitura — nenhuma ativação, vínculo de máquina ou gravação é
            realizada por esta tela.
          </div>
        </div>
      )}
    </div>
  );
};

export default VerifyKeyView;
