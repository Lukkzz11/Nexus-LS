import React, { useState } from "react";
import { X, Calendar, Clock, Check } from "lucide-react";
import { Licenca } from "../../types";

interface RenewLicenseModalProps {
  isOpen: boolean;
  onClose: () => void;
  licenca: Licenca | null;
  onRenew: (licencaId: string, novaDataVencimento: string, diasAdicionais?: number) => Promise<void>;
}

export const RenewLicenseModal: React.FC<RenewLicenseModalProps> = ({
  isOpen,
  onClose,
  licenca,
  onRenew,
}) => {
  const [diasSelecionados, setDiasSelecionados] = useState<number>(365);
  const [dataManual, setDataManual] = useState<string>("");
  const [modoManual, setModoManual] = useState<boolean>(false);
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  if (!isOpen || !licenca) return null;

  // Calcula nova data de vencimento
  const dataBase = licenca.dataVencimento ? new Date(licenca.dataVencimento + "T00:00:00") : new Date();
  const hoje = new Date();
  // Se já venceu, soma a partir de hoje; se ainda está válida, soma a partir do vencimento atual
  const baseCalculo = dataBase > hoje ? dataBase : hoje;

  const novaDataCalculada = new Date(baseCalculo);
  novaDataCalculada.setDate(novaDataCalculada.getDate() + diasSelecionados);
  const novaDataCalculadaStr = novaDataCalculada.toISOString().split("T")[0];

  const dataFinal = modoManual ? dataManual : novaDataCalculadaStr;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!dataFinal) {
      setErro("Informe uma data válida de vencimento.");
      return;
    }

    setSalvando(true);
    setErro(null);
    try {
      await onRenew(licenca.id, dataFinal, modoManual ? undefined : diasSelecionados);
      onClose();
    } catch (err: any) {
      setErro(err.message || "Erro ao renovar licença.");
    } finally {
      setSalvando(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div className="relative w-full max-w-md bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-emerald-50 text-emerald-600">
              <Calendar className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900">Renovar Licença</h3>
              <p className="text-xs text-slate-400">
                Prorrogar validade para {licenca.clienteNome}
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            disabled={salvando}
            className="text-slate-400 hover:text-slate-600 rounded-lg p-1 transition"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Body */}
        <form onSubmit={handleSubmit}>
          <div className="p-6 space-y-4">
            {erro && (
              <div className="p-3 text-xs font-medium text-red-700 bg-red-50 border border-red-200 rounded-lg">
                {erro}
              </div>
            )}

            {/* Licença Info */}
            <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg">
              <div className="flex items-center justify-between text-xs">
                <span className="text-slate-500">Chave:</span>
                <span className="font-mono font-bold text-slate-900">{licenca.codigoFormatado}</span>
              </div>
              <div className="flex items-center justify-between text-xs mt-1.5">
                <span className="text-slate-500">Vencimento Atual:</span>
                <span className="font-medium text-slate-800">
                  {new Date(licenca.dataVencimento + "T00:00:00").toLocaleDateString("pt-BR")}
                </span>
              </div>
            </div>

            {/* Quick Add Presets */}
            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1.5">
                Prorrogação Rápida
              </label>
              <div className="grid grid-cols-2 gap-2">
                {[
                  { dias: 30, label: "+30 Dias (1 Mês)" },
                  { dias: 90, label: "+90 Dias (3 Meses)" },
                  { dias: 180, label: "+180 Dias (Semestral)" },
                  { dias: 365, label: "+365 Dias (1 Ano)" },
                ].map((item) => (
                  <button
                    key={item.dias}
                    type="button"
                    onClick={() => {
                      setDiasSelecionados(item.dias);
                      setModoManual(false);
                    }}
                    className={`p-2.5 rounded-lg border text-xs font-semibold text-left transition ${
                      !modoManual && diasSelecionados === item.dias
                        ? "bg-emerald-50 border-emerald-600 text-emerald-800"
                        : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                    }`}
                  >
                    {item.label}
                  </button>
                ))}
              </div>
            </div>

            {/* Data Manual */}
            <div>
              <button
                type="button"
                onClick={() => {
                  setModoManual(!modoManual);
                  if (!modoManual && !dataManual) {
                    setDataManual(novaDataCalculadaStr);
                  }
                }}
                className="text-xs text-blue-600 hover:text-blue-700 font-semibold mb-2 block"
              >
                {modoManual ? "Voltar aos prazos rápidos" : "Ou definir data específica manualmente..."}
              </button>

              {modoManual && (
                <input
                  type="date"
                  required
                  value={dataManual}
                  onChange={(e) => setDataManual(e.target.value)}
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              )}
            </div>

            {/* Preview da Nova Data */}
            <div className="p-3 bg-emerald-50/70 border border-emerald-200 rounded-lg flex items-center justify-between">
              <div>
                <span className="text-[10px] font-bold uppercase tracking-wider text-emerald-700">
                  Novo Vencimento Previsto
                </span>
                <p className="text-base font-bold text-emerald-950">
                  {new Date(dataFinal + "T00:00:00").toLocaleDateString("pt-BR")}
                </p>
              </div>
              <span className="text-[11px] font-medium text-emerald-700 bg-white px-2 py-1 rounded shadow-2xs">
                Status: ATIVA
              </span>
            </div>
          </div>

          {/* Footer Actions */}
          <div className="flex items-center justify-end gap-3 px-6 py-4 bg-slate-50 border-t border-slate-100">
            <button
              type="button"
              onClick={onClose}
              disabled={salvando}
              className="px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-200/70 rounded-lg transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={salvando}
              className="flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-emerald-600 hover:bg-emerald-700 rounded-lg shadow-xs transition disabled:opacity-50"
            >
              <Check className="w-4 h-4 stroke-[2.5]" />
              <span>{salvando ? "Renovando..." : "Confirmar Renovação"}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
