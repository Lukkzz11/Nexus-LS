import React from "react";
import {
  Users,
  KeyRound,
  AlertCircle,
  ShieldAlert,
  Clock,
  Monitor,
  Plus,
  ArrowRight,
  Copy,
  Check,
} from "lucide-react";
import { Cliente, Licenca } from "../../types";
import { StatusBadge } from "../common/StatusBadge";
import { TabId } from "../layout/Sidebar";

interface DashboardViewProps {
  clientes: Cliente[];
  licencas: Licenca[];
  onNavigate: (tab: TabId) => void;
  onOpenNovaLicenca: () => void;
  onOpenNovoCliente: () => void;
}

export const DashboardView: React.FC<DashboardViewProps> = ({
  clientes,
  licencas,
  onNavigate,
  onOpenNovaLicenca,
  onOpenNovoCliente,
}) => {
  const [copiedKey, setCopiedKey] = React.useState<string | null>(null);

  // Métricas
  const totalClientes = clientes.length;
  const ativas = licencas.filter((l) => l.status === "ATIVA" || l.status === "VENCENDO").length;
  const vencidas = licencas.filter((l) => l.status === "VENCIDA").length;
  const bloqueadas = licencas.filter((l) => l.status === "BLOQUEADA").length;
  const vencendo = licencas.filter((l) => l.status === "VENCENDO").length;
  const totalDispositivosAtivos = licencas.reduce((acc, l) => acc + (l.dispositivosAtivosCount || 0), 0);

  // Licenças recentes (top 8)
  const recentes = licencas.slice(0, 8);
  const clientesRecentes = clientes.slice(0, 5);

  const handleCopy = (chave: string) => {
    navigator.clipboard.writeText(chave);
    setCopiedKey(chave);
    setTimeout(() => setCopiedKey(null), 2000);
  };

  return (
    <div className="space-y-6">
      {/* Top Banner & Quick Actions */}
      <div className="bg-white rounded-xl p-6 border border-slate-200 shadow-2xs flex flex-col md:flex-row md:items-center justify-between gap-4">
        <div>
          <h2 className="text-lg font-bold text-slate-900 tracking-tight">
            Central de Controle de Licenciamento
          </h2>
          <p className="text-xs text-slate-500 mt-1 max-w-xl">
            Gerencie o ciclo de vida comercial do Nexus LS, controle de máquinas ativas e sincronização com o Atualizador.
          </p>
        </div>
        <div className="flex items-center gap-2.5 shrink-0">
          <button
            onClick={onOpenNovoCliente}
            className="flex items-center gap-2 px-3.5 py-2 text-xs font-semibold text-slate-700 bg-slate-50 hover:bg-slate-100 border border-slate-200 rounded-lg transition"
          >
            <Users className="w-3.5 h-3.5" />
            <span>Cadastrar Cliente</span>
          </button>
          <button
            onClick={onOpenNovaLicenca}
            className="flex items-center gap-2 px-4 py-2 text-xs font-semibold text-white bg-blue-600 hover:bg-blue-700 rounded-lg shadow-xs transition"
          >
            <Plus className="w-4 h-4 stroke-[2.5]" />
            <span>Gerar Nova Licença</span>
          </button>
        </div>
      </div>

      {/* KPI Cards Grid */}
      <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-6 gap-4">
        {/* Total Clientes */}
        <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-2xs">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-slate-400">
              Clientes
            </span>
            <div className="p-1.5 bg-slate-100 text-slate-600 rounded-md">
              <Users className="w-3.5 h-3.5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-2xl font-bold text-slate-900">{totalClientes}</span>
            <p className="text-[11px] text-slate-500 mt-0.5">Empresas e pessoas</p>
          </div>
        </div>

        {/* Licenças Ativas */}
        <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-2xs">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-emerald-600">
              Ativas
            </span>
            <div className="p-1.5 bg-emerald-50 text-emerald-600 rounded-md">
              <KeyRound className="w-3.5 h-3.5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-2xl font-bold text-emerald-700">{ativas}</span>
            <p className="text-[11px] text-slate-500 mt-0.5">Em uso no Nexus LS</p>
          </div>
        </div>

        {/* Vencendo em breve */}
        <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-2xs">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-amber-600">
              A Vencer
            </span>
            <div className="p-1.5 bg-amber-50 text-amber-600 rounded-md">
              <Clock className="w-3.5 h-3.5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-2xl font-bold text-amber-700">{vencendo}</span>
            <p className="text-[11px] text-slate-500 mt-0.5">Nos próximos 15 dias</p>
          </div>
        </div>

        {/* Licenças Vencidas */}
        <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-2xs">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-red-600">
              Vencidas
            </span>
            <div className="p-1.5 bg-red-50 text-red-600 rounded-md">
              <AlertCircle className="w-3.5 h-3.5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-2xl font-bold text-red-700">{vencidas}</span>
            <p className="text-[11px] text-slate-500 mt-0.5">Expiradas pelo prazo</p>
          </div>
        </div>

        {/* Licenças Bloqueadas */}
        <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-2xs">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-rose-600">
              Bloqueadas
            </span>
            <div className="p-1.5 bg-rose-50 text-rose-600 rounded-md">
              <ShieldAlert className="w-3.5 h-3.5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-2xl font-bold text-rose-700">{bloqueadas}</span>
            <p className="text-[11px] text-slate-500 mt-0.5">Suspensas pelo admin</p>
          </div>
        </div>

        {/* Dispositivos Conectados */}
        <div className="bg-white p-4 rounded-xl border border-slate-200 shadow-2xs">
          <div className="flex items-center justify-between">
            <span className="text-[11px] font-semibold uppercase tracking-wider text-blue-600">
              Máquinas
            </span>
            <div className="p-1.5 bg-blue-50 text-blue-600 rounded-md">
              <Monitor className="w-3.5 h-3.5" />
            </div>
          </div>
          <div className="mt-3">
            <span className="text-2xl font-bold text-blue-700">{totalDispositivosAtivos}</span>
            <p className="text-[11px] text-slate-500 mt-0.5">Instalações ativas</p>
          </div>
        </div>
      </div>

      {/* Main Grid: Recent Licenses Table + Recent Clients list */}
      <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
        {/* Tabela de Licenças Recentes (3 colunas em lg) */}
        <div className="lg:col-span-3 bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
          <div className="px-6 py-4 border-b border-slate-100 flex items-center justify-between">
            <div>
              <h3 className="text-sm font-bold text-slate-900">Licenças Recentes</h3>
              <p className="text-xs text-slate-400 mt-0.5">Últimas licenças cadastradas e monitoradas</p>
            </div>
            <button
              onClick={() => onNavigate("licencas")}
              className="flex items-center gap-1 text-xs font-semibold text-blue-600 hover:text-blue-700 transition"
            >
              <span>Ver todas</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </button>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left border-collapse text-xs">
              <thead>
                <tr className="bg-slate-50/75 border-b border-slate-100 text-slate-500 font-semibold uppercase text-[10px] tracking-wider">
                  <th className="py-3 px-4">Cliente / Empresa</th>
                  <th className="py-3 px-4">Código da Licença</th>
                  <th className="py-3 px-4">Início</th>
                  <th className="py-3 px-4">Vencimento</th>
                  <th className="py-3 px-4 text-center">Status</th>
                  <th className="py-3 px-4 text-center">Dispositivos</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100 text-slate-700">
                {recentes.length === 0 ? (
                  <tr>
                    <td colSpan={6} className="py-8 text-center text-slate-400">
                      Nenhuma licença cadastrada ainda. Clique em "Gerar Nova Licença" para começar.
                    </td>
                  </tr>
                ) : (
                  recentes.map((lic) => {
                    return (
                      <tr key={lic.id} className="hover:bg-slate-50/60 transition duration-75">
                        <td className="py-3 px-4">
                          <p className="font-semibold text-slate-900">{lic.clienteNome}</p>
                          <p className="text-[11px] text-slate-400">{lic.empresa || "—"}</p>
                        </td>
                        <td className="py-3 px-4">
                          <div className="inline-flex items-center gap-1.5 font-mono text-xs bg-slate-100 px-2 py-0.5 rounded text-slate-800">
                            <span>{lic.codigoFormatado}</span>
                            <button
                              onClick={() => handleCopy(lic.codigoFormatado)}
                              className="text-slate-400 hover:text-slate-600 p-0.5 rounded"
                              title="Copiar código"
                            >
                              {copiedKey === lic.codigoFormatado ? (
                                <Check className="w-3 h-3 text-emerald-600" />
                              ) : (
                                <Copy className="w-3 h-3" />
                              )}
                            </button>
                          </div>
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
                          <span className="inline-flex items-center gap-1 font-mono text-xs text-slate-600 bg-slate-100 px-2 py-0.5 rounded">
                            <Monitor className="w-3 h-3 text-slate-400" />
                            <span>
                              {lic.dispositivosAtivosCount || 0}/{lic.limiteDispositivos}
                            </span>
                          </span>
                        </td>
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </div>

        {/* Clientes Recentes (1 coluna em lg) */}
        <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden flex flex-col justify-between">
          <div>
            <div className="px-5 py-4 border-b border-slate-100 flex items-center justify-between">
              <div>
                <h3 className="text-sm font-bold text-slate-900">Clientes Recentes</h3>
                <p className="text-xs text-slate-400 mt-0.5">Últimos cadastros realizados</p>
              </div>
              <button
                onClick={() => onNavigate("clientes")}
                className="text-xs font-semibold text-blue-600 hover:text-blue-700"
              >
                Ver todos
              </button>
            </div>

            <div className="p-4 space-y-3">
              {clientesRecentes.length === 0 ? (
                <p className="text-xs text-center text-slate-400 py-6">
                  Nenhum cliente cadastrado.
                </p>
              ) : (
                clientesRecentes.map((cli) => (
                  <div
                    key={cli.id}
                    className="p-3 rounded-lg border border-slate-100 bg-slate-50/50 hover:bg-slate-50 transition"
                  >
                    <div className="flex items-center justify-between">
                      <p className="text-xs font-bold text-slate-900 truncate">
                        {cli.nomeResponsavel}
                      </p>
                      <span className="text-[10px] font-semibold text-slate-500 bg-white border border-slate-200 px-1.5 py-0.5 rounded">
                        {cli.totalLicencas || 0} lic.
                      </span>
                    </div>
                    <p className="text-[11px] text-slate-500 truncate mt-0.5">{cli.empresa}</p>
                    <p className="text-[10px] text-slate-400 truncate mt-1">{cli.email}</p>
                  </div>
                ))
              )}
            </div>
          </div>

          <div className="p-4 bg-slate-50 border-t border-slate-100">
            <button
              onClick={onOpenNovoCliente}
              className="w-full flex items-center justify-center gap-2 px-3 py-2 text-xs font-semibold text-slate-700 bg-white hover:bg-slate-100 border border-slate-200 rounded-lg transition"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>Novo Cadastro</span>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
};
