import React, { useState, useEffect } from "react";
import { X, Monitor, Trash2, ShieldCheck, Clock, AlertTriangle } from "lucide-react";
import { Licenca, DispositivoVinculado } from "../../types";
import {
  listarDispositivosPorLicenca,
  desvincularDispositivo,
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
      carregar();
    }
  }, [isOpen, licenca]);

  if (!isOpen || !licenca) return null;

  const handleRemover = async (disp: DispositivoVinculado) => {
    if (!window.confirm(`Tem certeza que deseja desvincular o computador "${disp.nomeDispositivo}" (${disp.identificadorDispositivo})? Esta ação liberará 1 vaga na licença.`)) {
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

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div className="relative w-full max-w-xl bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-blue-50 text-blue-600">
              <Monitor className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900">Dispositivos Vinculados</h3>
              <p className="text-xs text-slate-400">
                {licenca.clienteNome} ({licenca.codigoFormatado})
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="text-slate-400 hover:text-slate-600 rounded-lg p-1 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <div className="p-6 space-y-4 max-h-[70vh] overflow-y-auto">
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
                dispositivos.length >= licenca.limiteDispositivos
                  ? "bg-amber-100 text-amber-800"
                  : "bg-emerald-100 text-emerald-800"
              }`}
            >
              {dispositivos.length >= licenca.limiteDispositivos ? "Limite Atingido" : "Vagas Livres"}
            </span>
          </div>

          {carregando ? (
            <p className="text-xs text-center py-8 text-slate-400">Carregando dispositivos...</p>
          ) : dispositivos.length === 0 ? (
            <div className="text-center py-8 border border-dashed border-slate-200 rounded-lg">
              <Monitor className="w-8 h-8 text-slate-300 mx-auto mb-2" />
              <p className="text-xs font-medium text-slate-600">Nenhum computador vinculado ainda.</p>
              <p className="text-[11px] text-slate-400 mt-1 max-w-sm mx-auto">
                O primeiro computador que ativar a chave no Atualizador do Nexus LS será registrado aqui automaticamente.
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
                    <div>
                      <div className="flex items-center gap-2">
                        <Monitor className="w-4 h-4 text-slate-600" />
                        <span className="font-bold text-slate-900">{disp.nomeDispositivo}</span>
                        <span className="text-[10px] font-mono bg-slate-100 px-1.5 py-0.5 rounded text-slate-600">
                          {disp.identificadorDispositivo}
                        </span>
                      </div>
                      <div className="flex items-center gap-4 text-[11px] text-slate-400 mt-1.5">
                        <span>Ativado: {primeiraData}</span>
                        <span>Última checagem: {ultimaData}</span>
                      </div>
                    </div>

                    <button
                      onClick={() => handleRemover(disp)}
                      disabled={removendoId === disp.id}
                      className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition disabled:opacity-50"
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
        <div className="px-6 py-3 bg-slate-50 border-t border-slate-100 flex justify-end">
          <button
            onClick={onClose}
            className="px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-200/70 rounded-lg transition"
          >
            Fechar
          </button>
        </div>
      </div>
    </div>
  );
};
