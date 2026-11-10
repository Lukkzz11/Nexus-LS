import React, { useState } from "react";
import {
  Search,
  Plus,
  Copy,
  Check,
  Calendar,
  ShieldAlert,
  ShieldCheck,
  Monitor,
  XCircle,
  Share2,
} from "lucide-react";
import { Licenca, StatusLicenca, PlanoLicenca } from "../../types";
import { StatusBadge } from "../common/StatusBadge";
import { ConfirmModal } from "../common/ConfirmModal";

interface LicensesViewProps {
  licencas: Licenca[];
  onOpenNovaLicenca: () => void;
  onRenovar: (licenca: Licenca) => void;
  onBloquear: (licenca: Licenca) => void;
  onDesbloquear: (licencaId: string) => Promise<void>;
  onCancelar: (licencaId: string) => Promise<void>;
  onVerDispositivos: (licenca: Licenca) => void;
  onVerificarChave?: () => void;
}

export const LicensesView: React.FC<LicensesViewProps> = ({
  licencas,
  onOpenNovaLicenca,
  onRenovar,
  onBloquear,
  onDesbloquear,
  onCancelar,
  onVerDispositivos,
  onVerificarChave,
}) => {
  const [busca, setBusca] = useState("");
  const [filtroStatus, setFiltroStatus] = useState<StatusLicenca | "TODAS">("TODAS");
  const [filtroPlano, setFiltroPlano] = useState<PlanoLicenca | "TODOS">("TODOS");
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  // Modais de confirmação
  const [licencaParaCancelar, setLicencaParaCancelar] = useState<Licenca | null>(null);
  const [processandoAcao, setProcessandoAcao] = useState(false);

  const handleCopy = (chave: string) => {
    navigator.clipboard.writeText(chave);
    setCopiedKey(chave);
    setTimeout(() => setCopiedKey(null), 2000);
  };

  const handleShareLicenca = async (lic: Licenca) => {
    const vencBr = lic.dataVencimento
      ? new Date(lic.dataVencimento + "T00:00:00").toLocaleDateString("pt-BR")
      : "—";
    const texto =
      `🔑 *Licença Nexus LS*\n` +
      `Cliente: *${lic.clienteNome}*${lic.empresa ? ` (${lic.empresa})` : ""}\n` +
      `Chave de Ativação: *${lic.codigoFormatado}*\n` +
      `Validade: *${vencBr}*\n\n` +
      `Abra o *Nexus Atualizador e Ativador*, cole a chave acima e clique em *Ativar licença*.`;

    if (navigator.share) {
      try {
        await navigator.share({ title: `Licença Nexus LS — ${lic.clienteNome}`, text: texto });
        return;
      } catch {
        // fallback para WhatsApp
      }
    }
    window.open(`https://wa.me/?text=${encodeURIComponent(texto)}`, "_blank");
  };

  const filtradas = licencas.filter((l) => {
    const termo = busca.toLowerCase();
    const bateTexto =
      l.clienteNome.toLowerCase().includes(termo) ||
      l.empresa.toLowerCase().includes(termo) ||
      l.codigoFormatado.toLowerCase().includes(termo) ||
      (l.observacoes && l.observacoes.toLowerCase().includes(termo));

    const bateStatus = filtroStatus === "TODAS" || l.status === filtroStatus;
    const batePlano = filtroPlano === "TODOS" || l.plano === filtroPlano;

    return bateTexto && bateStatus && batePlano;
  });

  const handleConfirmarCancelamento = async () => {
    if (!licencaParaCancelar) return;
    setProcessandoAcao(true);
    try {
      await onCancelar(licencaParaCancelar.id);
      setLicencaParaCancelar(null);
    } catch (err) {
      alert("Falha ao cancelar licença.");
    } finally {
      setProcessandoAcao(false);
    }
  };

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* Top Filter Bar */}
      <div className="bg-white rounded-xl p-3.5 sm:p-4 border border-slate-200 shadow-2xs space-y-3 sm:space-y-4">
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
          <div className="relative flex-1 max-w-md">
            <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
            <input
              type="text"
              value={busca}
              onChange={(e) => setBusca(e.target.value)}
              placeholder="Buscar por cliente, empresa ou chave..."
              className="w-full text-xs pl-9 pr-4 py-2.5 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
            />
          </div>

          <div className="flex flex-wrap items-center gap-2 sm:gap-3">
            <select
              value={filtroPlano}
              onChange={(e) => setFiltroPlano(e.target.value as any)}
              className="flex-1 sm:flex-initial text-xs px-3 py-2.5 bg-slate-50 border border-slate-200 rounded-lg focus:outline-none focus:border-blue-600 transition font-medium text-slate-700"
            >
              <option value="TODOS">Todos os Planos</option>
              <option value="mensal">Mensal (30d)</option>
              <option value="trimestral">Trimestral (90d)</option>
              <option value="semestral">Semestral (180d)</option>
              <option value="anual">Anual (365d)</option>
              <option value="personalizado">Personalizado</option>
            </select>

            <button
              onClick={onVerificarChave}
              className="flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold text-slate-700 bg-white border border-slate-200 hover:bg-slate-50 rounded-lg shadow-2xs transition shrink-0"
            >
              <Search className="w-4 h-4 text-slate-500" />
              <span>Verificar chave</span>
            </button>

            <button
              onClick={onOpenNovaLicenca}
              className="flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition shrink-0"
            >
              <Plus className="w-4 h-4 stroke-[2.5]" />
              <span>Gerar Licença</span>
            </button>
          </div>
        </div>

        {/* Status Tabs Filter */}
        <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs border-t border-slate-100 pt-3">
          {[
            { id: "TODAS", label: "Todas" },
            { id: "ATIVA", label: "Ativas" },
            { id: "VENCENDO", label: "A Vencer (≤15d)" },
            { id: "VENCIDA", label: "Vencidas" },
            { id: "BLOQUEADA", label: "Bloqueadas" },
            { id: "PENDENTE", label: "Pendentes" },
            { id: "CANCELADA", label: "Canceladas" },
          ].map((tab) => {
            const count =
              tab.id === "TODAS"
                ? licencas.length
                : licencas.filter((l) => l.status === tab.id).length;
            const active = filtroStatus === tab.id;

            return (
              <button
                key={tab.id}
                onClick={() => setFiltroStatus(tab.id as any)}
                className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg font-medium transition shrink-0 ${
                  active
                    ? "bg-slate-900 text-white"
                    : "bg-slate-50 text-slate-600 hover:bg-slate-100"
                }`}
              >
                <span>{tab.label}</span>
                <span
                  className={`text-[10px] px-1.5 py-0.2 rounded-full ${
                    active ? "bg-slate-800 text-white" : "bg-slate-200/70 text-slate-700"
                  }`}
                >
                  {count}
                </span>
              </button>
            );
          })}
        </div>
      </div>

      {/* Mobile Cards View (< md) */}
      <div className="md:hidden space-y-3">
        {filtradas.length === 0 ? (
          <div className="bg-white rounded-xl border border-slate-200 p-8 text-center text-xs text-slate-400">
            Nenhuma licença encontrada para os filtros selecionados.
          </div>
        ) : (
          filtradas.map((lic) => (
            <div
              key={lic.id}
              className="bg-white rounded-xl border border-slate-200 shadow-2xs p-4 space-y-3"
            >
              <div className="flex items-start justify-between gap-2">
                <div className="min-w-0">
                  <p className="text-sm font-bold text-slate-900 truncate">{lic.clienteNome}</p>
                  <p className="text-xs text-slate-500 truncate">{lic.empresa || "—"}</p>
                </div>
                <StatusBadge status={lic.status} size="sm" />
              </div>

              {/* Chave + Copiar + WhatsApp */}
              <div className="flex items-center justify-between gap-2 bg-slate-50 border border-slate-200/80 rounded-lg p-2.5">
                <span className="font-mono text-xs font-bold text-slate-800 select-all truncate">
                  {lic.codigoFormatado}
                </span>
                <div className="flex items-center gap-1.5 shrink-0">
                  <button
                    onClick={() => handleCopy(lic.codigoFormatado)}
                    className="px-2.5 py-1.5 bg-white border border-slate-200 rounded-md text-[11px] font-semibold text-slate-700 flex items-center gap-1"
                  >
                    {copiedKey === lic.codigoFormatado ? (
                      <Check className="w-3.5 h-3.5 text-emerald-600" />
                    ) : (
                      <Copy className="w-3.5 h-3.5" />
                    )}
                    <span>{copiedKey === lic.codigoFormatado ? "Copiada" : "Copiar"}</span>
                  </button>
                  <button
                    onClick={() => handleShareLicenca(lic)}
                    className="px-2.5 py-1.5 bg-emerald-600 text-white rounded-md text-[11px] font-semibold flex items-center gap-1"
                    title="Enviar no WhatsApp"
                  >
                    <Share2 className="w-3.5 h-3.5" />
                    <span>Enviar</span>
                  </button>
                </div>
              </div>

              {/* Info Grid */}
              <div className="grid grid-cols-3 gap-2 text-xs pt-1">
                <div>
                  <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                    Plano
                  </span>
                  <span className="font-semibold text-slate-700 capitalize">{lic.plano}</span>
                </div>
                <div>
                  <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                    Vencimento
                  </span>
                  <span className="font-semibold text-slate-800">
                    {lic.dataVencimento
                      ? new Date(lic.dataVencimento + "T00:00:00").toLocaleDateString("pt-BR")
                      : "—"}
                  </span>
                </div>
                <div>
                  <span className="text-[10px] uppercase text-slate-400 font-semibold block">
                    Máquinas
                  </span>
                  <span className="font-mono font-bold text-blue-600">
                    {lic.dispositivosAtivosCount || 0}/{lic.limiteDispositivos} PC(s)
                  </span>
                </div>
              </div>

              {/* Mobile Action Buttons */}
              <div className="grid grid-cols-3 gap-2 pt-2 border-t border-slate-100">
                <button
                  onClick={() => onVerDispositivos(lic)}
                  className="flex items-center justify-center gap-1.5 py-2 px-2 rounded-lg bg-blue-50 text-blue-700 font-semibold text-xs transition"
                >
                  <Monitor className="w-3.5 h-3.5" />
                  <span>Ativar PC</span>
                </button>

                <button
                  onClick={() => onRenovar(lic)}
                  disabled={lic.status === "CANCELADA"}
                  className="flex items-center justify-center gap-1.5 py-2 px-2 rounded-lg bg-emerald-50 text-emerald-700 font-semibold text-xs transition disabled:opacity-40"
                >
                  <Calendar className="w-3.5 h-3.5" />
                  <span>Renovar</span>
                </button>

                {lic.status === "BLOQUEADA" ? (
                  <button
                    onClick={() => onDesbloquear(lic.id)}
                    className="flex items-center justify-center gap-1.5 py-2 px-2 rounded-lg bg-amber-50 text-amber-800 font-semibold text-xs transition"
                  >
                    <ShieldCheck className="w-3.5 h-3.5" />
                    <span>Liberar</span>
                  </button>
                ) : (
                  <button
                    onClick={() => onBloquear(lic)}
                    disabled={lic.status === "CANCELADA"}
                    className="flex items-center justify-center gap-1.5 py-2 px-2 rounded-lg bg-rose-50 text-rose-700 font-semibold text-xs transition disabled:opacity-40"
                  >
                    <ShieldAlert className="w-3.5 h-3.5" />
                    <span>Bloquear</span>
                  </button>
                )}
              </div>
            </div>
          ))
        )}
      </div>

      {/* Desktop Main Table (hidden on mobile) */}
      <div className="hidden md:block bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse text-xs">
            <thead>
              <tr className="bg-slate-50/75 border-b border-slate-100 text-slate-500 font-semibold uppercase text-[10px] tracking-wider">
                <th className="py-3 px-4">Cliente / Loja</th>
                <th className="py-3 px-4">Código da Licença</th>
                <th className="py-3 px-4">Plano</th>
                <th className="py-3 px-4">Início</th>
                <th className="py-3 px-4">Vencimento</th>
                <th className="py-3 px-4 text-center">Situação</th>
                <th className="py-3 px-4 text-center">Máquinas</th>
                <th className="py-3 px-4 text-right">Ações</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 text-slate-700">
              {filtradas.length === 0 ? (
                <tr>
                  <td colSpan={8} className="py-12 text-center text-slate-400">
                    Nenhuma licença encontrada para os filtros selecionados.
                  </td>
                </tr>
              ) : (
                filtradas.map((lic) => {
                  return (
                    <tr key={lic.id} className="hover:bg-slate-50/60 transition duration-75">
                      <td className="py-3 px-4">
                        <p className="font-semibold text-slate-900">{lic.clienteNome}</p>
                        <p className="text-[11px] text-slate-400">{lic.empresa || "—"}</p>
                      </td>
                      <td className="py-3 px-4">
                        <div className="inline-flex items-center gap-1.5 font-mono text-xs bg-slate-100 px-2 py-1 rounded text-slate-800">
                          <span>{lic.codigoFormatado}</span>
                          <button
                            onClick={() => handleCopy(lic.codigoFormatado)}
                            className="text-slate-400 hover:text-slate-600 p-0.5 rounded transition"
                            title="Copiar código"
                          >
                            {copiedKey === lic.codigoFormatado ? (
                              <Check className="w-3.5 h-3.5 text-emerald-600" />
                            ) : (
                              <Copy className="w-3.5 h-3.5" />
                            )}
                          </button>
                          <button
                            onClick={() => handleShareLicenca(lic)}
                            className="text-slate-400 hover:text-emerald-600 p-0.5 rounded transition"
                            title="Enviar chave no WhatsApp"
                          >
                            <Share2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                      </td>
                      <td className="py-3 px-4 capitalize text-slate-600 font-medium">
                        {lic.plano}
                      </td>
                      <td className="py-3 px-4 text-slate-500">
                        {lic.dataInicio
                          ? new Date(lic.dataInicio + "T00:00:00").toLocaleDateString("pt-BR")
                          : "—"}
                      </td>
                      <td className="py-3 px-4">
                        <p className="font-medium text-slate-800">
                          {lic.dataVencimento
                            ? new Date(lic.dataVencimento + "T00:00:00").toLocaleDateString("pt-BR")
                            : "—"}
                        </p>
                        {lic.diasRestantes !== undefined && (
                          <p
                            className={`text-[10px] font-medium ${
                              lic.diasRestantes < 0
                                ? "text-red-600"
                                : lic.diasRestantes <= 15
                                ? "text-amber-600"
                                : "text-slate-400"
                            }`}
                          >
                            {lic.diasRestantes < 0
                              ? `Venceu há ${Math.abs(lic.diasRestantes)} d`
                              : `${lic.diasRestantes} dias restantes`}
                          </p>
                        )}
                      </td>
                      <td className="py-3 px-4 text-center">
                        <StatusBadge status={lic.status} size="sm" />
                      </td>
                      <td className="py-3 px-4 text-center">
                        <button
                          onClick={() => onVerDispositivos(lic)}
                          className="inline-flex items-center gap-1 font-mono text-xs text-slate-700 bg-slate-100 hover:bg-slate-200/80 px-2.5 py-1 rounded transition"
                          title="Gerenciar ou ativar computadores vinculados"
                        >
                          <Monitor className="w-3 h-3 text-slate-500" />
                          <span>
                            {lic.dispositivosAtivosCount || 0}/{lic.limiteDispositivos}
                          </span>
                        </button>
                      </td>
                      <td className="py-3 px-4 text-right">
                        <div className="flex items-center justify-end gap-1">
                          {/* Renovar */}
                          <button
                            onClick={() => onRenovar(lic)}
                            disabled={lic.status === "CANCELADA"}
                            className="p-1.5 text-emerald-600 hover:bg-emerald-50 rounded-lg transition disabled:opacity-30"
                            title="Renovar validade"
                          >
                            <Calendar className="w-4 h-4" />
                          </button>

                          {/* Bloquear / Desbloquear */}
                          {lic.status === "BLOQUEADA" ? (
                            <button
                              onClick={() => onDesbloquear(lic.id)}
                              className="p-1.5 text-blue-600 hover:bg-blue-50 rounded-lg transition"
                              title="Desbloquear licença"
                            >
                              <ShieldCheck className="w-4 h-4" />
                            </button>
                          ) : (
                            <button
                              onClick={() => onBloquear(lic)}
                              disabled={lic.status === "CANCELADA"}
                              className="p-1.5 text-rose-600 hover:bg-rose-50 rounded-lg transition disabled:opacity-30"
                              title="Bloquear / Suspender"
                            >
                              <ShieldAlert className="w-4 h-4" />
                            </button>
                          )}

                          {/* Cancelar definitivamente */}
                          {lic.status !== "CANCELADA" && (
                            <button
                              onClick={() => setLicencaParaCancelar(lic)}
                              className="p-1.5 text-slate-400 hover:text-red-600 hover:bg-red-50 rounded-lg transition"
                              title="Cancelar definitivamente"
                            >
                              <XCircle className="w-4 h-4" />
                            </button>
                          )}
                        </div>
                      </td>
                    </tr>
                  );
                })
              )}
            </tbody>
          </table>
        </div>
      </div>

      {/* Modal de Cancelamento Definitivo */}
      <ConfirmModal
        isOpen={!!licencaParaCancelar}
        title="Cancelar Licença Definitivamente?"
        description={`A licença ${licencaParaCancelar?.codigoFormatado} do cliente "${licencaParaCancelar?.clienteNome}" será cancelada sem permissão de reativação automática.`}
        confirmLabel="Sim, Cancelar Definitivamente"
        variant="danger"
        isLoading={processandoAcao}
        onConfirm={handleConfirmarCancelamento}
        onCancel={() => setLicencaParaCancelar(null)}
      />
    </div>
  );
};
