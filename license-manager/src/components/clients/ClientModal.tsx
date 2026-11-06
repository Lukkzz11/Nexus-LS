import React, { useState, useEffect } from "react";
import { X, UserPlus, Building, Phone, Mail, FileText, Check } from "lucide-react";
import { Cliente } from "../../types";

interface ClientModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSave: (dados: Omit<Cliente, "id" | "totalLicencas" | "criadoEm">) => Promise<void>;
  clienteParaEditar?: Cliente | null;
}

export const ClientModal: React.FC<ClientModalProps> = ({
  isOpen,
  onClose,
  onSave,
  clienteParaEditar,
}) => {
  const [nomeResponsavel, setNomeResponsavel] = useState("");
  const [empresa, setEmpresa] = useState("");
  const [documento, setDocumento] = useState("");
  const [telefone, setTelefone] = useState("");
  const [email, setEmail] = useState("");
  const [observacoes, setObservacoes] = useState("");
  const [salvando, setSalvando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  useEffect(() => {
    if (clienteParaEditar) {
      setNomeResponsavel(clienteParaEditar.nomeResponsavel || "");
      setEmpresa(clienteParaEditar.empresa || "");
      setDocumento(clienteParaEditar.documento || "");
      setTelefone(clienteParaEditar.telefone || "");
      setEmail(clienteParaEditar.email || "");
      setObservacoes(clienteParaEditar.observacoes || "");
    } else {
      setNomeResponsavel("");
      setEmpresa("");
      setDocumento("");
      setTelefone("");
      setEmail("");
      setObservacoes("");
    }
    setErro(null);
  }, [clienteParaEditar, isOpen]);

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!nomeResponsavel.trim()) {
      setErro("Informe o nome do responsável.");
      return;
    }
    if (!empresa.trim()) {
      setErro("Informe o nome da empresa ou loja.");
      return;
    }
    if (!email.trim() || !email.includes("@")) {
      setErro("Informe um e-mail válido.");
      return;
    }

    setSalvando(true);
    setErro(null);
    try {
      await onSave({
        nomeResponsavel: nomeResponsavel.trim(),
        empresa: empresa.trim(),
        documento: documento.trim() || undefined,
        telefone: telefone.trim() || undefined,
        email: email.trim().toLowerCase(),
        observacoes: observacoes.trim() || undefined,
      });
      onClose();
    } catch (err: any) {
      setErro(err.message || "Erro ao salvar cliente.");
    } finally {
      setSalvando(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div className="relative w-full max-w-lg bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-blue-50 text-blue-600">
              <UserPlus className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900">
                {clienteParaEditar ? "Editar Cliente" : "Cadastrar Novo Cliente"}
              </h3>
              <p className="text-xs text-slate-400">
                Dados cadastrais e comerciais para controle de licenças
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

        {/* Form Body */}
        <form onSubmit={handleSubmit}>
          <div className="p-6 space-y-4 max-h-[75vh] overflow-y-auto">
            {erro && (
              <div className="p-3 text-xs font-medium text-red-700 bg-red-50 border border-red-200 rounded-lg">
                {erro}
              </div>
            )}

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Nome do Responsável *
                </label>
                <input
                  type="text"
                  required
                  value={nomeResponsavel}
                  onChange={(e) => setNomeResponsavel(e.target.value)}
                  placeholder="Ex: Carlos Mendes"
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Empresa / Loja *
                </label>
                <div className="relative">
                  <input
                    type="text"
                    required
                    value={empresa}
                    onChange={(e) => setEmpresa(e.target.value)}
                    placeholder="Ex: Auto Peças Mendes"
                    className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                  />
                </div>
              </div>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  CPF ou CNPJ <span className="text-slate-400 font-normal">(opcional)</span>
                </label>
                <input
                  type="text"
                  value={documento}
                  onChange={(e) => setDocumento(e.target.value)}
                  placeholder="000.000.000-00 ou CNPJ"
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Telefone / WhatsApp <span className="text-slate-400 font-normal">(opcional)</span>
                </label>
                <input
                  type="text"
                  value={telefone}
                  onChange={(e) => setTelefone(e.target.value)}
                  placeholder="(00) 00000-0000"
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                E-mail Comercial *
              </label>
              <input
                type="email"
                required
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="contato@empresa.com.br"
                className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
              />
            </div>

            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                Observações Administrativas <span className="text-slate-400 font-normal">(opcional)</span>
              </label>
              <textarea
                rows={3}
                value={observacoes}
                onChange={(e) => setObservacoes(e.target.value)}
                placeholder="Informações sobre forma de pagamento, contatos adicionais, particularidades do contrato..."
                className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition resize-none"
              />
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
              className="flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition disabled:opacity-50"
            >
              <Check className="w-4 h-4 stroke-[2.5]" />
              <span>{salvando ? "Salvando..." : clienteParaEditar ? "Atualizar" : "Salvar Cadastro"}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
