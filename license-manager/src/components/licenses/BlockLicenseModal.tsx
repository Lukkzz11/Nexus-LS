import React, { useState } from "react";
import { X, ShieldAlert, AlertTriangle } from "lucide-react";
import { Licenca } from "../../types";

interface BlockLicenseModalProps {
  isOpen: boolean;
  onClose: () => void;
  licenca: Licenca | null;
  onBlock: (licencaId: string, motivo: string) => Promise<void>;
}

export const BlockLicenseModal: React.FC<BlockLicenseModalProps> = ({
  isOpen,
  onClose,
  licenca,
  onBlock,
}) => {
  const [motivo, setMotivo] = useState("");
  const [bloqueando, setBloqueando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  if (!isOpen || !licenca) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!motivo.trim()) {
      setErro("Informe obrigatoriamente o motivo do bloqueio para auditoria.");
      return;
    }

    setBloqueando(true);
    setErro(null);
    try {
      await onBlock(licenca.id, motivo.trim());
      onClose();
    } catch (err: any) {
      setErro(err.message || "Erro ao suspender licença.");
    } finally {
      setBloqueando(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div className="relative w-full max-w-md bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-rose-50 text-rose-600">
              <ShieldAlert className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900">Bloquear / Suspender Licença</h3>
              <p className="text-xs text-slate-400">{licenca.clienteNome}</p>
            </div>
          </div>
          <button
            onClick={onClose}
            disabled={bloqueando}
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

            <div className="p-3 bg-rose-50/60 border border-rose-200 rounded-lg flex items-start gap-3">
              <AlertTriangle className="w-5 h-5 text-rose-600 shrink-0 mt-0.5" />
              <div className="text-xs text-rose-800 leading-relaxed">
                Ao bloquear, o Atualizador e o Nexus LS <strong>recusarão imediatamente</strong> a inicialização em todos os computadores vinculados a esta licença.
              </div>
            </div>

            <div className="p-3 bg-slate-50 border border-slate-200 rounded-lg text-xs space-y-1">
              <div className="flex justify-between">
                <span className="text-slate-500">Chave:</span>
                <span className="font-mono font-bold text-slate-900">{licenca.codigoFormatado}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-slate-500">Empresa:</span>
                <span className="font-medium text-slate-800">{licenca.empresa || "—"}</span>
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                Motivo do Bloqueio * <span className="text-slate-400 font-normal">(registrado na auditoria)</span>
              </label>
              <textarea
                required
                rows={3}
                value={motivo}
                onChange={(e) => setMotivo(e.target.value)}
                placeholder="Ex: Inadimplência na mensalidade, encerramento de contrato, violação de termos de uso..."
                className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-rose-500/20 focus:border-rose-600 transition resize-none"
              />
            </div>
          </div>

          {/* Footer Actions */}
          <div className="flex items-center justify-end gap-3 px-6 py-4 bg-slate-50 border-t border-slate-100">
            <button
              type="button"
              onClick={onClose}
              disabled={bloqueando}
              className="px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-200/70 rounded-lg transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={bloqueando}
              className="px-4 py-2 text-xs font-semibold text-white bg-rose-600 hover:bg-rose-700 rounded-lg shadow-xs transition disabled:opacity-50"
            >
              {bloqueando ? "Bloqueando..." : "Confirmar Bloqueio"}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
