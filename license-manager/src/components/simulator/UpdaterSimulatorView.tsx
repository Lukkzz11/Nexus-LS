import React, { useState } from "react";
import {
  Terminal,
  Play,
  CheckCircle2,
  XCircle,
  AlertTriangle,
  Monitor,
  Copy,
  Check,
  RotateCcw,
} from "lucide-react";
import { Licenca, RespostaValidacaoAtualizador } from "../../types";
import { simularValidacaoAtualizador } from "../../services/licenseService";

interface UpdaterSimulatorViewProps {
  licencas: Licenca[];
  onRefreshLicencas: () => void;
}

export const UpdaterSimulatorView: React.FC<UpdaterSimulatorViewProps> = ({
  licencas,
  onRefreshLicencas,
}) => {
  const [chaveSelecionada, setChaveSelecionada] = useState("");
  const [dispositivoId, setDispositivoId] = useState("PC-BALCAO-TESTE-01");
  const [nomeDispositivo, setNomeDispositivo] = useState("Computador Frente de Caixa");
  const [testando, setTestando] = useState(false);
  const [resposta, setResposta] = useState<RespostaValidacaoAtualizador | null>(null);
  const [copiadoJson, setCopiadoJson] = useState(false);

  const handleTestar = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!chaveSelecionada.trim() || !dispositivoId.trim()) return;

    setTestando(true);
    setResposta(null);
    try {
      const res = await simularValidacaoAtualizador({
        chave: chaveSelecionada.trim(),
        dispositivoId: dispositivoId.trim(),
        nomeDispositivo: nomeDispositivo.trim(),
        sistema: "NexusLS",
      });
      setResposta(res);
      onRefreshLicencas();
    } catch (err: any) {
      setResposta({
        autorizado: false,
        situacao: "ERRO",
        mensagem: err.message || "Erro de conexão ao simular validação.",
      });
    } finally {
      setTestando(false);
    }
  };

  const handleCopiarJson = () => {
    if (!resposta) return;
    navigator.clipboard.writeText(JSON.stringify(resposta, null, 2));
    setCopiadoJson(true);
    setTimeout(() => setCopiadoJson(false), 2000);
  };

  return (
    <div className="space-y-6">
      {/* Intro Box */}
      <div className="bg-white rounded-xl p-6 border border-slate-200 shadow-2xs">
        <div className="flex items-start gap-3">
          <div className="p-2 rounded-lg bg-blue-50 text-blue-600 mt-0.5">
            <Terminal className="w-5 h-5" />
          </div>
          <div>
            <h3 className="text-sm font-bold text-slate-900">
              Simulador do Atualizador Nexus LS & Verificador de Licença
            </h3>
            <p className="text-xs text-slate-500 mt-1 max-w-2xl leading-relaxed">
              Esta ferramenta executa a mesma chamada de validação e ativação que o Atualizador em C# faz ao inicializar. Teste a validação de chave, limite de máquinas simultâneas, verificação de datas de validade calculadas no servidor e bloqueio imediato.
            </p>
          </div>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
        {/* Formulário de Envio */}
        <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
          <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
            <h4 className="text-xs font-bold uppercase tracking-wider text-slate-700">
              Parâmetros de Entrada da Requisição
            </h4>
            <span className="text-[10px] font-mono text-slate-400 bg-slate-100 px-2 py-0.5 rounded">
              POST /validateLicense
            </span>
          </div>

          <form onSubmit={handleTestar} className="p-6 space-y-4">
            <div>
              <div className="flex items-center justify-between mb-1">
                <label className="text-xs font-semibold text-slate-700">
                  Chave da Licença *
                </label>
                {licencas.length > 0 && (
                  <span className="text-[11px] text-slate-400">Ou escolha da lista abaixo</span>
                )}
              </div>
              <input
                type="text"
                required
                value={chaveSelecionada}
                onChange={(e) => setChaveSelecionada(e.target.value)}
                placeholder="Ex: NEXUS-8K29-XP41-MZ73"
                className="w-full font-mono text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
              />

              {licencas.length > 0 && (
                <div className="mt-2 flex flex-wrap gap-1.5 max-h-24 overflow-y-auto">
                  {licencas.slice(0, 6).map((l) => (
                    <button
                      key={l.id}
                      type="button"
                      onClick={() => setChaveSelecionada(l.codigoFormatado)}
                      className={`text-[10px] font-mono px-2 py-0.5 rounded border transition ${
                        chaveSelecionada === l.codigoFormatado
                          ? "bg-blue-50 border-blue-600 text-blue-700 font-bold"
                          : "bg-slate-50 border-slate-200 text-slate-600 hover:bg-slate-100"
                      }`}
                    >
                      {l.codigoFormatado} ({l.clienteNome})
                    </button>
                  ))}
                </div>
              )}
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  ID do Dispositivo (Hardware Fingerprint) *
                </label>
                <input
                  type="text"
                  required
                  value={dispositivoId}
                  onChange={(e) => setDispositivoId(e.target.value)}
                  placeholder="Ex: PC-CAIXA-01"
                  className="w-full font-mono text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Nome Amigável do Dispositivo
                </label>
                <input
                  type="text"
                  value={nomeDispositivo}
                  onChange={(e) => setNomeDispositivo(e.target.value)}
                  placeholder="Ex: Caixa 01 Loja Centro"
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>
            </div>

            <div className="pt-2">
              <button
                type="submit"
                disabled={testando}
                className="w-full flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition disabled:opacity-50"
              >
                <Play className="w-3.5 h-3.5 fill-current" />
                <span>{testando ? "Executando Validação..." : "Simular Validação"}</span>
              </button>
            </div>
          </form>
        </div>

        {/* Resposta Recebida */}
        <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden flex flex-col justify-between">
          <div>
            <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
              <h4 className="text-xs font-bold uppercase tracking-wider text-slate-700">
                Resposta do Servidor (Payload Retornado)
              </h4>
              {resposta && (
                <button
                  onClick={handleCopiarJson}
                  className="text-xs text-slate-500 hover:text-slate-800 flex items-center gap-1 font-medium"
                >
                  {copiadoJson ? <Check className="w-3.5 h-3.5 text-emerald-600" /> : <Copy className="w-3.5 h-3.5" />}
                  <span>{copiadoJson ? "Copiado" : "Copiar JSON"}</span>
                </button>
              )}
            </div>

            <div className="p-6">
              {!resposta ? (
                <div className="text-center py-12 text-slate-400">
                  <Terminal className="w-8 h-8 text-slate-300 mx-auto mb-2" />
                  <p className="text-xs">Clique em "Simular Validação" para testar a resposta do servidor.</p>
                </div>
              ) : (
                <div className="space-y-4">
                  {/* Status Banner */}
                  <div
                    className={`p-4 rounded-xl border flex items-start gap-3 ${
                      resposta.autorizado
                        ? "bg-emerald-50/70 border-emerald-200 text-emerald-900"
                        : "bg-red-50/70 border-red-200 text-red-900"
                    }`}
                  >
                    {resposta.autorizado ? (
                      <CheckCircle2 className="w-5 h-5 text-emerald-600 shrink-0 mt-0.5" />
                    ) : (
                      <XCircle className="w-5 h-5 text-red-600 shrink-0 mt-0.5" />
                    )}
                    <div>
                      <div className="flex items-center gap-2">
                        <span className="text-xs font-bold uppercase tracking-wider">
                          {resposta.autorizado ? "Autorização Concedida" : "Autorização Negada"}
                        </span>
                        <span className="text-[10px] font-mono px-2 py-0.2 rounded-full bg-white font-bold border">
                          {resposta.situacao}
                        </span>
                      </div>
                      <p className="text-xs mt-1 leading-relaxed">{resposta.mensagem}</p>
                    </div>
                  </div>

                  {/* Resposta em JSON estruturado */}
                  <div>
                    <span className="text-[10px] font-bold uppercase tracking-wider text-slate-400 block mb-1">
                      JSON Retornado ao Atualizador
                    </span>
                    <pre className="p-3 bg-slate-900 text-emerald-400 rounded-lg text-[11px] font-mono overflow-x-auto max-h-56">
                      {JSON.stringify(resposta, null, 2)}
                    </pre>
                  </div>
                </div>
              )}
            </div>
          </div>

          <div className="p-4 bg-slate-50 border-t border-slate-100 text-[11px] text-slate-400">
            * O Atualizador do Nexus LS utiliza este JSON para liberar a inicialização do software local.
          </div>
        </div>
      </div>
    </div>
  );
};
