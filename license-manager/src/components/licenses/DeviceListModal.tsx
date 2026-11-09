import React, { useState, useEffect } from "react";
import { X, Monitor, Trash2, Plus, ClipboardPaste, Check } from "lucide-react";
import { Licenca, DispositivoVinculado } from "../../types";
import {
  listarDispositivosPorLicenca,
  desvincularDispositivo,
  vincularDispositivoManual,
} from "../../services/licenseService";

interface DeviceListModalProps {
  isOpen: boolean;
  onClose: () => void;
  licenca: Licenca | null;
  onDevicesUpdated: () => void;
}

export const DeviceListModal: React.FC<DeviceListModalProps> = ({
  isOpen,
  onClose,
  licenca,
  onDevicesUpdated,
}) => {
  const [dispositivos, setDispositivos] = useState<DispositivoVinculado[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [removendoId, setRemovendoId] = useState<string | null>(null);

  // Ativação manual pelo celular/painel
  const [novoHwid, setNovoHwid] = useState("");
  const [novoNome, setNovoNome] = useState("");
  const [vinculando, setVinculando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  const carregar = async () => {
    if (!licenca) return;
    setCarregando(true);
    try {
      const lista = await listarDispositivosPorLicenca(licenca.id);
      setDispositivos(lista);
    } catch (err) {
      console.error("Erro ao listar dispositivos:", err);
    } finally {
      setCarregando(false);
    }
  };

  useEffect(() => {
    if (isOpen && licenca) {
      setErro(null);
      setNovoHwid("");
      setNovoNome("");
      carregar();
    }
  }, [isOpen, licenca]);

  if (!isOpen || !licenca) return null;

  const handleRemover = async (disp: DispositivoVinculado) => {
    if (
      !window.confirm(
        `Tem certeza que deseja desvincular o computador "${disp.nomeDispositivo}" (${disp.identificadorDispositivo})? Esta ação liberará 1 vaga na licença.`
      )
    ) {
      return;
    }

    setRemovendoId(disp.id);
    try {
      await desvincularDispositivo(disp.id, licenca.id);
      await carregar();
      onDevicesUpdated();
    } catch (err) {
      alert("Falha ao desvincular dispositivo.");
    } finally {
      setRemovendoId(null);
    }
  };

  const handleVincularManual = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!novoHwid.trim()) {
      setErro("Digite ou cole o Código do Computador (HWID).");
      return;
    }
    setVinculando(true);
    setErro(null);
    try {
      await vincularDispositivoManual(licenca.id, novoHwid, novoNome);
      setNovoHwid("");
      setNovoNome("");
      await carregar();
      onDevicesUpdated();
    } catch (err: any) {
      setErro(err.message || "Não foi possível vincular o computador.");
    } finally {
      setVinculando(false);
    }
  };

  const handleColarHwid = async () => {
    try {
      const txt = await navigator.clipboard.readText();
      if (txt) setNovoHwid(txt.trim().toUpperCase());
    } catch {
      // sem permissão de clipboard
    }
  };

  const vagasEsgotadas = dispositivos.length >= licenca.limiteDispositivos;

  return (
    <div className="fixed inset-0 z-50 flex items-end sm:items-center justify-center p-0 sm:p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div className="relative w-full max-w-xl bg-white rounded-t-2xl sm:rounded-xl shadow-xl border border-slate-200 overflow-hidden max-h-[90vh] flex flex-col">
        {/* Header */}
        <div className="flex items-center justify-between px-4 sm:px-6 py-4 border-b border-slate-100 shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-blue-50 text-blue-600">
              <Monitor className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-sm sm:text-base font-bold text-slate-900">
                Computadores da Licença
              </h3>
              <p className="text-xs text-slate-400">
                {licenca.clienteNome} ({licenca.codigoFormatado})
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-slate-400 hover:text-slate-600 rounded-lg p-1.5 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-4 sm:p-6 space-y-4 overflow-y-auto flex-1">
          {/* Header Status */}
          <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg flex items-center justify-between text-xs">
            <div>
              <span className="text-slate-500">Capacidade Contratada:</span>
              <p className="font-bold text-slate-900 mt-0.5">
                {dispositivos.length} de {licenca.limiteDispositivos} computador(es) em uso
              </p>
            </div>
            <span
              className={`text-xs font-semibold px-2.5 py-1 rounded-full ${
                vagasEsgotadas
                  ? "bg-amber-100 text-amber-800"
                  : "bg-emerald-100 text-emerald-800"
              }`}
            >
              {vagasEsgotadas ? "Limite Atingido" : "Vagas Livres"}
            </span>
          </div>

          {/* Ativação Manual de Computador pelo Celular / Painel */}
          {!vagasEsgotadas && (
            <form
              onSubmit={handleVincularManual}
              className="p-3.5 bg-blue-50/50 border border-blue-100 rounded-xl space-y-2.5"
            >
              <p className="text-xs font-bold text-slate-800 flex items-center gap-1.5">
                <Plus className="w-4 h-4 text-blue-600" />
                <span>Ativar / Vincular Máquina Manualmente</span>
              </p>

              {erro && (
                <div className="p-2 text-xs text-red-700 bg-red-50 border border-red-200 rounded-lg">
                  {erro}
                </div>
              )}

              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                <div className="flex items-center gap-1.5">
                  <input
                    type="text"
                    value={novoHwid}
                    onChange={(e) => setNovoHwid(e.target.value.toUpperCase())}
                    placeholder="Código HWID (ex: A1B2C3D4E5F6)"
                    className="w-full font-mono text-xs px-3 py-2 bg-white border border-slate-200 rounded-lg focus:outline-none focus:border-blue-600 uppercase"
                  />
                  <button
                    type="button"
                    onClick={handleColarHwid}
                    className="p-2 bg-white hover:bg-slate-50 border border-slate-200 rounded-lg text-slate-600 shrink-0"
                    title="Colar HWID"
                  >
                    <ClipboardPaste className="w-4 h-4" />
                  </button>
                </div>

                <div className="flex items-center gap-1.5">
                  <input
                    type="text"
                    value={novoNome}
                    onChange={(e) => setNovoNome(e.target.value)}
                    placeholder="Nome (ex: Caixa 01)"
                    className="w-full text-xs px-3 py-2 bg-white border border-slate-200 rounded-lg focus:outline-none focus:border-blue-600"
                  />
                  <button
                    type="submit"
                    disabled={vinculando}
                    className="px-3 py-2 bg-blue-600 hover:bg-blue-700 text-white text-xs font-semibold rounded-lg transition flex items-center gap-1 shrink-0 disabled:opacity-50"
                  >
                    <Check className="w-3.5 h-3.5" />
                    <span>{vinculando ? "..." : "Ativar"}</span>
                  </button>
                </div>
              </div>
            </form>
          )}

          {carregando ? (
            <p className="text-xs text-center py-8 text-slate-400">Carregando dispositivos...</p>
          ) : dispositivos.length === 0 ? (
            <div className="text-center py-8 border border-dashed border-slate-200 rounded-lg">
              <Monitor className="w-8 h-8 text-slate-300 mx-auto mb-2" />
              <p className="text-xs font-medium text-slate-600">
                Nenhum computador vinculado ainda.
              </p>
              <p className="text-[11px] text-slate-400 mt-1 max-w-sm mx-auto px-2">
                Você pode vincular o HWID acima pelo celular ou deixar que o cliente ative a chave no Atualizador do PC.
              </p>
            </div>
          ) : (
            <div className="space-y-2.5">
              {dispositivos.map((disp) => {
                const primeiraData = disp.primeiraAtivacao?.toDate
                  ? disp.primeiraAtivacao.toDate().toLocaleString("pt-BR")
                  : "—";
                const ultimaData = disp.ultimaVerificacao?.toDate
                  ? disp.ultimaVerificacao.toDate().toLocaleString("pt-BR")
                  : "—";

                return (
                  <div
                    key={disp.id}
                    className="p-3.5 rounded-lg border border-slate-200 bg-white hover:border-slate-300 transition flex items-center justify-between gap-3 text-xs"
                  >
                    <div className="min-w-0">
                      <div className="flex flex-wrap items-center gap-2">
                        <Monitor className="w-4 h-4 text-slate-600 shrink-0" />
                        <span className="font-bold text-slate-900 truncate">
                          {disp.nomeDispositivo}
                        </span>
                        <span className="text-[10px] font-mono bg-slate-100 px-1.5 py-0.5 rounded text-slate-600">
                          {disp.identificadorDispositivo}
                        </span>
                      </div>
                      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-slate-400 mt-1.5">
                        <span>Ativado: {primeiraData}</span>
                        <span>Última checagem: {ultimaData}</span>
                      </div>
                    </div>

                    <button
                      onClick={() => handleRemover(disp)}
                      disabled={removendoId === disp.id}
                      className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition disabled:opacity-50 shrink-0"
                      title="Desvincular e liberar vaga"
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                );
              })}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="px-4 sm:px-6 py-3 bg-slate-50 border-t border-slate-100 flex justify-end shrink-0">
          <button
            onClick={onClose}
            className="w-full sm:w-auto px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-200/70 rounded-lg transition"
          >
            Fechar
          </button>
        </div>
      </div>
    </div>
  );
};
