import React, { useState, useEffect } from "react";
import {
  X,
  KeyRound,
  RefreshCw,
  Copy,
  Check,
  Calendar,
  Monitor,
  User,
  Shield,
  Clock,
} from "lucide-react";
import { Cliente, PlanoLicenca } from "../../types";
import {
  gerarChaveNexus,
  calcularDataVencimentoPorPlano,
} from "../../services/licenseService";

interface GenerateLicenseModalProps {
  isOpen: boolean;
  onClose: () => void;
  clientes: Cliente[];
  clientePreselecionado?: Cliente | null;
  onGenerate: (params: {
    clienteId: string;
    clienteNome: string;
    empresa: string;
    plano: PlanoLicenca;
    dataInicio: string;
    dataVencimento: string;
    limiteDispositivos: number;
    observacoes?: string;
    chavePersonalizada?: string;
  }) => Promise<void>;
}

export const GenerateLicenseModal: React.FC<GenerateLicenseModalProps> = ({
  isOpen,
  onClose,
  clientes,
  clientePreselecionado,
  onGenerate,
}) => {
  const [clienteId, setClienteId] = useState("");
  const [plano, setPlano] = useState<PlanoLicenca>("anual");
  const [dataInicio, setDataInicio] = useState(new Date().toISOString().split("T")[0]);
  const [dataVencimento, setDataVencimento] = useState("");
  const [limiteDispositivos, setLimiteDispositivos] = useState(1);
  const [observacoes, setObservacoes] = useState("");
  const [chaveGerada, setChaveGerada] = useState("");
  const [copiado, setCopiado] = useState(false);
  const [gerando, setGerando] = useState(false);
  const [erro, setErro] = useState<string | null>(null);

  // Inicialização e recálculo
  useEffect(() => {
    if (isOpen) {
      setChaveGerada(gerarChaveNexus());
      setCopiado(false);
      setErro(null);
      const hoje = new Date().toISOString().split("T")[0];
      setDataInicio(hoje);
      setDataVencimento(calcularDataVencimentoPorPlano("anual", hoje));

      if (clientePreselecionado) {
        setClienteId(clientePreselecionado.id);
      } else if (clientes.length > 0 && !clienteId) {
        setClienteId(clientes[0].id);
      }
    }
  }, [isOpen, clientePreselecionado, clientes]);

  const handlePlanoChange = (novoPlano: PlanoLicenca) => {
    setPlano(novoPlano);
    if (novoPlano !== "personalizado") {
      setDataVencimento(calcularDataVencimentoPorPlano(novoPlano, dataInicio));
    }
  };

  const handleDataInicioChange = (novaData: string) => {
    setDataInicio(novaData);
    if (plano !== "personalizado") {
      setDataVencimento(calcularDataVencimentoPorPlano(plano, novaData));
    }
  };

  const handleRegenerarChave = () => {
    setChaveGerada(gerarChaveNexus());
    setCopiado(false);
  };

  const handleCopiarChave = () => {
    navigator.clipboard.writeText(chaveGerada);
    setCopiado(true);
    setTimeout(() => setCopiado(false), 2000);
  };

  if (!isOpen) return null;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!clienteId) {
      setErro("Selecione um cliente para vincular à licença.");
      return;
    }
    if (!dataVencimento) {
      setErro("Defina a data de vencimento da licença.");
      return;
    }

    const selecionado = clientes.find((c) => c.id === clienteId);
    if (!selecionado) {
      setErro("Cliente selecionado não foi encontrado.");
      return;
    }

    setGerando(true);
    setErro(null);
    try {
      await onGenerate({
        clienteId: selecionado.id,
        clienteNome: selecionado.nomeResponsavel,
        empresa: selecionado.empresa,
        plano,
        dataInicio,
        dataVencimento,
        limiteDispositivos: Number(limiteDispositivos) || 1,
        observacoes: observacoes.trim() || undefined,
        chavePersonalizada: chaveGerada,
      });
      onClose();
    } catch (err: any) {
      setErro(err.message || "Erro ao gerar licença.");
    } finally {
      setGerando(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-slate-900/60 backdrop-blur-xs animate-in fade-in duration-150">
      <div className="relative w-full max-w-xl bg-white rounded-xl shadow-xl border border-slate-200 overflow-hidden">
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-slate-100">
          <div className="flex items-center gap-2.5">
            <div className="p-2 rounded-lg bg-blue-50 text-blue-600">
              <KeyRound className="w-5 h-5" />
            </div>
            <div>
              <h3 className="text-base font-bold text-slate-900">Gerar Nova Licença Comercial</h3>
              <p className="text-xs text-slate-400">
                Emissão segura de chave vinculada ao cliente e ao Nexus LS
              </p>
            </div>
          </div>
          <button
            onClick={onClose}
            disabled={gerando}
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

            {/* Chave Gerada Preview */}
            <div className="p-4 bg-slate-50 border border-slate-200 rounded-xl">
              <div className="flex items-center justify-between mb-2">
                <span className="text-[11px] font-bold uppercase tracking-wider text-slate-500">
                  Chave Única Gerada
                </span>
                <span className="text-[10px] text-emerald-600 font-semibold bg-emerald-50 px-2 py-0.5 rounded">
                  Criptograficamente Segura
                </span>
              </div>

              <div className="flex items-center gap-2">
                <input
                  type="text"
                  readOnly
                  value={chaveGerada}
                  className="w-full font-mono text-base font-bold tracking-wider text-slate-900 bg-white border border-slate-200 px-3 py-2 rounded-lg select-all text-center shadow-2xs"
                />
                <button
                  type="button"
                  onClick={handleCopiarChave}
                  className="px-3 py-2 bg-white hover:bg-slate-100 border border-slate-200 text-slate-700 rounded-lg text-xs font-semibold flex items-center gap-1.5 transition shadow-2xs"
                  title="Copiar chave"
                >
                  {copiado ? <Check className="w-4 h-4 text-emerald-600" /> : <Copy className="w-4 h-4" />}
                  <span>{copiado ? "Copiada" : "Copiar"}</span>
                </button>
                <button
                  type="button"
                  onClick={handleRegenerarChave}
                  className="p-2 bg-white hover:bg-slate-100 border border-slate-200 text-slate-700 rounded-lg transition shadow-2xs"
                  title="Gerar outra chave"
                >
                  <RefreshCw className="w-4 h-4" />
                </button>
              </div>
            </div>

            {/* Cliente Selector */}
            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                Cliente Vinculado *
              </label>
              <select
                required
                value={clienteId}
                onChange={(e) => setClienteId(e.target.value)}
                className="w-full text-xs px-3 py-2 bg-white border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
              >
                <option value="">Selecione um cliente...</option>
                {clientes.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.nomeResponsavel} — {c.empresa} ({c.email})
                  </option>
                ))}
              </select>
            </div>

            {/* Plano Selector */}
            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                Plano de Cobrança / Vigência *
              </label>
              <div className="grid grid-cols-2 sm:grid-cols-5 gap-2">
                {(
                  [
                    { id: "mensal", label: "Mensal", days: "30 dias" },
                    { id: "trimestral", label: "Trimestral", days: "90 dias" },
                    { id: "semestral", label: "Semestral", days: "180 dias" },
                    { id: "anual", label: "Anual", days: "365 dias" },
                    { id: "personalizado", label: "Manual", days: "Livre" },
                  ] as const
                ).map((p) => {
                  const sel = plano === p.id;
                  return (
                    <button
                      key={p.id}
                      type="button"
                      onClick={() => handlePlanoChange(p.id)}
                      className={`p-2 rounded-lg border text-left transition ${
                        sel
                          ? "bg-blue-50 border-blue-600 text-blue-700"
                          : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                      }`}
                    >
                      <p className="text-xs font-bold leading-tight">{p.label}</p>
                      <p className="text-[10px] text-slate-400 mt-0.5">{p.days}</p>
                    </button>
                  );
                })}
              </div>
            </div>

            {/* Datas de Vigência */}
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Data de Início *
                </label>
                <input
                  type="date"
                  required
                  value={dataInicio}
                  onChange={(e) => handleDataInicioChange(e.target.value)}
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-semibold text-slate-700 mb-1">
                  Data de Vencimento *
                </label>
                <input
                  type="date"
                  required
                  value={dataVencimento}
                  onChange={(e) => {
                    setDataVencimento(e.target.value);
                    setPlano("personalizado");
                  }}
                  className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
                />
              </div>
            </div>

            {/* Limite de Dispositivos */}
            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                Limite de Computadores Autorizados *
              </label>
              <div className="flex items-center gap-2">
                {[1, 2, 3, 5, 10].map((num) => (
                  <button
                    key={num}
                    type="button"
                    onClick={() => setLimiteDispositivos(num)}
                    className={`flex-1 py-1.5 text-xs font-semibold rounded-lg border transition ${
                      limiteDispositivos === num
                        ? "bg-blue-600 border-blue-600 text-white shadow-xs"
                        : "bg-white border-slate-200 text-slate-700 hover:bg-slate-50"
                    }`}
                  >
                    {num} {num === 1 ? "Máquina" : "Máquinas"}
                  </button>
                ))}
              </div>
              <p className="text-[11px] text-slate-400 mt-1">
                O Atualizador do Nexus LS impedirá ativações adicionais quando este limite for atingido.
              </p>
            </div>

            {/* Observações */}
            <div>
              <label className="block text-xs font-semibold text-slate-700 mb-1">
                Observações da Licença <span className="text-slate-400 font-normal">(opcional)</span>
              </label>
              <textarea
                rows={2}
                value={observacoes}
                onChange={(e) => setObservacoes(e.target.value)}
                placeholder="Ex: Licença cortesia, filial 2, upgrade de versão..."
                className="w-full text-xs px-3 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition resize-none"
              />
            </div>
          </div>

          {/* Footer Actions */}
          <div className="flex items-center justify-end gap-3 px-6 py-4 bg-slate-50 border-t border-slate-100">
            <button
              type="button"
              onClick={onClose}
              disabled={gerando}
              className="px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-200/70 rounded-lg transition"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={gerando}
              className="flex items-center gap-1.5 px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition disabled:opacity-50"
            >
              <Check className="w-4 h-4 stroke-[2.5]" />
              <span>{gerando ? "Emitindo..." : "Gerar e Ativar"}</span>
            </button>
          </div>
        </form>
      </div>
    </div>
  );
};
