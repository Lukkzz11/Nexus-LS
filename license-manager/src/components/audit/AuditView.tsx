import React, { useState, useEffect } from "react";
import { History, Search, Shield, Filter, Calendar, Clock, User, FileText } from "lucide-react";
import { HistoricoAuditoria, TipoAcaoAuditoria } from "../../types";
import { listarAuditoria } from "../../services/licenseService";

export const AuditView: React.FC = () => {
  const [logs, setLogs] = useState<HistoricoAuditoria[]>([]);
  const [carregando, setCarregando] = useState(true);
  const [busca, setBusca] = useState("");
  const [filtroTipo, setFiltroTipo] = useState<string>("TODOS");

  const carregar = async () => {
    setCarregando(true);
    try {
      const lista = await listarAuditoria(150);
      setLogs(lista);
    } catch (err) {
      console.error("Erro ao carregar auditoria:", err);
    } finally {
      setCarregando(false);
    }
  };

  useEffect(() => {
    carregar();
  }, []);

  const labelsAcao: Record<TipoAcaoAuditoria, { label: string; cor: string }> = {
    CRIACAO_LICENCA: { label: "Emissão de Licença", cor: "bg-blue-50 text-blue-700 border-blue-200" },
    ATIVACAO_DISPOSITIVO: { label: "Ativação de Máquina", cor: "bg-emerald-50 text-emerald-700 border-emerald-200" },
    RENOVACAO: { label: "Renovação", cor: "bg-teal-50 text-teal-700 border-teal-200" },
    BLOQUEIO: { label: "Suspensão / Bloqueio", cor: "bg-rose-50 text-rose-700 border-rose-200" },
    DESBLOQUEIO: { label: "Desbloqueio", cor: "bg-indigo-50 text-indigo-700 border-indigo-200" },
    CANCELAMENTO: { label: "Cancelamento Definitivo", cor: "bg-slate-100 text-slate-700 border-slate-300" },
    REMOCAO_DISPOSITIVO: { label: "Remoção de Máquina", cor: "bg-amber-50 text-amber-800 border-amber-200" },
    CRIACAO_CLIENTE: { label: "Cadastro de Cliente", cor: "bg-purple-50 text-purple-700 border-purple-200" },
    EDICAO_CLIENTE: { label: "Atualização de Cliente", cor: "bg-sky-50 text-sky-700 border-sky-200" },
    CRIACAO_ADMINISTRADOR: { label: "Novo Administrador", cor: "bg-red-50 text-red-700 border-red-200" },
  };

  const filtrados = logs.filter((l) => {
    const termo = busca.toLowerCase();
    const bateTexto =
      (l.codigoLicenca && l.codigoLicenca.toLowerCase().includes(termo)) ||
      (l.clienteNome && l.clienteNome.toLowerCase().includes(termo)) ||
      l.responsavel.toLowerCase().includes(termo) ||
      JSON.stringify(l.detalhes || {}).toLowerCase().includes(termo);

    const bateTipo = filtroTipo === "TODOS" || l.tipoAcao === filtroTipo;

    return bateTexto && bateTipo;
  });

  return (
    <div className="space-y-6">
      {/* Top Bar with Filter */}
      <div className="bg-white rounded-xl p-4 border border-slate-200 shadow-2xs flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div className="relative flex-1 max-w-md">
          <Search className="w-4 h-4 text-slate-400 absolute left-3 top-1/2 -translate-y-1/2" />
          <input
            type="text"
            value={busca}
            onChange={(e) => setBusca(e.target.value)}
            placeholder="Buscar por licença, cliente, operador ou detalhe..."
            className="w-full text-xs pl-9 pr-4 py-2 border border-slate-200 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500/20 focus:border-blue-600 transition"
          />
        </div>

        <div className="flex items-center gap-3">
          <select
            value={filtroTipo}
            onChange={(e) => setFiltroTipo(e.target.value)}
            className="text-xs px-3 py-2 bg-slate-50 border border-slate-200 rounded-lg focus:outline-none focus:border-blue-600 transition font-medium text-slate-700"
          >
            <option value="TODOS">Todos os Eventos</option>
            <option value="CRIACAO_LICENCA">Emissões de Licença</option>
            <option value="ATIVACAO_DISPOSITIVO">Ativações de Máquinas</option>
            <option value="RENOVACAO">Renovações</option>
            <option value="BLOQUEIO">Bloqueios</option>
            <option value="DESBLOQUEIO">Desbloqueios</option>
            <option value="CANCELAMENTO">Cancelamentos</option>
            <option value="REMOCAO_DISPOSITIVO">Remoção de Máquinas</option>
            <option value="CRIACAO_CLIENTE">Cadastros de Clientes</option>
          </select>

          <button
            onClick={carregar}
            className="px-3 py-2 text-xs font-semibold text-slate-700 bg-slate-50 hover:bg-slate-100 border border-slate-200 rounded-lg transition"
          >
            Recarregar
          </button>
        </div>
      </div>

      {/* Audit Log Table */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-2xs overflow-hidden">
        <div className="overflow-x-auto">
          <table className="w-full text-left border-collapse text-xs">
            <thead>
              <tr className="bg-slate-50/75 border-b border-slate-100 text-slate-500 font-semibold uppercase text-[10px] tracking-wider">
                <th className="py-3 px-4">Data / Horário</th>
                <th className="py-3 px-4">Tipo de Ação</th>
                <th className="py-3 px-4">Licença / Referência</th>
                <th className="py-3 px-4">Cliente Vinculado</th>
                <th className="py-3 px-4">Operador / Origem</th>
                <th className="py-3 px-4">Detalhes do Evento</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100 text-slate-700">
              {carregando ? (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-400">
                    Carregando trilha de auditoria imutável...
                  </td>
                </tr>
              ) : filtrados.length === 0 ? (
                <tr>
                  <td colSpan={6} className="py-12 text-center text-slate-400">
                    Nenhum registro de auditoria encontrado.
                  </td>
                </tr>
              ) : (
                filtrados.map((log) => {
                  const dataStr = log.dataHora?.toDate
                    ? log.dataHora.toDate().toLocaleString("pt-BR")
                    : "—";

                  const conf = labelsAcao[log.tipoAcao] || {
                    label: log.tipoAcao,
                    cor: "bg-slate-100 text-slate-700 border-slate-200",
                  };

                  return (
                    <tr key={log.id} className="hover:bg-slate-50/60 transition duration-75">
                      <td className="py-3 px-4 font-mono text-[11px] text-slate-500 whitespace-nowrap">
                        {dataStr}
                      </td>
                      <td className="py-3 px-4 whitespace-nowrap">
                        <span
                          className={`inline-block px-2 py-0.5 rounded text-[11px] font-semibold border ${conf.cor}`}
                        >
                          {conf.label}
                        </span>
                      </td>
                      <td className="py-3 px-4 font-mono text-slate-800 font-medium">
                        {log.codigoLicenca || "—"}
                      </td>
                      <td className="py-3 px-4 font-medium text-slate-800">
                        {log.clienteNome || "—"}
                      </td>
                      <td className="py-3 px-4 text-slate-600">
                        {log.responsavel}
                      </td>
                      <td className="py-3 px-4">
                        <div className="font-mono text-[11px] text-slate-600 bg-slate-50 border border-slate-100 px-2 py-1 rounded max-w-md truncate">
                          {log.detalhes ? JSON.stringify(log.detalhes) : "—"}
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
    </div>
  );
};
